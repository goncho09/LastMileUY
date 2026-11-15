param(
    [string]$TerraformDir = (Join-Path (Get-Location) 'infra/terraform'),
    [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
    [string]$SshUser = 'azureuser'
)

$ErrorActionPreference = 'Stop'
foreach ($name in @('terraform','ssh')) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "Falta el comando $name" }
}

if (-not (Test-Path $TerraformDir)) { throw "No existe: $TerraformDir" }
if (-not (Test-Path $SshKey)) { throw "No existe la clave privada: $SshKey" }

$TerraformDir = (Resolve-Path $TerraformDir).Path
# IPv4 externa de esta computadora. No se almacena en archivos de codigo.
$publicIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 15).ToString().Trim()
$parsed = $null

if (-not [System.Net.IPAddress]::TryParse($publicIp, [ref]$parsed) -or
    $parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "No se pudo determinar una IPv4 publica valida: $publicIp"
}

$cidr = "$publicIp/32"
Write-Host "IP detectada: $cidr"

# Variable de entorno tiene precedencia menor que tfvars: usamos -var para sobreescribirla.
$planFile = Join-Path $TerraformDir 'ssh-current-ip.tfplan'

try {
    Write-Host "Verificando cambios en Azure..." -ForegroundColor Cyan

    $planLog = Join-Path $TerraformDir 'ssh-plan.log'
    $applyLog = Join-Path $TerraformDir 'ssh-apply.log'

    # Guardar salida y errores en archivos separados
    $planArgs = @(
        "-chdir=$TerraformDir"
        "plan"
        "-var=ssh_allowed_cidr=$cidr"
        "-out=$planFile"
        "-no-color"
        "-input=false"
    )

    $process = Start-Process -FilePath "terraform" `
        -ArgumentList $planArgs `
        -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $planLog `
        -RedirectStandardError "$planLog.err"

    if ($process.ExitCode -ne 0) {
        Get-Content "$planLog.err"
        Get-Content $planLog
        throw 'Terraform plan fallo. No se aplicaron cambios.'
    }

    # Leer cambios del plan
    $jsonFile = Join-Path $TerraformDir 'ssh-plan.json'

    $showProcess = Start-Process -FilePath "terraform" `
        -ArgumentList @("-chdir=$TerraformDir", "show", "-json", $planFile) `
        -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $jsonFile `
        -RedirectStandardError "$jsonFile.err"

    if ($showProcess.ExitCode -ne 0) {
        Get-Content "$jsonFile.err"
        throw 'No se pudo leer el plan.'
    }

    $plan = Get-Content $jsonFile -Raw | ConvertFrom-Json

    $changes = @($plan.resource_changes | Where-Object {
        $_.change.actions -notcontains 'no-op'
    })

    foreach ($change in $changes) {
        $actions = $change.change.actions -join ','
        Write-Host "$($change.address): $actions"
    }

    if ($changes.Count -eq 0) {
        Write-Host "Sin cambios necesarios." -ForegroundColor Green
    }

    # Bloquear cualquier eliminacion o reemplazo
    $dangerous = @($changes | Where-Object {
        $_.change.actions -contains 'delete'
    })

    if ($dangerous.Count -gt 0) {
        throw 'El plan contiene eliminaciones o reemplazos. NO aplicar.'
    }

    # Solo permitir cambios en el NSG
    $unexpected = @($changes | Where-Object {
        $_.address -ne 'azurerm_network_security_group.lastmileuy'
    })

    if ($unexpected.Count -gt 0) {
        throw 'El plan modifica recursos inesperados. NO aplicar.'
    }

    if ($changes.Count -eq 0) {
        Write-Host 'SSH ya configurado.' -ForegroundColor Green
    }
    else {
        $answer = Read-Host 'Aplicar cambios SSH? (SI/no)'

        if ($answer.Trim() -ine 'SI') {
            throw 'Cancelado sin aplicar cambios.'
        }

        Write-Host "Aplicando configuracion SSH..." -ForegroundColor Cyan

        $applyProcess = Start-Process -FilePath "terraform" `
            -ArgumentList @(
                "-chdir=$TerraformDir"
                "apply"
                "-no-color"
                "-input=false"
                $planFile
            ) `
            -Wait -PassThru -NoNewWindow `
            -RedirectStandardOutput $applyLog `
            -RedirectStandardError "$applyLog.err"

        if ($applyProcess.ExitCode -ne 0) {
            Get-Content "$applyLog.err"
            Get-Content $applyLog
            throw 'Terraform apply fallo.'
        }

        # Persistir tambien en tfvars: de lo contrario el siguiente plan revertiria la regla.
        $tfvarsPath=Join-Path $TerraformDir 'terraform.tfvars'
        $tfvars=Get-Content -LiteralPath $tfvarsPath -Raw
        if ($tfvars -notmatch '(?m)^\s*ssh_allowed_cidr\s*=\s*"[^"\r\n]+"') {
            throw 'SSH aplicado, pero no se pudo localizar ssh_allowed_cidr en terraform.tfvars. Actualizarlo manualmente antes de otro apply.'
        }
        $newTfvars=[regex]::Replace($tfvars, '(?m)^(\s*ssh_allowed_cidr\s*=\s*)"[^"\r\n]+"', {
            param($match) $match.Groups[1].Value + '"' + $cidr + '"'
        })
        [IO.File]::WriteAllText($tfvarsPath,$newTfvars,(New-Object Text.UTF8Encoding($false)))
        Write-Host "Configuracion SSH aplicada y terraform.tfvars sincronizado." -ForegroundColor Green
    }
}
finally {
    Remove-Item -LiteralPath $planFile -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $TerraformDir 'ssh-plan.json') -Force -ErrorAction SilentlyContinue
}


$ip = (& terraform "-chdir=$TerraformDir" output -raw public_ip).Trim()

if ($LASTEXITCODE -ne 0 -or -not $ip) { throw 'No se pudo leer la IP publica de la VM.' }


Write-Host "Comprobando SSH: ${ip}:22" -ForegroundColor Cyan

& ssh -i $SshKey -o BatchMode=yes -o ConnectTimeout=12 -o StrictHostKeyChecking=accept-new "$SshUser@$ip" 'echo SSH_OK'

if ($LASTEXITCODE -ne 0) {
    throw 'SSH no respondio. NO destruir.'
}

Write-Host "`nComando para conectarte:" -ForegroundColor Cyan
Write-Host "ssh -i `"$SshKey`" $SshUser@$ip" -ForegroundColor Yellow

if ($LASTEXITCODE -ne 0) {
    throw 'La regla se aplico pero SSH no respondio. Revisar firewall de Ubuntu, VM y ruta de red. NO destruir.'
}

Write-Host 'SSH directo funcionando.' -ForegroundColor Green

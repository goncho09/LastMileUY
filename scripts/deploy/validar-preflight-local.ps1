$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-ExitCode([string]$Paso) {
    if ($LASTEXITCODE -ne 0) { throw "$Paso falló (código $LASTEXITCODE)." }
}
function Require-Command([string]$Nombre) {
    if (-not (Get-Command $Nombre -ErrorAction SilentlyContinue)) {
        throw "Falta instalar o agregar al PATH: $Nombre"
    }
}

$raiz = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $raiz
try {
    Write-Host 'Comprobando herramientas locales...'
    foreach ($cmd in @('git', 'az', 'terraform', 'docker', 'ssh', 'scp')) {
        Require-Command $cmd
    }
    if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'Se requiere PowerShell 5.1 o superior.' }

    Write-Host 'Comprobando repositorio Git...'

    # Evitar problemas de codificación con rutas que contienen tildes.
    $salidaAnterior = [Console]::OutputEncoding

    try {
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

        $repoRoot = (& git rev-parse --show-toplevel)
        Assert-ExitCode 'Git rev-parse'
    }
    finally {
        [Console]::OutputEncoding = $salidaAnterior
    }

    $repoNormalizado = [IO.Path]::GetFullPath(
            $repoRoot.Trim().Replace('/', '\')
    ).TrimEnd('\')

    $raizNormalizada = [IO.Path]::GetFullPath(
            $raiz.Replace('/', '\')
    ).TrimEnd('\')

    if (-not [string]::Equals(
            $repoNormalizado,
            $raizNormalizada,
            [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Repositorio incorrecto: $repoNormalizado"
    }
    $version = (& git rev-parse HEAD).Trim()
    Assert-ExitCode 'Git rev-parse HEAD'
    if ($version -notmatch '^[0-9a-f]{40}$') { throw 'No se pudo obtener el commit completo.' }
    $branch = (& git branch --show-current).Trim()
    Assert-ExitCode 'Git branch'
    if ($branch -ne 'main') { throw "La rama actual es '$branch', se esperaba 'main'." }
    $dirty = @(& git status --porcelain --untracked-files=no)
    Assert-ExitCode 'Git status'
    if ($dirty.Count -gt 0) {
        throw 'Hay cambios locales en archivos rastreados. Revisalos antes del destroy.'
    }
    & git fetch origin main --quiet
    Assert-ExitCode 'Git fetch origin main'
    $remote = (& git rev-parse origin/main).Trim()
    Assert-ExitCode 'Git rev-parse origin/main'
    if ($version -ne $remote) { throw 'main local no coincide con origin/main. Ejecutá git pull --ff-only origin main.' }

    Write-Host 'Comprobando sesión de Azure...'
    $accountJson = & az account show --output json 2>$null
    Assert-ExitCode 'Azure CLI: az account show (ejecutá az login si falla)'
    $account = ($accountJson | Out-String | ConvertFrom-Json)
    if (-not $account.id -or -not $account.tenantId) { throw 'No hay suscripción activa válida en Azure CLI.' }
    Write-Host "Suscripción Azure: $($account.name) [$($account.id)]"
    Write-Host 'Confirmá que esta es la suscripción donde está desplegado LastMileUY.'

    Write-Host 'Comprobando Docker Desktop...'
    & docker info --format '{{.ServerVersion}}' | Out-Null
    Assert-ExitCode 'Docker Engine'
    & docker compose version | Out-Null
    Assert-ExitCode 'Docker Compose'

    Write-Host 'Validando scripts PowerShell...'
    $parseErrors = @()
    Get-ChildItem (Join-Path $raiz 'scripts/deploy') -Filter '*.ps1' -File | ForEach-Object {
        $tokens = $null
        $fileErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$fileErrors) | Out-Null
        if ($fileErrors) { $parseErrors += $fileErrors }
    }
    if ($parseErrors.Count -gt 0) {
        $parseErrors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw 'Error de sintaxis PowerShell.'
    }

    Write-Host 'Validando scripts Bash con Docker...'
    & docker run --rm -v "${raiz}:/workspace:ro" -w /workspace bash:5.2 -c 'for f in scripts/deploy/*.sh; do bash -n "$f" || exit 1; done'
    Assert-ExitCode 'Validación Bash'

    Write-Host 'Validando archivos esenciales...'
    foreach ($relative in @('compose.yaml','compose.deploy.yaml','docker-compose.azure.yml','infra/terraform','scripts/deploy/main-azure.ps1')) {
        if (-not (Test-Path (Join-Path $raiz $relative))) { throw "Falta: $relative" }
    }
    $sshKey = Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'
    if (-not (Test-Path -LiteralPath $sshKey -PathType Leaf)) {
        throw "No se encontró la clave SSH esperada: $sshKey"
    }

    Write-Host "Validando Terraform..."

    Push-Location (Join-Path $raiz "infra/terraform")

    try {
        $salida = & terraform init -input=false -no-color 2>&1
        if ($LASTEXITCODE -ne 0) {
            $salida | Out-Host
            throw "Terraform init falló"
        }

        Write-Host "  Terraform init: OK" -ForegroundColor Green

        $salida = & terraform validate -no-color 2>&1
        if ($LASTEXITCODE -ne 0) {
            $salida | Out-Host
            throw "Terraform validate falló"
        }

        Write-Host "  Terraform validate: OK" -ForegroundColor Green

        Write-Host "Comprobando cambios de infraestructura..."

        $salida = & terraform plan -input=false -no-color -detailed-exitcode 2>&1
        $codigo = $LASTEXITCODE

        switch ($codigo) {
            0 {
                Write-Host "  Terraform plan: sin cambios" -ForegroundColor Green
            }
            2 {
                $salida | Out-Host
                throw "Terraform detectó cambios. Revisar antes del destroy."
            }
            default {
                $salida | Out-Host
                throw "Terraform plan falló."
            }
        }
    }
    finally {
        Pop-Location
    }

    Write-Host 'Validando Docker Compose...'
    $placeholders = @{
        LASTMILEUY_VERSION = $version
        GRAFANA_ADMIN_PASSWORD = 'preflight-placeholder'
        RABBITMQ_PASSWORD = 'preflight-placeholder'
        URL_SEGUIMIENTO = 'https://seguimiento.example.com'
    }
    $original = @{}
    try {
        foreach ($key in $placeholders.Keys) {
            $original[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            if ([string]::IsNullOrWhiteSpace($original[$key])) {
                [Environment]::SetEnvironmentVariable($key, $placeholders[$key], 'Process')
            }
        }
        & docker compose -f compose.yaml -f compose.deploy.yaml -f docker-compose.azure.yml config --quiet
        Assert-ExitCode 'Docker Compose config'
    }
    finally {
        foreach ($key in $original.Keys) {
            [Environment]::SetEnvironmentVariable($key, $original[$key], 'Process')
        }
    }
    Write-Host ''
    Write-Host 'PREFLIGHT_LOCAL_OK' -ForegroundColor Green
}
finally {
    Pop-Location
}

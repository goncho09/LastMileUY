#requires -Version 5.1
[CmdletBinding()]
param(
  [switch]$Destroy,
  [string]$ResumeBackupDir,
  [string]$BackupRoot = (Join-Path $HOME 'LastMileUY-Azure/backups'),
  [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
  [string]$EnvFile = (Join-Path $HOME 'LastMileUY-Azure/config/.env.azure')

)

function Mostrar-Accesos([string]$ip, [string]$key) {
  $hostIp=$ip -replace '\.', '-'
  Write-Host "`n========== ACCESOS LASTMILEUY ==========" -ForegroundColor Green
  Write-Host "SSH: ssh -i `"$key`" azureuser@$ip"
  foreach ($name in @('api','backoffice','comercio','repartidor','seguimiento','receptor')) {
    Write-Host ("{0}: https://{1}.{2}.sslip.io" -f $name,$name,$hostIp)
  }
  Write-Host '========================================'
}
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$tf=Join-Path $root 'infra/terraform'
$restore=Join-Path $PSScriptRoot 'restaurar-azure.ps1'
$backupScript=Join-Path $PSScriptRoot 'respaldar-azure.ps1'
$installer=Join-Path $PSScriptRoot 'instalar-azure.sh'
$remoteRestore=Join-Path $PSScriptRoot 'restaurar-vm.sh'

# Diario persistente local: sobrevivira aunque se cierre PowerShell a mitad del destroy.
$journalPath=Join-Path $BackupRoot '.lastmileuy-reconstruccion-pendiente.json'
function Save-Journal([string]$backupPath,[string]$phase) {
  if (-not (Test-Path -LiteralPath $BackupRoot)) { New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null }
  $data=[ordered]@{ backup=$backupPath; phase=$phase; updatedUtc=[DateTime]::UtcNow.ToString('o') }
  $tmp="$journalPath.tmp"
  [IO.File]::WriteAllText($tmp,($data | ConvertTo-Json -Compress),(New-Object Text.UTF8Encoding($false)))
  Move-Item -LiteralPath $tmp -Destination $journalPath -Force
}
if (Test-Path -LiteralPath $journalPath) {
  $journal=Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
  if ($Destroy) {
    throw "Hay una reconstruccion pendiente ($($journal.phase)): $($journal.backup). NO repetir destroy. Usar -ResumeBackupDir con esa carpeta."
  }
  if (-not [string]::IsNullOrWhiteSpace($ResumeBackupDir)) {
    $wanted=(Resolve-Path -LiteralPath $ResumeBackupDir).Path
    if ($wanted -ne $journal.backup) {
      throw "El respaldo elegido no coincide con la reconstruccion pendiente: $($journal.backup)"
    }
  }
}

function TF([string[]]$TerraformArgs) {
  # No redirigir stderr de ejecutables nativos: PowerShell 5.1 puede convertir
  # advertencias normales en errores terminantes con ErrorActionPreference Stop.
  & terraform "-chdir=$tf" @TerraformArgs
  $code=$LASTEXITCODE
  if ($code -ne 0) { throw "Terraform fallo: $($TerraformArgs -join ' ') (codigo $code)" }
}
function Invoke-Remote([string]$target,[string[]]$sshOptions,[string]$command,[string]$label) {
  & ssh @sshOptions $target $command
  if ($LASTEXITCODE -ne 0) { throw "$label fallo (codigo $LASTEXITCODE)" }
}
function Wait-SSH([string]$target,[string[]]$sshOptions) {
  for($i=0;$i -lt 60;$i++) {
    $oldPreference=$ErrorActionPreference
    try {
      $ErrorActionPreference='Continue'
      & ssh @sshOptions $target 'echo SSH_OK' 2>$null
      $exitCode=$LASTEXITCODE
    } finally { $ErrorActionPreference=$oldPreference }
    if ($exitCode -eq 0) { return }
    Start-Sleep -Seconds 10
  }
  throw "La VM no responde por SSH: $target. Se puede reanudar con el mismo backup."
}

foreach($cmd in @('terraform','ssh','scp')) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Falta $cmd" }
}
foreach($path in @($tf,$SshKey,$EnvFile,$restore,$backupScript,$installer,$remoteRestore)) {
  if (-not (Test-Path -LiteralPath $path)) { throw "Falta $path" }
}
foreach($name in @('terraform.tfstate','terraform.tfvars')) {
  if (-not (Test-Path -LiteralPath (Join-Path $tf $name))) { throw "Falta $name en $tf" }
}

# La VM actual configurada no es una SKU que podamos dar por gratuita.
$tfvars=Get-Content -LiteralPath (Join-Path $tf 'terraform.tfvars') -Raw
if ($tfvars -notmatch '(?m)^\s*vm_size\s*=\s*"([^"]+)"') { throw 'No se pudo determinar vm_size desde terraform.tfvars' }
$vmSize=$Matches[1]

if ($vmSize -eq 'Standard_D2as_v4') {
  Write-Warning 'La VM configurada es Standard_D2as_v4; no es elegible para asumir cobertura Azure Free.'
}

# La IP publica puede cambiar. Solo en modo Destroy se actualiza la regla SSH.
# La regla se aplica de forma aislada antes del backup: no abrir SSH a Internet.
$sshNeedsUpdate=$false
if ($tfvars -notmatch '(?m)^\s*ssh_allowed_cidr\s*=\s*"([^"/]+)/32"') { throw 'No se pudo leer ssh_allowed_cidr de terraform.tfvars' }
$sshAllowedIp=$Matches[1]
if ($Destroy) {
  try {
    $publicIpv4=([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10)).Trim()
  } catch { throw 'No se pudo consultar la IPv4 publica. No se modifica Azure.' }
  $parsedIp=$null
  if (-not [Net.IPAddress]::TryParse($publicIpv4,[ref]$parsedIp) -or
      $parsedIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
    throw "IPv4 publica inesperada: $publicIpv4"
  }
  $sshNeedsUpdate=($publicIpv4 -ne $sshAllowedIp)
  if ($sshNeedsUpdate) {
    $tfvarsPath=Join-Path $tf 'terraform.tfvars'
    $newTfvars=[regex]::Replace($tfvars, '(?m)^(\s*ssh_allowed_cidr\s*=\s*)"[^"/]+/32"', {
      param($m) $m.Groups[1].Value + '"' + $publicIpv4 + '/32"'
    })
    if ($newTfvars -eq $tfvars) { throw 'No se pudo actualizar ssh_allowed_cidr' }
    [IO.File]::WriteAllText($tfvarsPath,$newTfvars,(New-Object Text.UTF8Encoding($false)))
    Write-Host "IP publica actualizada en terraform.tfvars: $sshAllowedIp/32 -> $publicIpv4/32" -ForegroundColor Cyan
  } else {
    Write-Host "Regla SSH configurada para la IP publica actual: $publicIpv4/32" -ForegroundColor Green
  }
}

TF -TerraformArgs @('init','-input=false')
TF -TerraformArgs @('validate')
if (-not [string]::IsNullOrWhiteSpace($ResumeBackupDir)) {
  if ($Destroy) { throw 'No combinar -ResumeBackupDir con -Destroy' }
  if (-not (Test-Path -LiteralPath $ResumeBackupDir -PathType Container)) { throw 'No existe el respaldo de reanudacion' }
  & $restore -BackupDir $ResumeBackupDir -ValidateOnly
  if (-not $?) { throw 'Validacion de respaldo fallida' }
  # Plan guardado e inspeccionado: una reanudacion JAMAS reemplaza/elimina recursos.
  $resumePlan=Join-Path $env:TEMP ('lastmileuy-resume-' + [guid]::NewGuid().ToString('N') + '.tfplan')
  try {
    TF -TerraformArgs @('plan','-input=false','-no-color',"-out=$resumePlan")
    $planJson=@(& terraform "-chdir=$tf" show -json $resumePlan)
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo inspeccionar el plan de reanudacion' }
    $plan=($planJson -join "`n") | ConvertFrom-Json
    $destructive=@($plan.resource_changes | Where-Object { @($_.change.actions) -contains 'delete' })
    if ($destructive.Count -ne 0) {
      throw 'REANUDACION BLOQUEADA: Terraform quiere destruir o reemplazar recursos. Inspeccionar el plan manualmente.'
    }
    $answer=Read-Host 'Escribi CONTINUAR para aplicar sin destrucciones y reanudar'
    if ($answer -cne 'CONTINUAR') { throw 'Reanudacion cancelada' }
    TF -TerraformArgs @('apply','-input=false',$resumePlan)
  } finally { Remove-Item -LiteralPath $resumePlan -Force -ErrorAction SilentlyContinue }
  $newIp=(& terraform "-chdir=$tf" output -raw public_ip)
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($newIp)) { throw 'Sin IP de VM' }
  $newDest="azureuser@$($newIp.Trim())"
  $sshOpts=@('-i',$SshKey,'-o','BatchMode=yes','-o','StrictHostKeyChecking=accept-new','-o','ConnectTimeout=15')
  Wait-SSH $newDest $sshOpts
  Invoke-Remote $newDest $sshOpts 'sudo cloud-init status --wait' 'Cloud-init'
  # El primer intento puede haberse interrumpido durante el instalador.
  & ssh @sshOpts $newDest 'test -w /opt/lastmileuy/.git && test -f /etc/systemd/system/lastmileuy-update.timer && command -v caddy >/dev/null && docker compose version >/dev/null'
  $projectReady=($LASTEXITCODE -eq 0)
  if (-not $projectReady) {
    & scp @sshOpts $installer "${newDest}:/home/azureuser/instalar-azure.sh"
    if ($LASTEXITCODE -ne 0) { throw 'Copia del instalador fallida' }
    Invoke-Remote $newDest $sshOpts 'sudo bash /home/azureuser/instalar-azure.sh' 'Instalacion base'
  } else { Write-Host 'Instalacion base ya disponible; se conserva la restauracion parcial.' }
  & $restore -BackupDir $ResumeBackupDir -TerraformDir $tf -SshKey $SshKey -EnvFile $EnvFile -MaxAgeMinutes 1000000 -Resume
  if (-not $?) { throw 'Restauracion reanudada fallida' }
  if (Test-Path -LiteralPath $journalPath) { Remove-Item -LiteralPath $journalPath -Force }
  Mostrar-Accesos $newIp $SshKey
  Write-Host 'REANUDACION_OK (sin ejecutar destroy)' -ForegroundColor Green
  return
}
if ($Destroy -and $sshNeedsUpdate) {
  # El plan aislado debe modificar exclusivamente el NSG existente.
  # No aplicar ningun otro recurso ni cambios destructivos durante este paso.
  $planFile=Join-Path $env:TEMP ('lastmileuy-ssh-' + [guid]::NewGuid().ToString('N') + '.tfplan')
  try {
    TF -TerraformArgs @('plan','-input=false','-no-color','-target=azurerm_network_security_group.lastmileuy',"-out=$planFile")
    $planJson=@(& terraform "-chdir=$tf" show -json $planFile)
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo inspeccionar el plan SSH' }
    $plan=($planJson -join "`n") | ConvertFrom-Json
    $changes=@($plan.resource_changes | Where-Object { (@($_.change.actions) -join ',') -ne 'no-op' })
    if ($changes.Count -ne 1 -or
        $changes[0].address -ne 'azurerm_network_security_group.lastmileuy' -or
        (@($changes[0].change.actions) -join ',') -ne 'update') {
      throw 'Plan SSH inesperado: no se aplicara ningun cambio'
    }
    $sshRules=@($changes[0].change.after.security_rule | Where-Object { $_.name -eq 'Allow-SSH-Restricted' })
    if ($sshRules.Count -ne 1 -or $sshRules[0].source_address_prefix -ne "$publicIpv4/32") {
      throw 'Plan SSH no contiene la IP publica esperada'
    }
    Write-Host 'Aplicando exclusivamente la regla SSH del NSG (sin recrear la VM)...' -ForegroundColor Cyan
    TF -TerraformArgs @('apply','-input=false','-auto-approve',$planFile)
  } finally {
    Remove-Item -LiteralPath $planFile -Force -ErrorAction SilentlyContinue
  }
}
Write-Host 'Plan Terraform (solo lectura; no se aplica):' -ForegroundColor Cyan
TF -TerraformArgs @('plan','-input=false','-no-color')

if (-not $Destroy) {
  $latest=Get-ChildItem -LiteralPath $BackupRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
    Sort-Object Name -Descending | Select-Object -First 1
  if ($null -ne $latest) { & $restore -BackupDir $latest.FullName -ValidateOnly }
  Write-Host 'PREFLIGHT OK: Terraform validado y plan revisado. No se modifico Azure.' -ForegroundColor Green
  Write-Host 'Para generar un backup nuevo y reconstruir: .\scripts\deploy\recrear-azure.ps1 -Destroy' -ForegroundColor Yellow
  return
}

# Backup fresco, verificado localmente, ANTES de cualquier accion destructiva.
$start=Get-Date
# Obtener IP y preparar acceso antes de iniciar el respaldo.
$ip=(& terraform "-chdir=$tf" output -raw public_ip)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ip)) { throw 'No se pudo obtener public_ip' }
$ip=$ip.Trim()
$dest="azureuser@$ip"
$sshOpts=@('-i',$SshKey,'-o','BatchMode=yes','-o','StrictHostKeyChecking=accept-new','-o','ConnectTimeout=10')
Invoke-Remote $dest $sshOpts 'echo SSH_OK' 'Comprobacion SSH previa'

$maintenanceStopped=$false
$vmDestroyed=$false
try {
  Write-Host 'Creando respaldo nuevo de PostgreSQL, RabbitMQ y volumenes...' -ForegroundColor Cyan
  & $backupScript -TerraformDir $tf -SshKey $SshKey -BackupRoot $BackupRoot
  if (-not $?) { throw "El script de respaldo fallo" }
  # El backup remoto termina con servicios detenidos; desde aqui hay que reanudarlos si algo falla.
  $maintenanceStopped=$true
  $backup=Get-ChildItem -LiteralPath $BackupRoot -Directory |
    Where-Object { $_.CreationTime -ge $start.AddMinutes(-2) -and $_.Name -match '^\d{8}-\d{6}$' } |
    Sort-Object Name -Descending | Select-Object -First 1
  if ($null -eq $backup) { throw 'No se encontro un backup nuevo. Destruccion bloqueada.' }
  # Los servicios permanecen detenidos durante validacion y confirmacion.
  & $restore -BackupDir $backup.FullName -ValidateOnly
  if (-not $?) { throw "La validacion del respaldo fallo" }
  Write-Host "RESPALDO VALIDADO: $($backup.FullName)" -ForegroundColor Green

  # respaldar-azure.ps1 deja aplicaciones, RabbitMQ, monitorizacion y timer detenidos.
  # No volver a iniciar ni detener servicios aqui.
  Write-Host 'ATENCION: Terraform destroy elimina los recursos administrados por este estado.' -ForegroundColor Red
  Write-Host 'Backup verificado y servicios detenidos. La aplicacion permanece en mantenimiento.' -ForegroundColor Yellow
  $confirmation=Read-Host 'Escribi DESTRUIR para confirmar (cualquier otra cosa cancela)'
  if ($confirmation -cne 'DESTRUIR') {
    $resumeCommand = 'for n in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus lastmileuy-lastmileuy.api-1 lastmileuy-lastmileuy.backoffice-1 lastmileuy-lastmileuy.commerceportal-1 lastmileuy-lastmileuy.driverpwa-1 lastmileuy-lastmileuy.publictracking-1 lastmileuy-lastmileuy.worker-1 lastmileuy-lastmileuy.receptoravisos-1; do docker start "$n" >/dev/null 2>&1 || true; done'
    Invoke-Remote $dest $sshOpts $resumeCommand 'Reanudar servicios tras cancelar'
    $maintenanceStopped=$false
    throw 'Cancelado sin destruir Azure'
  }

  $state=Join-Path $tf 'terraform.tfstate'
  Copy-Item -LiteralPath $state -Destination "$state.pre-destroy-$(Get-Date -Format yyyyMMdd-HHmmss)" -ErrorAction Stop
  Save-Journal $backup.FullName 'DESTROY_INICIADO'
  # Desde este momento no suponemos que la VM anterior sobreviva si Terraform falla.
  $vmDestroyed=$true
  TF -TerraformArgs @('destroy','-input=false','-auto-approve')
  Save-Journal $backup.FullName 'DESTROY_COMPLETO'
  TF -TerraformArgs @('apply','-input=false','-auto-approve')
  Save-Journal $backup.FullName 'VM_RECREADA'

  $newIp=(& terraform "-chdir=$tf" output -raw public_ip)
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($newIp)) { throw 'No se pudo obtener la nueva IP publica' }
  $newIp=$newIp.Trim()
  $newDest="azureuser@$newIp"
  Wait-SSH $newDest $sshOpts
  Invoke-Remote $newDest $sshOpts 'sudo cloud-init status --wait && docker compose version' 'Preparacion cloud-init/Docker'
  & scp @sshOpts $installer "${newDest}:/home/azureuser/instalar-azure.sh"
  if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar instalar-azure.sh' }
  Invoke-Remote $newDest $sshOpts 'sudo bash /home/azureuser/instalar-azure.sh' 'Instalacion base'

  & $restore -BackupDir $backup.FullName -TerraformDir $tf -SshKey $SshKey -EnvFile $EnvFile -Resume
  if (-not $?) { throw "La restauracion fallo" }
  if (Test-Path -LiteralPath $journalPath) { Remove-Item -LiteralPath $journalPath -Force }
  Mostrar-Accesos $newIp $SshKey
  Write-Host "RECONSTRUCCION_Y_RESTAURACION_OK: https://api.$($newIp -replace '\.','-').sslip.io" -ForegroundColor Green
}
catch {
  if ($vmDestroyed -and (Test-Path -LiteralPath $journalPath)) {
    Write-Warning 'Reconstruccion interrumpida. NO ejecutar -Destroy de nuevo.'
    Write-Warning "Para continuar: .\scripts\deploy\recrear-azure.ps1 -ResumeBackupDir '$($backup.FullName)'"
  }
  if ($maintenanceStopped -and -not $vmDestroyed) {
    Write-Warning 'La VM original parece seguir disponible. Intentando reanudar los servicios detenidos...'
    try {
      $resumeCommand = 'for n in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus lastmileuy-lastmileuy.api-1 lastmileuy-lastmileuy.backoffice-1 lastmileuy-lastmileuy.commerceportal-1 lastmileuy-lastmileuy.driverpwa-1 lastmileuy-lastmileuy.publictracking-1 lastmileuy-lastmileuy.worker-1 lastmileuy-lastmileuy.receptoravisos-1; do docker start "$n" >/dev/null 2>&1 || true; done'
      Invoke-Remote $dest $sshOpts $resumeCommand 'Reanudacion de servicios'
    } catch { Write-Warning 'No se pudieron reanudar automaticamente. Revisar la VM por SSH.' }
  }
  throw
}

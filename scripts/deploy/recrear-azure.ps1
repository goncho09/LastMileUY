#requires -Version 5.1
[CmdletBinding()]
param(
  [switch]$Destroy,
  [switch]$PermitirSkuConCosto,
  [string]$SkuAzureFreeVerificada,
  [string]$BackupRoot = (Join-Path $HOME 'LastMileUY-Azure/backups'),
  [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
  [string]$EnvFile = (Join-Path $HOME 'LastMileUY-Azure/config/.env.azure')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$tf=Join-Path $root 'infra/terraform'
$restore=Join-Path $PSScriptRoot 'restaurar-azure.ps1'
$backupScript=Join-Path $PSScriptRoot 'respaldar-azure.ps1'
$installer=Join-Path $PSScriptRoot 'instalar-azure.sh'
$remoteRestore=Join-Path $PSScriptRoot 'restaurar-vm.sh'

function TF([string[]]$arguments) {
  $output = @(& terraform "-chdir=$tf" @arguments 2>&1)
  $code = $LASTEXITCODE
  if ($code -ne 0) {
    $output | ForEach-Object { Write-Host $_ }
    throw "Terraform fallo: $($arguments -join ' ') (codigo $code)"
  }
  # Mantener el preflight legible: mostrar resumen y acciones, no todo el refresh.
  foreach ($line in $output) {
    $value = [string]$line
    if ($value -match '^\s*(Plan:|No changes\.|Success!|Terraform has been successfully initialized|# azurerm_.* will be)') {
      Write-Host $value
    }
  }
}
function Invoke-Remote([string]$target,[string[]]$sshOptions,[string]$command,[string]$label) {
  & ssh @sshOptions $target $command
  if ($LASTEXITCODE -ne 0) { throw "$label fallo (codigo $LASTEXITCODE)" }
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
if ($Destroy -and -not $PermitirSkuConCosto) {
  if ([string]::IsNullOrWhiteSpace($SkuAzureFreeVerificada)) {
    throw 'DESTROY BLOQUEADO: antes de destruir, verifica en tu suscripcion y region que vm_size sea elegible para Azure Free. Luego pasa -SkuAzureFreeVerificada con el mismo valor exacto de terraform.tfvars. No se presume que una SKU sea gratuita.'
  }
  if ($SkuAzureFreeVerificada -cne $vmSize) {
    throw "DESTROY BLOQUEADO: confirmaste '$SkuAzureFreeVerificada', pero terraform.tfvars configura '$vmSize'."
  }
}
if ($vmSize -eq 'Standard_D2as_v4') {
  Write-Warning 'La VM configurada es Standard_D2as_v4; no es elegible para asumir cobertura Azure Free.'
}

# ipconfig muestra IP privada de LAN; para la regla SSH se valida la IPv4 publica de salida.
if ($tfvars -notmatch '(?m)^\s*ssh_allowed_cidr\s*=\s*"([^"/]+)/32"') { throw 'No se pudo leer ssh_allowed_cidr de terraform.tfvars' }
$sshAllowedIp = $Matches[1]
if ($Destroy) {
  try {
    $publicIpv4 = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10)).Trim()
  } catch {
    throw 'DESTROY BLOQUEADO: no se pudo consultar la IPv4 publica actual. No se puede validar la regla SSH.'
  }
  if ($publicIpv4 -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw "IPv4 publica inesperada: $publicIpv4" }
  if ($publicIpv4 -ne $sshAllowedIp) {
    throw "DESTROY BLOQUEADO: terraform.tfvars permite SSH desde $sshAllowedIp/32, pero la IPv4 publica actual es $publicIpv4. Actualiza ssh_allowed_cidr y revisa el plan antes de continuar. La IPv4 192.168.x.x de ipconfig no sirve para esta regla."
  }
  Write-Host "Regla SSH verificada para IPv4 publica $publicIpv4/32." -ForegroundColor Green
}

TF @('init','-input=false')
TF @('validate')
Write-Host 'Plan Terraform (solo lectura; no se aplica):' -ForegroundColor Cyan
TF @('plan','-input=false','-no-color')

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
  # El backup remoto termina con servicios detenidos; desde aqui hay que reanudarlos si algo falla.
  $maintenanceStopped=$true
  $backup=Get-ChildItem -LiteralPath $BackupRoot -Directory |
    Where-Object { $_.CreationTime -ge $start.AddMinutes(-2) -and $_.Name -match '^\d{8}-\d{6}$' } |
    Sort-Object Name -Descending | Select-Object -First 1
  if ($null -eq $backup) { throw 'No se encontro un backup nuevo. Destruccion bloqueada.' }
  # Los servicios permanecen detenidos durante validacion y confirmacion.
  & $restore -BackupDir $backup.FullName -ValidateOnly
  Write-Host "RESPALDO VALIDADO: $($backup.FullName)" -ForegroundColor Green

  # respaldar-azure.ps1 deja aplicaciones, RabbitMQ, monitorizacion y timer detenidos.
  # No volver a iniciar ni detener servicios aqui.
  Write-Host 'ATENCION: Terraform destroy elimina los recursos administrados por este estado.' -ForegroundColor Red
  Write-Host 'Backup verificado y servicios detenidos. La aplicacion permanece en mantenimiento.' -ForegroundColor Yellow
  $confirmation=Read-Host 'Escribi DESTRUIR para confirmar (cualquier otra cosa cancela)'
  if ($confirmation -cne 'DESTRUIR') {
    $resumeCommand = 'for n in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus lastmileuy-lastmileuy.api-1 lastmileuy-lastmileuy.backoffice-1 lastmileuy-lastmileuy.commerceportal-1 lastmileuy-lastmileuy.driverpwa-1 lastmileuy-lastmileuy.publictracking-1 lastmileuy-lastmileuy.worker-1 lastmileuy-lastmileuy.receptoravisos-1; do docker start "$n" >/dev/null 2>&1 || true; done; sudo -n systemctl start lastmileuy-update.timer 2>/dev/null || true'
    Invoke-Remote $dest $sshOpts $resumeCommand 'Reanudar servicios tras cancelar'
    $maintenanceStopped=$false
    throw 'Cancelado sin destruir Azure'
  }

  $state=Join-Path $tf 'terraform.tfstate'
  Copy-Item -LiteralPath $state -Destination "$state.pre-destroy-$(Get-Date -Format yyyyMMdd-HHmmss)" -ErrorAction Stop
  TF @('destroy','-input=false','-auto-approve')
  $vmDestroyed=$true
  TF @('apply','-input=false','-auto-approve')

  $newIp=(& terraform "-chdir=$tf" output -raw public_ip)
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($newIp)) { throw 'No se pudo obtener la nueva IP publica' }
  $newIp=$newIp.Trim()
  $newDest="azureuser@$newIp"
  $ready=$false
  for($i=0;$i -lt 60;$i++) {
    & ssh @sshOpts $newDest 'echo SSH_OK' 2>$null
    if ($LASTEXITCODE -eq 0) { $ready=$true; break }
    Start-Sleep -Seconds 10
  }
  if (-not $ready) { throw "La VM se creo, pero SSH no esta disponible en $newIp. No se pudo restaurar." }
  Invoke-Remote $newDest $sshOpts 'sudo cloud-init status --wait && docker compose version' 'Preparacion cloud-init/Docker'
  & scp @sshOpts $installer "${newDest}:/home/azureuser/instalar-azure.sh"
  if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar instalar-azure.sh' }
  Invoke-Remote $newDest $sshOpts 'sudo bash /home/azureuser/instalar-azure.sh' 'Instalacion base'

  & $restore -BackupDir $backup.FullName -TerraformDir $tf -SshKey $SshKey -EnvFile $EnvFile -MaxAgeMinutes 240
  Write-Host "RECONSTRUCCION_Y_RESTAURACION_OK: https://api.$($newIp -replace '\.','-').sslip.io" -ForegroundColor Green
}
catch {
  if ($maintenanceStopped -and -not $vmDestroyed) {
    Write-Warning 'La VM original parece seguir disponible. Intentando reanudar los servicios detenidos...'
    try {
      $resumeCommand = 'for n in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus lastmileuy-lastmileuy.api-1 lastmileuy-lastmileuy.backoffice-1 lastmileuy-lastmileuy.commerceportal-1 lastmileuy-lastmileuy.driverpwa-1 lastmileuy-lastmileuy.publictracking-1 lastmileuy-lastmileuy.worker-1 lastmileuy-lastmileuy.receptoravisos-1; do docker start "$n" >/dev/null 2>&1 || true; done; sudo -n systemctl start lastmileuy-update.timer 2>/dev/null || true'
      Invoke-Remote $dest $sshOpts $resumeCommand 'Reanudacion de servicios'
    } catch { Write-Warning 'No se pudieron reanudar automaticamente. Revisar la VM por SSH.' }
  }
  throw
}

#requires -Version 5.1
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$BackupDir,
  [string]$TerraformDir = (Join-Path (Get-Location) 'infra/terraform'),
  [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
  [string]$EnvFile = (Join-Path $HOME 'LastMileUY-Azure/config/.env.azure'),
  [switch]$ValidateOnly,
  [int]$MaxAgeMinutes = 240
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function CheckExit([string]$label) {
  if ($LASTEXITCODE -ne 0) { throw "$label fallo ($LASTEXITCODE)" }
}

if (-not (Test-Path -LiteralPath $BackupDir -PathType Container)) { throw "Falta backup $BackupDir" }
$BackupDir=(Resolve-Path -LiteralPath $BackupDir).Path
$hashFile=Join-Path $BackupDir 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $hashFile -PathType Leaf)) { throw 'Falta SHA256SUMS' }

$required=@('postgres.dump','globals.sql','manifest.txt','imagenes.txt','rabbitmq-colas.txt','rabbitmq-definitions.json')
$volumes=@('rabbitmq_data','grafana_data','prometheus_data','claves_api','claves_backoffice','claves_portal','evidencias_api','receptor_avisos_data')
foreach($v in $volumes) { $required += "volumes/lastmileuy_$v.tar.gz" }

# Verificar hashes, rutas seguras y existencia de cada archivo declarado.
$checked=@{}
foreach($line in @(Get-Content -LiteralPath $hashFile)) {
  if ($line -notmatch '^([a-fA-F0-9]{64})\s+\*?(?:\./)?(.+)$') { throw "SHA256SUMS invalido: $line" }
  $expected=$Matches[1]
  $rel=$Matches[2]
  if ($rel.StartsWith('/') -or $rel -match '(^|/)\.\.(/|$)' -or $rel -match '^[A-Za-z]:') { throw "Ruta insegura: $rel" }
  $path=Join-Path $BackupDir ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta: $rel" }
  if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $expected) { throw "SHA256 incorrecto: $rel" }
  $checked[$rel]=$true
}
foreach($rel in $required) {
  if (-not $checked.ContainsKey($rel)) { throw "Falta respaldo verificado: $rel" }
}

$manifest=Get-Content -LiteralPath (Join-Path $BackupDir 'manifest.txt') -Raw
if ($manifest -notmatch '(?m)^backup_version=1\s*$') { throw 'Version de manifiesto no soportada' }
if ($manifest -notmatch '(?m)^created_utc=(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ)\s*$') { throw 'manifest.txt sin fecha UTC valida' }
$created=[DateTime]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
$age=([DateTime]::UtcNow-$created).TotalMinutes
if ($age -lt -5) { throw 'El backup tiene fecha futura inesperada' }

# Las siete imagenes deben pertenecer al mismo commit respaldado.
$images=Get-Content -LiteralPath (Join-Path $BackupDir 'imagenes.txt') -Raw
$imageMatches=[regex]::Matches($images, 'ghcr\.io/goncho09/lastmileuy-(?:api|backoffice|commerceportal|driverpwa|publictracking|worker|receptoravisos):([a-f0-9]{40})')
$versions=@($imageMatches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
if ($imageMatches.Count -lt 7 -or $versions.Count -ne 1) { throw 'Las siete imagenes no corresponden a una unica version SHA' }
$version=$versions[0]

# Validar que el backup de RabbitMQ contenga exactamente un hostname Mnesia recuperable.
$tar=Get-Command tar.exe -ErrorAction SilentlyContinue
if ($null -eq $tar) { throw 'Se necesita tar.exe para verificar los volumenes antes de destruir Azure' }
foreach($suffix in $volumes) {
  $archive=Join-Path $BackupDir "volumes/lastmileuy_$suffix.tar.gz"
  & $tar.Source -tzf $archive *> $null
  CheckExit "Integridad del volumen $suffix"
}
$rabbitArchive=Join-Path $BackupDir 'volumes/lastmileuy_rabbitmq_data.tar.gz'
$entries=@(& $tar.Source -tzf $rabbitArchive 2>&1)
CheckExit 'Lectura del volumen RabbitMQ'
$nodes=@($entries | ForEach-Object {
  $entry=([string]$_).TrimStart('./')
  if ($entry -match '^mnesia/(rabbit@[^/]+)(?:/.*)?$') { $Matches[1] }
} | Where-Object {
  # Mnesia crea estos directorios auxiliares; no son nodos RabbitMQ.
  $_ -notmatch '-(feature_flags|plugins-expand)$'
} | Sort-Object -Unique)
if ($nodes.Count -ne 1) { throw "No se pudo identificar un unico nodo RabbitMQ en Mnesia. Nodos: $($nodes -join ', ')" }

# rabbitmqctl emite cabeceras y pie de salida ademas de filas tabuladas.
# Exigir al menos una fila de cola y rechazar mensajes pendientes.
$queueLines=@(Get-Content -LiteralPath (Join-Path $BackupDir 'rabbitmq-colas.txt'))
if ($queueLines.Count -eq 0) { throw 'rabbitmq-colas.txt esta vacio' }
$queueCount=0
foreach($rawLine in $queueLines) {
  $line=([string]$rawLine).Trim()
  if ($line.Length -eq 0) { continue }
  if ($line -match '^Listing queues for vhost\s+.+\.\.\.$') { continue }
  if ($line -match '^Timeout:\s*\d+(?:\.\d+)?\s+seconds?\s*(?:\.\.\.)?\s*$') { continue }
  if ($line -match '^name\s+messages_ready\s+messages_unacknowledged\s*$') { continue }
  if ($line -match '^\.\.\.done\.?$') { continue }
  if ($line -match '^\S+\s+(\d+)\s+(\d+)\s*$') {
    $queueCount++
    if ([long]$Matches[1] -ne 0 -or [long]$Matches[2] -ne 0) {
      throw "El backup contiene mensajes RabbitMQ pendientes: $line"
    }
    continue
  }
  throw "Formato inesperado en rabbitmq-colas.txt: $line"
}
if ($queueCount -eq 0) { throw 'No se encontraron filas de colas RabbitMQ en el respaldo' }

Write-Host "Respaldo integro: $BackupDir" -ForegroundColor Green
Write-Host "Version: $version | RabbitMQ: $($nodes[0]) | Edad: $([math]::Round($age,1)) min" -ForegroundColor Cyan
if ($ValidateOnly) {
  Write-Host 'VALIDACION_LOCAL_OK: no se modifico Azure.' -ForegroundColor Green
  return
}
if ($age -gt $MaxAgeMinutes) { throw "Respaldo demasiado antiguo: $([math]::Round($age)) minutos. Maximo: $MaxAgeMinutes" }

foreach($cmd in @('terraform','ssh','scp')) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Falta $cmd" }
}
foreach($path in @($TerraformDir,$SshKey,$EnvFile)) {
  if (-not (Test-Path -LiteralPath $path)) { throw "Falta $path" }
}
$ip=(& terraform "-chdir=$TerraformDir" output -raw public_ip)
CheckExit 'Terraform output'
$ip=$ip.Trim()
if ($ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw "IP inesperada: $ip" }
$dest="azureuser@$ip"
$sshOpts=@('-i',$SshKey,'-o','BatchMode=yes','-o','StrictHostKeyChecking=accept-new','-o','ConnectTimeout=15')
$remote="/home/azureuser/lastmileuy-restauracion-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
& ssh @sshOpts $dest "mkdir -m 700 '$remote'"
CheckExit 'Crear carpeta remota'
& scp @sshOpts -r "${BackupDir}/." "${dest}:${remote}/"
CheckExit 'Subir respaldo'
& scp @sshOpts $EnvFile "${dest}:${remote}/.env.azure"
CheckExit 'Subir configuracion privada'
$script=Join-Path $PSScriptRoot 'restaurar-vm.sh'
if (-not (Test-Path -LiteralPath $script)) { throw "Falta $script" }
& scp @sshOpts $script "${dest}:${remote}/restaurar-vm.sh"
CheckExit 'Subir restaurador'
& ssh @sshOpts $dest "bash '$remote/restaurar-vm.sh' '$remote' '$version' '$ip'"
CheckExit 'Restauracion remota'
Write-Host 'RESTAURACION_COMPLETA_OK' -ForegroundColor Green

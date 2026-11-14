#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$TerraformDir = (Join-Path (Get-Location) 'infra/terraform'),
    [string]$SshKey = (Join-Path $HOME 'LastMileUY-Azure/ssh/lastmileuy_azure_nueva'),
    [string]$BackupRoot = (Join-Path $HOME 'LastMileUY-Azure/backups')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($cmd in @('terraform','ssh','scp')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Falta $cmd" }
}

if (-not (Test-Path -LiteralPath $SshKey -PathType Leaf)) { throw "Falta clave SSH: $SshKey" }
if (-not (Test-Path -LiteralPath $TerraformDir -PathType Container)) { throw "Falta Terraform: $TerraformDir" }

$ip = (& terraform "-chdir=$TerraformDir" output -raw public_ip)

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ip)) { throw 'No se pudo obtener public_ip de Terraform' }

$ip = $ip.Trim()
if ($ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw "IP inesperada: $ip" }

$dest = "azureuser@$ip"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$local = Join-Path $BackupRoot $stamp
New-Item -ItemType Directory -Path $local -Force | Out-Null
$remote = "/home/azureuser/lastmileuy-respaldo-$stamp"

# No cambiar el contenido del bloque sin volver a validar el procedimiento de restauracion.
$remoteScript = @'
set -Eeuo pipefail
umask 077
DIR="$1"
PROJECT="/opt/lastmileuy"
VOLUMES=(
  lastmileuy_rabbitmq_data
  lastmileuy_grafana_data
  lastmileuy_prometheus_data
  lastmileuy_claves_api
  lastmileuy_claves_backoffice
  lastmileuy_claves_portal
  lastmileuy_evidencias_api
  lastmileuy_receptor_avisos_data
)
APPS=(
  lastmileuy-lastmileuy.api-1
  lastmileuy-lastmileuy.backoffice-1
  lastmileuy-lastmileuy.commerceportal-1
  lastmileuy-lastmileuy.driverpwa-1
  lastmileuy-lastmileuy.publictracking-1
  lastmileuy-lastmileuy.worker-1
  lastmileuy-lastmileuy.receptoravisos-1
)
CONSISTENCY=(lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus)
STOPPED=()
TIMER_ACTIVO=false
BACKUP_OK=false
restore_services() {
  rc=$?
  trap - EXIT
  # Si el backup no termino, recuperar el servicio automaticamente.
  # Si termino, mantener todo detenido hasta confirmar destroy o cancelar
  # explicitamente desde Windows: no debe existir una ventana de escrituras.
  if [[ "$BACKUP_OK" != true ]]; then
    if ((${#STOPPED[@]})); then
      echo "Reanudando contenedores porque el respaldo fallo..." >&2
      for name in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus; do
        for stopped in "${STOPPED[@]}"; do
          if [[ "$stopped" == "$name" ]]; then docker start "$name" >&2 || rc=1; fi
        done
      done
      for name in "${APPS[@]}"; do
        for stopped in "${STOPPED[@]}"; do
          if [[ "$stopped" == "$name" ]]; then docker start "$name" >&2 || rc=1; fi
        done
      done
    fi
    if [[ "$TIMER_ACTIVO" == true ]]; then
      sudo -n systemctl start lastmileuy-update.timer || rc=1
    fi
  else
    echo "RESPALDO_OK_SERVICIOS_MANTENIDOS_DETENIDOS" >&2
  fi
  exit "$rc"
}
trap restore_services EXIT
# Deshabilitar primero nuevos disparos y luego comprobar si ya habia una ejecucion activa.
if systemctl is-active --quiet lastmileuy-update.timer; then
  sudo -n systemctl stop lastmileuy-update.timer
  TIMER_ACTIVO=true
fi
if systemctl is-active --quiet lastmileuy-update.service; then
  echo "Hay una actualización en curso. No se puede respaldar." >&2
  exit 1
fi
command -v docker >/dev/null
command -v sha256sum >/dev/null
command -v tar >/dev/null
sudo -n true
test -f "$PROJECT/.env.azure"
docker inspect lastmileuy-postgres >/dev/null
test "$(docker inspect -f '{{.State.Running}}' lastmileuy-postgres)" = true
for vol in "${VOLUMES[@]}"; do docker volume inspect "$vol" >/dev/null; done
for name in "${APPS[@]}" "${CONSISTENCY[@]}"; do
  docker inspect "$name" >/dev/null
done
if [[ -e "$DIR" ]]; then echo "Directorio remoto ya existe: $DIR" >&2; exit 1; fi
mkdir -m 700 -p "$DIR/volumes"
# Registrar la version exacta antes de detener contenedores.
docker inspect --format '{{.Name}}|{{.Config.Image}}|{{.Image}}' "${APPS[@]}" > "$DIR/imagenes.txt"
docker ps --format '{{.Names}}|{{.Status}}' > "$DIR/contenedores-antes.txt"
# Detener solamente los contenedores que estaban activos.
stop_if_running() {
  local name="$1"
  if [[ "$(docker inspect -f '{{.State.Running}}' "$name")" == true ]]; then
    STOPPED+=("$name")
    docker stop -t 45 "$name" >/dev/null
  fi
}
echo "Pausando aplicaciones para evitar escrituras y nuevos mensajes..."
for name in "${APPS[@]}"; do stop_if_running "$name"; done
echo "Guardando estado de colas..."
docker exec lastmileuy-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged > "$DIR/rabbitmq-colas.txt"
if awk 'NR > 1 && $2 ~ /^[0-9]+$/ && $3 ~ /^[0-9]+$/ && ($2 > 0 || $3 > 0) { pendientes=1 } END { exit !pendientes }' "$DIR/rabbitmq-colas.txt"; then
  echo "Hay mensajes pendientes en RabbitMQ. Respaldo cancelado." >&2
  exit 1
fi
# Guardar definiciones para recuperacion alternativa y auditoria.
docker exec lastmileuy-rabbitmq rabbitmqctl export_definitions /tmp/lastmileuy-definitions.json >/dev/null
docker cp lastmileuy-rabbitmq:/tmp/lastmileuy-definitions.json "$DIR/rabbitmq-definitions.json"
docker exec lastmileuy-rabbitmq rm -f /tmp/lastmileuy-definitions.json
echo "Generando dump PostgreSQL con aplicaciones detenidas..."
docker exec lastmileuy-postgres pg_dump -U lastmileuy -d lastmileuy -Fc > "$DIR/postgres.dump"
docker exec lastmileuy-postgres pg_dumpall -U lastmileuy --globals-only > "$DIR/globals.sql"
test -s "$DIR/postgres.dump"
test -s "$DIR/globals.sql"
docker exec -i lastmileuy-postgres pg_restore --list < "$DIR/postgres.dump" >/dev/null
echo "Deteniendo RabbitMQ, Grafana y Prometheus para copiar volúmenes..."
for name in "${CONSISTENCY[@]}"; do stop_if_running "$name"; done
for vol in "${VOLUMES[@]}"; do
  mountpoint=$(docker volume inspect -f '{{.Mountpoint}}' "$vol")
  [[ "$mountpoint" == /var/lib/docker/volumes/*/_data ]] || { echo "Ruta de volumen inesperada: $vol" >&2; exit 1; }
  sudo -n tar -C "$mountpoint" -czf - . > "$DIR/volumes/$vol.tar.gz"
  tar -tzf "$DIR/volumes/$vol.tar.gz" >/dev/null
done
cat > "$DIR/manifest.txt" <<EOF
backup_version=1
created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
postgres=logical_pg_dump_custom
postgres_globals=pg_dumpall_globals
rabbitmq=offline_volume_and_definitions
compose_project=lastmileuy
EOF
(cd "$DIR" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
(cd "$DIR" && sha256sum -c SHA256SUMS)
BACKUP_OK=true
echo "RESPALDO_REMOTO_OK=$DIR"
'@

$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoteScript))
$remoteCommand = "echo '$encoded' | base64 -d | bash -s -- '$remote'"
$sshArgs = @('-i',$SshKey,'-o','BatchMode=yes','-o','StrictHostKeyChecking=accept-new')
$resumeCommand = @'
set -e
for n in lastmileuy-rabbitmq lastmileuy-grafana lastmileuy-prometheus; do docker start "$n" >/dev/null 2>&1 || true; done
for n in lastmileuy-lastmileuy.api-1 lastmileuy-lastmileuy.backoffice-1 lastmileuy-lastmileuy.commerceportal-1 lastmileuy-lastmileuy.driverpwa-1 lastmileuy-lastmileuy.publictracking-1 lastmileuy-lastmileuy.worker-1 lastmileuy-lastmileuy.receptoravisos-1; do docker start "$n" >/dev/null 2>&1 || true; done
sudo -n systemctl start lastmileuy-update.timer 2>/dev/null || true
'@
$remoteBackupSucceeded = $false
try {
    Write-Host "Generando respaldo remoto en $dest ..." -ForegroundColor Cyan
    & ssh @sshArgs $dest $remoteCommand
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el respaldo remoto. No se destruira Azure.' }
    $remoteBackupSucceeded = $true

    Write-Host "Descargando respaldo a $local ..." -ForegroundColor Cyan
    & scp @sshArgs -r "${dest}:${remote}/." "$local/"
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la descarga. No se destruira Azure.' }
$hashFile = Join-Path $local 'SHA256SUMS'

if (-not (Test-Path -LiteralPath $hashFile)) { throw 'Falta SHA256SUMS. NO destruir Azure.' }
$lines = @(Get-Content -LiteralPath $hashFile)

if ($lines.Count -lt 12) { throw 'Manifiesto de hashes incompleto. NO destruir Azure.' }
foreach ($line in $lines) {
    if ($line -notmatch '^([a-fA-F0-9]{64})\s+\*?(.+)$') { throw "Linea de hash invalida: $line" }
    
    $expected = $Matches[1]
    $relative = $Matches[2]
    
    if ($relative.StartsWith('./')) { $relative = $relative.Substring(2) }
    if ($relative.StartsWith('/') -or $relative -match '(^|/)\.\.(/|$)' -or $relative -match '^[A-Za-z]:') {
        throw "Ruta insegura en manifiesto: $relative"
    }
    
    $file = Join-Path $local ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
    
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta archivo: $relative" }
    
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    
    if ($actual -ine $expected) { throw "SHA256 incorrecto: $relative" }
}
$required = @('postgres.dump','globals.sql','manifest.txt','imagenes.txt','rabbitmq-colas.txt','rabbitmq-definitions.json')
$volumes = @('rabbitmq_data','grafana_data','prometheus_data','claves_api','claves_backoffice','claves_portal','evidencias_api','receptor_avisos_data')

foreach ($v in $volumes) { $required += "volumes/lastmileuy_$v.tar.gz" }
foreach ($f in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $local $f) -PathType Leaf)) { throw "Falta $f. NO destruir Azure." }
}

Write-Host "Respaldo descargado y SHA256 verificados: $local" -ForegroundColor Green
Write-Host 'Servicios remotos y timer permanecen detenidos hasta confirmar reconstruccion o cancelar.' -ForegroundColor Yellow
}
catch {
    if ($remoteBackupSucceeded) {
        Write-Warning 'La descarga o validacion local fallo. Reanudando servicios en la VM original...'
        & ssh @sshArgs $dest $resumeCommand
        if ($LASTEXITCODE -ne 0) { Write-Warning 'No se pudieron reanudar todos los servicios. Revisar la VM por SSH.' }
    }
    throw
}

#!/usr/bin/env bash
set -Eeuo pipefail

PROYECTO="/opt/lastmileuy"
REPO="ghcr.io/goncho09/lastmileuy"
MARCADOR="${REPO}-release:stable"
ESTADO="/var/lib/lastmileuy-deploy"
VERSION_FILE="$ESTADO/version"
LOCK="$ESTADO/deploy.lock"

SERVICIOS=(api backoffice commerceportal driverpwa publictracking worker receptoravisos)

cd "$PROYECTO"
mkdir -p "$ESTADO"
exec 9>"$LOCK"
flock -n 9 || { echo "Ya existe una actualización en ejecución."; exit 0; }

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

compose_base=(docker compose --project-directory "$PROYECTO" --env-file .env.azure \
  -f compose.yaml -f docker-compose.azure.yml -f compose.deploy.yaml)

obtener_version() {
  local cid version
  docker pull "$MARCADOR" >&2
  cid=$(docker create "$MARCADOR" /version.txt)
  if ! version=$(docker cp "$cid:/version.txt" - | tar -xO -f - version.txt | tr -d '\r\n'); then
    docker rm -f "$cid" >/dev/null 2>&1 || true
    return 1
  fi
  docker rm "$cid" >/dev/null
  [[ "$version" =~ ^[a-f0-9]{40}$ ]] || { log "Indicador GHCR inválido."; return 1; }
  printf '%s\n' "$version"
}

verificar_contenedores() {
  local servicio cid
  for servicio in "${SERVICIOS[@]}"; do
    cid=$("${compose_base[@]}" ps -q "lastmileuy.$servicio")
    [[ -n "$cid" ]] || { log "No existe el contenedor lastmileuy.$servicio"; return 1; }
    [[ "$(docker inspect -f '{{.State.Running}}' "$cid")" == true ]] || {
      log "lastmileuy.$servicio no está ejecutándose."; return 1;
    }
  done
}

verificar_http() {
  local nombre="$1" url="$2" intento
  for intento in {1..20}; do
    if curl --fail --silent --max-time 5 "$url" >/dev/null; then
      log "$nombre responde correctamente."; return 0
    fi
    sleep 3
  done
  log "Falló la comprobación HTTP de $nombre ($url)."
  return 1
}

verificar_aplicacion() {
  verificar_contenedores
  verificar_http "API" "http://127.0.0.1:8080/health"
  verificar_http "Backoffice" "http://127.0.0.1:8081/Login"
}

crear_respaldo_y_override() {
  local version="$1" servicio cid image_id tag dir="$ESTADO/rollback-$1" override="$ESTADO/rollback-$1.yaml"
  mkdir -p "$dir"
  printf 'services:\n' > "$override"
  for servicio in "${SERVICIOS[@]}"; do
    cid=$("${compose_base[@]}" ps -q "lastmileuy.$servicio")
    [[ -n "$cid" ]] || { log "No se pudo respaldar lastmileuy.$servicio"; return 1; }
    image_id=$(docker inspect -f '{{.Image}}' "$cid")
    tag="lastmileuy-rollback-${servicio}:${version}"
    docker tag "$image_id" "$tag"
    printf '  lastmileuy.%s:\n    image: %s\n    build: !reset null\n' "$servicio" "$tag" >> "$override"
  done
  printf '%s\n' "$override"
}

restaurar() {
  local override="$1"
  log "Intentando restaurar las imágenes anteriores..."
  docker compose --project-directory "$PROYECTO" --env-file .env.azure \
    -f compose.yaml -f docker-compose.azure.yml -f "$override" \
    up -d --no-build --no-deps "${SERVICIOS[@]/#/lastmileuy.}"
  verificar_contenedores
  verificar_http "API restaurada" "http://127.0.0.1:8080/health"
  verificar_http "Backoffice restaurado" "http://127.0.0.1:8081/Login"
  log "Rollback verificado."
}

log "Consultando versión validada en GHCR..."
VERSION_NUEVA=$(obtener_version)
VERSION_ACTUAL=$(cat "$VERSION_FILE" 2>/dev/null || true)
[[ "$VERSION_NUEVA" != "$VERSION_ACTUAL" ]] || { log "Ya está instalada la versión $VERSION_NUEVA."; exit 0; }

# No se despliega una imagen si su commit no es el HEAD actual de main.
log "Comprobando que el repositorio local puede sincronizarse..."
git fetch origin main
HEAD_REMOTO=$(git rev-parse origin/main)
if [[ "$HEAD_REMOTO" != "$VERSION_NUEVA" ]]; then
  log "La versión publicada ($VERSION_NUEVA) no coincide todavía con origin/main ($HEAD_REMOTO). Se reintentará luego."
  exit 0
fi
if [[ "$(git branch --show-current)" != main ]]; then
  log "El repositorio no está en main. No se despliega automáticamente."
  exit 1
fi
if [[ -n "$(git status --porcelain)" ]]; then
  log "Hay cambios locales en el repositorio. Se bloquea el despliegue para protegerlos."
  exit 1
fi
git merge --ff-only origin/main

export LASTMILEUY_VERSION="$VERSION_NUEVA"
"${compose_base[@]}" config --quiet

log "Descargando las siete imágenes antes de modificar contenedores..."
for servicio in "${SERVICIOS[@]}"; do
  "${compose_base[@]}" pull "lastmileuy.$servicio"
done

log "Comprobando migraciones de la imagen nueva contra PostgreSQL..."
set +e
"${compose_base[@]}" run --rm --no-deps --entrypoint dotnet lastmileuy.api \
  LastMileUY.API.dll --check-migrations
codigo_migraciones=$?
set -e
if [[ "$codigo_migraciones" -eq 10 ]]; then
  log "Hay migraciones pendientes. Despliegue automático bloqueado; requiere respaldo y ejecución controlada."
  exit 0
elif [[ "$codigo_migraciones" -ne 0 ]]; then
  log "No se pudo comprobar el estado de las migraciones (código $codigo_migraciones). Despliegue bloqueado."
  exit 1
fi

log "Guardando respaldo de las imágenes actuales..."
ROLLBACK=$(crear_respaldo_y_override "$VERSION_NUEVA")

DESPLIEGUE_OK=false
rollback_si_falla() {
  local codigo=$?
  trap - ERR
  if [[ "$DESPLIEGUE_OK" != true && -n "${ROLLBACK:-}" && -f "$ROLLBACK" ]]; then
    if ! restaurar "$ROLLBACK"; then
      log "ERROR CRÍTICO: falló también la recuperación. Intervención manual necesaria."
    fi
  fi
  exit "$codigo"
}
trap rollback_si_falla ERR

log "Actualizando los siete servicios..."
"${compose_base[@]}" up -d --no-build --no-deps "${SERVICIOS[@]/#/lastmileuy.}"
verificar_aplicacion
printf '%s\n' "$VERSION_NUEVA" > "$VERSION_FILE.tmp"
mv "$VERSION_FILE.tmp" "$VERSION_FILE"
DESPLIEGUE_OK=true
trap - ERR
log "Despliegue completado: $VERSION_NUEVA"

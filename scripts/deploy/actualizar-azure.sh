
#!/usr/bin/env bash
set -Eeuo pipefail

PROYECTO="/opt/lastmileuy"
REPO="ghcr.io/goncho09/lastmileuy"
MARCADOR="${REPO}-release:stable"

ESTADO="$PROYECTO/.deploy-state"
VERSION_FILE="$ESTADO/version"
LOCK="$ESTADO/deploy.lock"

SERVICIOS=(
  api
  backoffice
  commerceportal
  driverpwa
  publictracking
  worker
  receptoravisos
)

cd "$PROYECTO"
mkdir -p "$ESTADO"

exec 9>"$LOCK"
flock -n 9 || {
  echo "Ya existe un despliegue en ejecución."
  exit 0
}

log() {
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"
}

COMPOSE=(
  docker compose
  --env-file .env.azure
  -f compose.yaml
  -f docker-compose.azure.yml
  -f compose.deploy.yaml
)

# Obtiene el SHA publicado después de CI.
obtener_version() {
  local contenedor version

  docker pull "$MARCADOR" >&2

  contenedor=$(docker create "$MARCADOR")

  if ! version=$(
    docker cp "$contenedor:/version.txt" - |
      tar -xO -f - version.txt |
      tr -d '\r\n'
  ); then
    docker rm -f "$contenedor" >/dev/null
    return 1
  fi

  docker rm -f "$contenedor" >/dev/null

  [[ "$version" =~ ^[a-f0-9]{40}$ ]] || {
    log "ERROR: versión inválida."
    return 1
  }

  printf '%s\n' "$version"
}

# Comprueba los contenedores de aplicación.
verificar_contenedores() {
  local servicio id estado

  for servicio in "${SERVICIOS[@]}"; do
    id=$("${COMPOSE[@]}" ps -q "lastmileuy.$servicio")

    [[ -n "$id" ]] || return 1

    estado=$(docker inspect -f '{{.State.Running}}' "$id")

    if [[ "$estado" != "true" ]]; then
      log "ERROR: lastmileuy.$servicio no está ejecutándose."
      return 1
    fi
  done
}

verificar_http() {
  local nombre="$1"
  local url="$2"
  local intento

  for intento in {1..20}; do
    if curl --fail --silent --show-error \
      --max-time 5 "$url" >/dev/null 2>&1; then
      log "$nombre responde correctamente."
      return 0
    fi
    sleep 3
  done

  log "ERROR: $nombre no respondió correctamente."
  return 1
}

verificar_aplicacion() {
  verificar_contenedores
  verificar_http "API" "http://127.0.0.1:8080/health"
  verificar_http "Backoffice" "http://127.0.0.1:8081/Login"
}

# Guarda imágenes exactas, sin sobrescribir las anteriores.
guardar_respaldo() {
  local servicio id imagen
  local directorio="$1"

  mkdir -p "$directorio"

  for servicio in "${SERVICIOS[@]}"; do
    id=$("${COMPOSE[@]}" ps -q "lastmileuy.$servicio")
    [[ -n "$id" ]] || return 1

    imagen=$(docker inspect -f '{{.Image}}' "$id")
    printf '%s\n' "$imagen" > "$directorio/$servicio"
  done
}

# Crea un override temporal con las imágenes respaldadas.
crear_compose_rollback() {
  local directorio="$1"
  local archivo="$2"
  local servicio imagen

  printf 'services:\n' > "$archivo"

  for servicio in "${SERVICIOS[@]}"; do
    imagen=$(cat "$directorio/$servicio")

    printf '  lastmileuy.%s:\n' "$servicio" >> "$archivo"
    printf '    image: "%s"\n' "$imagen" >> "$archivo"
    printf '    build: !reset null\n' >> "$archivo"
  done
}

ROLLBACK=""
DESPLIEGUE_INICIADO=false

recuperar() {
  local codigo=$?
  trap - ERR

  if [[ "$DESPLIEGUE_INICIADO" == true &&
        -n "$ROLLBACK" &&
        -f "$ROLLBACK" ]]; then

    log "Falló el despliegue. Intentando rollback..."

    if docker compose \
      --env-file .env.azure \
      -f compose.yaml \
      -f docker-compose.azure.yml \
      -f "$ROLLBACK" \
      up -d --no-build --no-deps \
      "${SERVICIOS[@]/#/lastmileuy.}"; then

      log "Se restauraron los contenedores anteriores."
    else
      log "ERROR CRÍTICO: falló el rollback."
    fi
  fi

  exit "$codigo"
}

trap recuperar ERR

log "Consultando GHCR..."

VERSION_NUEVA=$(obtener_version)
VERSION_ACTUAL=$(cat "$VERSION_FILE" 2>/dev/null || true)

log "Instalada: ${VERSION_ACTUAL:-desconocida}"
log "Disponible: $VERSION_NUEVA"

if [[ "$VERSION_NUEVA" == "$VERSION_ACTUAL" ]]; then
  log "No hay actualizaciones."
  exit 0
fi

export LASTMILEUY_VERSION="$VERSION_NUEVA"

"${COMPOSE[@]}" config --quiet

log "Descargando imágenes..."

for servicio in "${SERVICIOS[@]}"; do
  "${COMPOSE[@]}" pull "lastmileuy.$servicio"
done

RESPALDO="$ESTADO/rollback-$VERSION_NUEVA"
ROLLBACK="$ESTADO/rollback-$VERSION_NUEVA.yaml"

log "Guardando versión anterior..."
guardar_respaldo "$RESPALDO"
crear_compose_rollback "$RESPALDO" "$ROLLBACK"

log "Actualizando LastMileUY..."
DESPLIEGUE_INICIADO=true

"${COMPOSE[@]}" up -d --no-build --no-deps \
  "${SERVICIOS[@]/#/lastmileuy.}"

log "Verificando funcionamiento..."
verificar_aplicacion

printf '%s\n' "$VERSION_NUEVA" > "$VERSION_FILE.tmp"
mv "$VERSION_FILE.tmp" "$VERSION_FILE"

DESPLIEGUE_INICIADO=false

log "Despliegue exitoso: $VERSION_NUEVA"

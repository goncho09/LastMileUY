
#!/usr/bin/env bash
set -Eeuo pipefail

PROYECTO="/opt/lastmileuy"
REPOSITORIO="ghcr.io/goncho09/lastmileuy"
INDICADOR="${REPOSITORIO}-release:stable"

ESTADO="${PROYECTO}/.deploy-state"
VERSION_INSTALADA="${ESTADO}/version"
LOCK="/tmp/lastmileuy-deploy.lock"

SERVICIOS=(
  lastmileuy.api
  lastmileuy.backoffice
  lastmileuy.commerceportal
  lastmileuy.driverpwa
  lastmileuy.publictracking
  lastmileuy.worker
  lastmileuy.receptoravisos
)

cd "$PROYECTO"
mkdir -p "$ESTADO"

exec 9>"$LOCK"
flock -n 9 || {
  echo "Ya hay un despliegue en curso."
  exit 0
}

COMPOSE=(
  docker compose
  --env-file .env.azure
  -f compose.yaml
  -f docker-compose.azure.yml
  -f compose.deploy.yaml
)

obtener_version() {
  local contenedor
  local version

  docker pull "$INDICADOR"

  contenedor=$(docker create "$INDICADOR")

  version=$(
    docker cp "$contenedor:/version.txt" - |
      tar -xO -f - version.txt |
      tr -d '\r\n'
  )

  docker rm "$contenedor" >/dev/null

  if [[ ! "$version" =~ ^[a-f0-9]{40}$ ]]; then
    echo "ERROR: versión inválida." >&2
    return 1
  fi

  printf '%s\n' "$version"
}

verificar_servicio() {
  local servicio="$1"
  local contenedor

  contenedor=$("${COMPOSE[@]}" ps -q "$servicio")

  if [[ -z "$contenedor" ]]; then
    echo "ERROR: no existe el contenedor $servicio"
    return 1
  fi

  if [[ "$(docker inspect -f '{{.State.Running}}' "$contenedor")" != "true" ]]; then
    echo "ERROR: $servicio no está funcionando."
    return 1
  fi
}

verificar_http() {
  local nombre="$1"
  local url="$2"
  local codigo
  local intento

  for intento in {1..15}; do
    codigo=$(curl -s -o /dev/null -w '%{http_code}' \
      --max-time 5 "$url" || true)

    if [[ "$codigo" =~ ^(200|301|302|307|308|401|403)$ ]]; then
      echo "$nombre responde HTTP $codigo"
      return 0
    fi

    sleep 3
  done

  echo "ERROR: $nombre no responde correctamente."
  return 1
}

VERSION_NUEVA=$(obtener_version)

VERSION_ACTUAL=""
if [[ -f "$VERSION_INSTALADA" ]]; then
  VERSION_ACTUAL=$(cat "$VERSION_INSTALADA")
fi

echo "Versión instalada: ${VERSION_ACTUAL:-desconocida}"
echo "Versión disponible: $VERSION_NUEVA"

if [[ "$VERSION_ACTUAL" == "$VERSION_NUEVA" ]]; then
  echo "LastMileUY ya está actualizado."
  exit 0
fi

export LASTMILEUY_VERSION="$VERSION_NUEVA"

echo "Validando configuración..."
"${COMPOSE[@]}" config --quiet

echo "Descargando las 7 imágenes..."
for servicio in "${SERVICIOS[@]}"; do
  "${COMPOSE[@]}" pull "$servicio"
done

echo "Guardando referencias de imágenes anteriores..."

declare -A IMAGENES_ANTERIORES

for servicio in "${SERVICIOS[@]}"; do
  contenedor=$("${COMPOSE[@]}" ps -q "$servicio" || true)

  if [[ -n "$contenedor" ]]; then
    IMAGENES_ANTERIORES["$servicio"]=$(
      docker inspect -f '{{.Image}}' "$contenedor"
    )
  fi
done

echo "Actualizando servicios..."
"${COMPOSE[@]}" up -d --no-build --no-deps "${SERVICIOS[@]}"

echo "Verificando contenedores..."
for servicio in "${SERVICIOS[@]}"; do
  verificar_servicio "$servicio"
done

echo "Verificando endpoints HTTP..."

verificar_http "API" "http://127.0.0.1:8080/health"
verificar_http "Backoffice" "http://127.0.0.1:8081/Login"

printf '%s\n' "$VERSION_NUEVA" > "${VERSION_INSTALADA}.tmp"
mv "${VERSION_INSTALADA}.tmp" "$VERSION_INSTALADA"

echo "Despliegue completado: $VERSION_NUEVA"

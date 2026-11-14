#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
DIR="${1:?Directorio respaldo}"
VERSION="${2:?SHA version}"
IP="${3:?IP publica}"
PROJECT=/opt/lastmileuy
STATE=/var/lib/lastmileuy-deploy
cd "$PROJECT"
sudo -n true
[[ "$VERSION" =~ ^[a-f0-9]{40}$ ]]
[[ "$IP" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ -f "$DIR/SHA256SUMS" && -f "$DIR/.env.azure" ]]
(cd "$DIR" && sha256sum -c SHA256SUMS)
[[ ! -e "$STATE/restauracion-completa" ]] || { echo 'Restauracion ya completada' >&2; exit 1; }
if docker ps -a --format '{{.Names}}' | grep -qx lastmileuy-postgres; then
  echo 'Ya existe PostgreSQL: no se sobrescribira' >&2
  exit 1
fi
sudo systemctl disable --now lastmileuy-update.timer 2>/dev/null || true

# Recuperar el hostname exacto de Mnesia: RabbitMQ no debe arrancar con otro node name.
RABBIT_ARCHIVE="$DIR/volumes/lastmileuy_rabbitmq_data.tar.gz"
[[ -s "$RABBIT_ARCHIVE" ]]
mapfile -t NODES < <(tar -tzf "$RABBIT_ARCHIVE" | sed -nE 's#^(\./)?mnesia/(rabbit@[^/]+)(/.*)?$#\2#p' | grep -Ev -- '-(feature_flags|plugins-expand)$' | sort -u)
[[ ${#NODES[@]} -eq 1 ]] || { printf 'Nodos RabbitMQ encontrados: %s\n' "${NODES[*]}" >&2; exit 1; }
NODE="${NODES[0]}"
HOST="${NODE#rabbit@}"
[[ "$HOST" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*$ ]] || { echo 'Hostname RabbitMQ invalido' >&2; exit 1; }

install -m 600 "$DIR/.env.azure" "$PROJECT/.env.azure"
sed -i '/^URL_SEGUIMIENTO=/d; /^RABBITMQ_RESTORE_HOST=/d' .env.azure
printf '\nURL_SEGUIMIENTO=https://seguimiento.%s.sslip.io\nRABBITMQ_RESTORE_HOST=%s\n' "${IP//./-}" "$HOST" >> .env.azure
export LASTMILEUY_VERSION="$VERSION"
BASE=(docker compose --project-directory "$PROJECT" --env-file .env.azure -f compose.yaml -f docker-compose.azure.yml -f compose.deploy.yaml)
"${BASE[@]}" config --quiet
"${BASE[@]}" pull

# Restaurar todos los volumenes con permisos y propietarios originales.
for suffix in rabbitmq_data grafana_data prometheus_data claves_api claves_backoffice claves_portal evidencias_api receptor_avisos_data; do
  vol="lastmileuy_$suffix"
  archive="$DIR/volumes/$vol.tar.gz"
  [[ -s "$archive" ]] || { echo "Falta archivo de volumen: $archive" >&2; exit 1; }
  docker volume create "$vol" >/dev/null
  mp=$(docker volume inspect -f '{{.Mountpoint}}' "$vol")
  [[ "$mp" == /var/lib/docker/volumes/*/_data ]] || { echo "Mountpoint inesperado: $mp" >&2; exit 1; }
  [[ -z "$(sudo find "$mp" -mindepth 1 -maxdepth 1 -print -quit)" ]] || { echo "Volumen $vol no esta vacio" >&2; exit 1; }
  sudo tar -C "$mp" --same-owner -xzf "$archive"
done

# PostgreSQL nuevo: restaurar roles globales y luego el dump conservando ACL/propietarios.
"${BASE[@]}" up -d postgres redis
for i in $(seq 1 40); do
  docker exec lastmileuy-postgres pg_isready -U lastmileuy -d lastmileuy >/dev/null 2>&1 && break
  sleep 3
done
docker exec lastmileuy-postgres pg_isready -U lastmileuy -d lastmileuy
COUNT=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_tables WHERE schemaname='public'")
[[ "$COUNT" == 0 ]] || { echo 'La BD no esta vacia' >&2; exit 1; }
sed '/^CREATE ROLE lastmileuy;$/d' "$DIR/globals.sql" | docker exec -i lastmileuy-postgres psql -X -v ON_ERROR_STOP=1 -U lastmileuy -d postgres
docker exec -i lastmileuy-postgres pg_restore -U lastmileuy -d lastmileuy --exit-on-error < "$DIR/postgres.dump"
TABLES=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_tables WHERE schemaname='public'")
[[ "$TABLES" -gt 0 ]] || { echo 'La restauracion no contiene tablas publicas' >&2; exit 1; }
MIGRATION_TABLE=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL")
[[ "$MIGRATION_TABLE" == t ]] || { echo 'No existe la tabla de historial de EF Core' >&2; exit 1; }
MIGRATIONS=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc 'SELECT count(*) FROM public."__EFMigrationsHistory"')
[[ "$MIGRATIONS" -gt 0 ]] || { echo 'No se restauraron migraciones de EF Core' >&2; exit 1; }
TABLES=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_tables WHERE schemaname='public'")
RLS=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p') AND c.relrowsecurity AND c.relforcerowsecurity")
[[ "$TABLES" -gt 0 && "$RLS" -eq "$TABLES" ]] || { echo "RLS no esta habilitado y forzado en todas las tablas publicas (tablas=$TABLES, protegidas=$RLS)" >&2; exit 1; }
for role in lastmileuy_app lastmileuy_publicador; do
  docker exec lastmileuy-postgres psql -U lastmileuy -d postgres -Atc "SELECT 1 FROM pg_roles WHERE rolname='$role' AND NOT rolsuper AND NOT rolbypassrls" | grep -qx 1
done
"${BASE[@]}" run --rm --no-deps --entrypoint dotnet lastmileuy.api LastMileUY.API.dll --check-migrations

# Iniciar RabbitMQ con su Mnesia original; no importar definiciones sobre un nodo nuevo.
"${BASE[@]}" up -d rabbitmq
for i in $(seq 1 40); do
  docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1 && break
  sleep 3
done
docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q ping
ACTUAL=$(docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q status | grep -m1 'Node name:' || true)
echo "RabbitMQ recuperado: $ACTUAL"
COLAS=$(docker exec lastmileuy-rabbitmq rabbitmqctl -q list_queues -p / name | grep -c '^lastmileuy\.' || true)
[[ "$COLAS" -ge 9 ]] || { echo "Colas insuficientes: $COLAS" >&2; exit 1; }
docker exec lastmileuy-redis redis-cli ping | grep -qx PONG

"${BASE[@]}" up -d
SERVICIOS=(lastmileuy.api lastmileuy.backoffice lastmileuy.commerceportal lastmileuy.driverpwa lastmileuy.publictracking lastmileuy.worker lastmileuy.receptoravisos)
for servicio in "${SERVICIOS[@]}"; do
  cid=$("${BASE[@]}" ps -q "$servicio")
  [[ -n "$cid" && "$(docker inspect -f '{{.State.Running}}' "$cid")" == true ]] || { echo "Servicio caido: $servicio" >&2; exit 1; }
done

wait_http() {
  local name="$1" url="$2" code=''
  for i in $(seq 1 30); do
    code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 8 "$url" || true)
    if [[ "$code" =~ ^[23][0-9][0-9]$ ]]; then echo "$name OK (HTTP $code)"; return 0; fi
    sleep 4
  done
  echo "$name no responde correctamente: $url (HTTP ${code:-sin respuesta})" >&2
  return 1
}
wait_http 'API' 'http://127.0.0.1:8080/health'
wait_http 'Backoffice' 'http://127.0.0.1:8081/Login'
wait_http 'Portal Comercio' 'http://127.0.0.1:8082/'
wait_http 'PWA Repartidor' 'http://127.0.0.1:8083/'
wait_http 'Seguimiento publico' 'http://127.0.0.1:8084/'
wait_http 'Receptor de avisos' 'http://127.0.0.1:8085/avisos'

# El Worker no expone HTTP: comprobar que efectivamente inicializo sus consumidores.
WORKER_OK=false
for i in $(seq 1 30); do
  LOGS=$(docker logs --tail 150 lastmileuy-lastmileuy.worker-1 2>&1 || true)
  if grep -Fq 'Consumidor de correos iniciado' <<< "$LOGS" && grep -Fq 'Escuchando la cola lastmileuy.worker.envios' <<< "$LOGS"; then
    WORKER_OK=true
    break
  fi
  sleep 3
done
[[ "$WORKER_OK" == true ]] || { echo 'El Worker esta activo pero no confirmo ambos consumidores en logs' >&2; docker logs --tail 150 lastmileuy-lastmileuy.worker-1 >&2; exit 1; }

sudo bash "$PROJECT/scripts/deploy/configurar-caddy.sh" "$IP"
sudo systemctl is-active --quiet caddy

# Marcar la restauracion antes de habilitar el timer de actualizaciones.
install -d -o azureuser -g azureuser -m 750 "$STATE"
printf '%s\n' "$VERSION" | sudo tee "$STATE/version" >/dev/null
sudo chown azureuser:azureuser "$STATE/version"
sudo touch "$STATE/restauracion-completa"
sudo chown azureuser:azureuser "$STATE/restauracion-completa"
sudo systemctl enable --now lastmileuy-update.timer
echo RESTAURACION_COMPLETA_OK

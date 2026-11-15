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
paso() { echo "PASO: $*"; }

[[ "$VERSION" =~ ^[a-f0-9]{40}$ ]]
[[ "$IP" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ -f "$DIR/SHA256SUMS" && -f "$DIR/.env.azure" ]]
paso "Verificando integridad SHA256 del respaldo..."
(cd "$DIR" && sha256sum -c SHA256SUMS >/dev/null)
# Etapas persistidas fuera de los volumenes. Un marcador solo se escribe tras validar.
# Si una operacion no atomica se interrumpe, bloquear antes de tocar datos.
BACKUP_ID=$(sha256sum "$DIR/SHA256SUMS" | awk '{print $1}')
RUN="$STATE/restauraciones/$BACKUP_ID"
sudo install -d -o azureuser -g azureuser -m 700 "$RUN"
# Tras una interrupcion entre actualizar y marcar completa, no reimportar PostgreSQL.
if [[ -f "$RUN/actualizador.ok" && ! -f "$RUN/completa.ok" ]]; then
  [[ -f "$RUN/postgres.ok" && -f "$STATE/version" ]] || { echo 'Restauracion incompleta' >&2; exit 1; }
  [[ "$(git rev-parse HEAD)" == "$(cat "$STATE/version")" ]] || { echo 'Version de codigo incorrecta' >&2; exit 1; }
  sudo systemctl enable --now lastmileuy-update.timer
  sudo systemctl is-enabled --quiet lastmileuy-update.timer
  sudo systemctl is-active --quiet lastmileuy-update.timer
  printf '%s\n' "$BACKUP_ID" > "$RUN/completa.ok"
  sudo touch "$STATE/restauracion-completa"
  sudo chown azureuser:azureuser "$STATE/restauracion-completa"
  echo 'RESTAURACION_COMPLETA_OK'
  exit 0
fi
if [[ -f "$RUN/completa.ok" && ! -f "$STATE/restauracion-completa" ]]; then
  # Interrupcion entre los dos marcadores: terminar la operacion sin reimportar nada.
  [[ -f "$RUN/postgres.ok" && -f "$STATE/version" ]] || {
    echo 'Restauracion marcada completa, pero faltan pruebas de PostgreSQL/version' >&2; exit 1;
  }
  [[ -f "$RUN/actualizador.ok" ]] || { echo 'Falta confirmacion del actualizador' >&2; exit 1; }
  [[ "$(git rev-parse HEAD)" == "$(cat "$STATE/version")" ]] || { echo 'Desfase entre codigo y version' >&2; exit 1; }
  sudo touch "$STATE/restauracion-completa"
  sudo chown azureuser:azureuser "$STATE/restauracion-completa"
  sudo systemctl is-enabled --quiet lastmileuy-update.timer
  sudo systemctl is-active --quiet lastmileuy-update.timer
  echo RESTAURACION_COMPLETA_OK
  exit 0
fi
if [[ -f "$STATE/restauracion-completa" ]]; then
  if [[ -f "$RUN/completa.ok" && -f "$RUN/actualizador.ok" ]]; then
    sudo systemctl is-enabled --quiet lastmileuy-update.timer
    sudo systemctl is-active --quiet lastmileuy-update.timer
    echo RESTAURACION_COMPLETA_OK; exit 0;
  fi
  echo 'Ya hay una restauracion completa de otro respaldo. No se modifica.' >&2; exit 1
fi
if [[ -f "$STATE/version" && ! -f "$RUN/postgres.ok" ]] && docker ps -a --format '{{.Names}}' | grep -qx lastmileuy-postgres; then
  echo 'Existe un despliegue previo sin marcador de este respaldo. No se modifica.' >&2; exit 1
fi
mark() { printf '%s\n' "$BACKUP_ID" > "$RUN/$1.tmp"; mv "$RUN/$1.tmp" "$RUN/$1.ok"; }
started() { [[ -f "$RUN/$1.inprogress" ]]; }
begin_stage() { touch "$RUN/$1.inprogress"; }
finish_stage() { mark "$1"; rm -f "$RUN/$1.inprogress"; }


sudo systemctl disable --now lastmileuy-update.timer 2>/dev/null || true

# Las imagenes del respaldo y los archivos Compose DEBEN pertenecer al mismo commit.
# Solo se permite repinear un repositorio limpio, de main y sin restauracion previa.
if [[ ! -f "$RUN/codigo.ok" ]]; then
  [[ "$(git branch --show-current)" == main ]] || { echo 'El codigo no esta en main' >&2; exit 1; }
  [[ -z "$(git status --porcelain)" ]] || { echo 'Cambios locales: no se modifica el codigo' >&2; exit 1; }
  [[ ! -f "$RUN/postgres.ok" && ! -f "$RUN/postgres.inprogress" ]] || {
    echo 'Hay PostgreSQL restaurado sin version de codigo fijada' >&2; exit 1;
  }
  # Git es reintentable; no bloquear por una interrupcion de red antes del restore.
  paso "Buscando codigo e imagenes del respaldo..."
  git fetch origin main
  git cat-file -e "${VERSION}^{commit}"
  git merge-base --is-ancestor "$VERSION" origin/main || {
    echo 'El commit del respaldo no pertenece a main' >&2; exit 1;
  }
  git reset --hard "$VERSION"
  mark codigo
fi
[[ "$(git rev-parse HEAD)" == "$VERSION" ]] || {
  echo 'El codigo no coincide con la version del backup' >&2; exit 1;
}

# Recuperar el hostname exacto de Mnesia: RabbitMQ no debe arrancar con otro node name.
RABBIT_ARCHIVE="$DIR/volumes/lastmileuy_rabbitmq_data.tar.gz"
[[ -s "$RABBIT_ARCHIVE" ]]
mapfile -t NODES < <(tar -tzf "$RABBIT_ARCHIVE" | sed -nE 's#^(\./)?mnesia/(rabbit@[^/]+)(/.*)?$#\2#p' | grep -Ev -- '-(feature_flags|plugins-expand)$' | sort -u)
[[ ${#NODES[@]} -eq 1 ]] || { printf 'Nodos RabbitMQ encontrados: %s\n' "${NODES[*]}" >&2; exit 1; }
NODE="${NODES[0]}"
HOST="${NODE#rabbit@}"
[[ "$HOST" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*$ ]] || { echo 'Hostname RabbitMQ invalido' >&2; exit 1; }

if [[ -f "$PROJECT/.env.azure" && -f "$RUN/env.ok" ]]; then
  : # Se conserva la configuracion ya verificada de esta restauracion.
else
  if [[ -f "$PROJECT/.env.azure" && ! -f "$RUN/env.ok" ]]; then
    echo 'Configuracion previa sin marcador; revisar manualmente' >&2; exit 1
  fi
  install -m 600 "$DIR/.env.azure" "$PROJECT/.env.azure"
fi
if [[ ! -f "$RUN/env.ok" ]]; then
  sed -i '/^URL_SEGUIMIENTO=/d; /^RABBITMQ_RESTORE_HOST=/d' .env.azure
  printf '\nURL_SEGUIMIENTO=https://seguimiento.%s.sslip.io\nRABBITMQ_RESTORE_HOST=%s\n' "${IP//./-}" "$HOST" >> .env.azure
  mark env
fi
# Override almacenado fuera del repo: funciona incluso restaurando un commit antiguo.
cat > "$RUN/compose-rabbitmq-restauracion.yml" <<EOF
services:
  rabbitmq:
    hostname: "$HOST"
EOF
export LASTMILEUY_VERSION="$VERSION"
BASE=(docker compose --project-directory "$PROJECT" --env-file .env.azure -f compose.yaml -f docker-compose.azure.yml -f compose.deploy.yaml -f "$RUN/compose-rabbitmq-restauracion.yml")
"${BASE[@]}" config --quiet
paso "Descargando imagenes Docker del respaldo..."
"${BASE[@]}" pull

# Restaurar todos los volumenes con permisos y propietarios originales.
for suffix in rabbitmq_data grafana_data prometheus_data claves_api claves_backoffice claves_portal evidencias_api receptor_avisos_data; do
  vol="lastmileuy_$suffix"
  archive="$DIR/volumes/$vol.tar.gz"
  [[ -s "$archive" ]] || { echo "Falta archivo de volumen: $archive" >&2; exit 1; }
  if [[ -f "$RUN/$vol.ok" ]]; then
    docker volume inspect "$vol" >/dev/null || { echo "Volumen marcado ausente: $vol" >&2; exit 1; }
    continue
  fi
  if started "$vol"; then echo "Extraccion parcial de $vol: intervencion manual requerida" >&2; exit 1; fi
  docker volume create "$vol" >/dev/null
  mp=$(docker volume inspect -f '{{.Mountpoint}}' "$vol")
  [[ "$mp" == /var/lib/docker/volumes/*/_data ]] || { echo "Mountpoint inesperado: $mp" >&2; exit 1; }
  [[ -z "$(sudo find "$mp" -mindepth 1 -maxdepth 1 -print -quit)" ]] || { echo "Volumen $vol no esta vacio" >&2; exit 1; }
  paso "Extrayendo volumen $vol..."
  begin_stage "$vol"
  sudo tar -C "$mp" --same-owner -xzf "$archive"
  finish_stage "$vol"
done

# PostgreSQL nuevo: restaurar roles globales y luego el dump conservando ACL/propietarios.
paso "Iniciando PostgreSQL y Redis..."
"${BASE[@]}" up -d postgres redis
for i in $(seq 1 40); do
  docker exec lastmileuy-postgres pg_isready -U lastmileuy -d lastmileuy >/dev/null 2>&1 && break
  sleep 3
done
docker exec lastmileuy-postgres pg_isready -U lastmileuy -d lastmileuy
if [[ ! -f "$RUN/postgres.ok" ]]; then
  if started postgres; then echo 'Restauracion PostgreSQL interrumpida: no se reimporta' >&2; exit 1; fi
  COUNT=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_tables WHERE schemaname='public'")
  [[ "$COUNT" == 0 ]] || { echo 'BD existente sin marcador de este respaldo: no se sobrescribira' >&2; exit 1; }
  paso "Importando roles y datos de PostgreSQL..."
  begin_stage postgres
  sed '/^CREATE ROLE lastmileuy;$/d' "$DIR/globals.sql" | docker exec -i lastmileuy-postgres psql -X -v ON_ERROR_STOP=1 -U lastmileuy -d postgres
  docker exec -i lastmileuy-postgres pg_restore -U lastmileuy -d lastmileuy --exit-on-error < "$DIR/postgres.dump"
fi
TABLES=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT count(*) FROM pg_tables WHERE schemaname='public'")
[[ "$TABLES" -gt 0 ]] || { echo 'La restauracion no contiene tablas publicas' >&2; exit 1; }
MIGRATION_TABLE=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL")
[[ "$MIGRATION_TABLE" == t ]] || { echo 'No existe la tabla de historial de EF Core' >&2; exit 1; }
MIGRATIONS=$(docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc 'SELECT count(*) FROM public."__EFMigrationsHistory"')
[[ "$MIGRATIONS" -gt 0 ]] || { echo 'No se restauraron migraciones de EF Core' >&2; exit 1; }
if [[ ! -f "$RUN/postgres.ok" ]]; then finish_stage postgres; fi
# La restauracion conserva el esquema, las politicas RLS y los permisos del dump.
# No se impone un inventario de seguridad ni cantidades fijas de tablas/politicas.
"${BASE[@]}" run --rm --no-deps --entrypoint dotnet lastmileuy.api LastMileUY.API.dll --check-migrations

# Iniciar RabbitMQ con su Mnesia original; no importar definiciones sobre un nodo nuevo.
paso "Recuperando RabbitMQ y verificando colas..."
"${BASE[@]}" up -d rabbitmq
for i in $(seq 1 40); do
  docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1 && break
  sleep 3
done
docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q ping
ACTUAL=$(docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q status | grep -m1 'Node name:' || true)
echo "RabbitMQ recuperado: $ACTUAL"
[[ "$ACTUAL" == *"$NODE"* ]] || { echo 'Nodo RabbitMQ no coincide con el backup' >&2; exit 1; }
# Verificar los nombres de las colas existentes en el backup, sin cantidades fijas.
EXPECTED_QUEUES=$(awk '$1 ~ /^lastmileuy[.]/ && $2 ~ /^[0-9]+$/ {print $1}' "$DIR/rabbitmq-colas.txt" | sort -u)
[[ -n "$EXPECTED_QUEUES" ]] || { echo 'Backup sin inventario de colas LastMileUY' >&2; exit 1; }
ACTUAL_QUEUES=$(docker exec lastmileuy-rabbitmq rabbitmqctl -q list_queues -p / name | sed '/^$/d' | sort -u)
MISSING_QUEUES=$(comm -23 <(printf '%s\n' "$EXPECTED_QUEUES") <(printf '%s\n' "$ACTUAL_QUEUES"))
[[ -z "$MISSING_QUEUES" ]] || { printf 'Colas del respaldo ausentes:\n%s\n' "$MISSING_QUEUES" >&2; exit 1; }
docker exec lastmileuy-redis redis-cli ping | grep -qx PONG

paso "Iniciando las aplicaciones y los contenedores restantes..."
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
paso 'Verificando HTTP de los seis servicios publicos...'
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

[[ -f "$DIR/configurar-caddy.sh" ]] || { echo 'Falta configurador de Caddy adjunto' >&2; exit 1; }
paso "Configurando Caddy y HTTPS..."
sudo bash "$DIR/configurar-caddy.sh" "$IP"
sudo systemctl is-active --quiet caddy
wait_http 'API HTTPS' "https://api.${IP//./-}.sslip.io/health"
wait_http 'Backoffice HTTPS' "https://backoffice.${IP//./-}.sslip.io/Login"

# Conservar las etapas de datos y el marcador de version antes de actualizar.
# NO marcar completa hasta validar el timer y aplicar las migraciones publicadas.
paso "Registrando version recuperada y preparando actualizador..."
install -d -o azureuser -g azureuser -m 750 "$STATE"
printf '%s\n' "$VERSION" | sudo tee "$STATE/version" >/dev/null
sudo chown azureuser:azureuser "$STATE/version"

# El checkout se fijo al commit del respaldo. Copiar el updater validado del
# paquete de restauracion a un lugar EXTERNO al repo que el updater modifica.
UPDATER_ORIGEN="$DIR/actualizar-azure.sh"
UPDATER_INSTALADO="$STATE/actualizar-azure-revisado.sh"
[[ -s "$UPDATER_ORIGEN" ]] || { echo 'Falta el actualizador revisado adjunto al backup' >&2; exit 1; }
bash -n "$UPDATER_ORIGEN"
grep -Fq -- '--apply-migrations' "$UPDATER_ORIGEN"
grep -Fq 'crear_backup_postgres' "$UPDATER_ORIGEN"
if [[ ! -f "$RUN/actualizador.ok" ]]; then
  paso "Instalando version revisada del actualizador y servicio systemd..."
  install -m 700 "$UPDATER_ORIGEN" "$UPDATER_INSTALADO.tmp"
  mv "$UPDATER_INSTALADO.tmp" "$UPDATER_INSTALADO"
  # Drop-in persistente: ExecStart nunca apunta al script historico tras git reset.
  sudo install -d -m 755 /etc/systemd/system/lastmileuy-update.service.d
  printf '[Service]\nExecStart=\nExecStart=/usr/bin/bash %s\n' "$UPDATER_INSTALADO" |
    sudo tee /etc/systemd/system/lastmileuy-update.service.d/10-updater-revisado.conf >/dev/null
  sudo systemctl daemon-reload

  # Primera ejecucion sin timer: debe terminar satisfactoriamente y quedar
  # sincronizada con la ultima version que realmente se publico en GHCR.
  paso "Comprobando imagenes publicadas y aplicando migraciones pendientes con backup previo..."
  sudo systemctl start lastmileuy-update.service
  sudo systemctl is-failed --quiet lastmileuy-update.service && {
    echo 'El actualizador fallo; no se marcara la restauracion completa' >&2; exit 1;
  }
  # El updater puede salir 0 sin desplegar cuando GitHub aun no publico las
  # imagenes: eso NO es exito del proceso completo.
  git fetch origin main
  [[ "$(cat "$STATE/version")" == "$(git rev-parse origin/main)" ]] || {
    echo 'La nueva version aun no se desplego. Reanudar cuando terminen las publicaciones.' >&2; exit 1;
  }
  [[ "$(git rev-parse HEAD)" == "$(cat "$STATE/version")" ]] || {
    echo 'El codigo no coincide con la version desplegada' >&2; exit 1;
  }
  mark actualizador
fi

paso "Habilitando actualizaciones automaticas cada cinco minutos..."
sudo systemctl enable --now lastmileuy-update.timer
sudo systemctl is-enabled --quiet lastmileuy-update.timer
sudo systemctl is-active --quiet lastmileuy-update.timer

paso "Finalizando verificaciones de reconstruccion..."
mark completa
sudo touch "$STATE/restauracion-completa"
sudo chown azureuser:azureuser "$STATE/restauracion-completa"
echo 'ACTUALIZADOR_HABILITADO_OK'
echo 'RESTAURACION_COMPLETA_OK'

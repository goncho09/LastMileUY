#!/usr/bin/env bash
set -Eeuo pipefail
REPO_URL=https://github.com/goncho09/LastMileUY.git
DESTINO=/opt/lastmileuy
USUARIO=azureuser
ESTADO=/var/lib/lastmileuy-deploy

[[ $EUID -eq 0 ]] || { echo 'Ejecutar con sudo' >&2; exit 1; }
id "$USUARIO" >/dev/null

echo "PASO: Actualizando repositorios de paquetes..."
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl git jq tar gzip openssh-client caddy

echo "PASO: Comprobando motor Docker y Compose..."
if ! command -v docker >/dev/null 2>&1; then
    apt-get update
    apt-get install -y docker.io docker-compose-v2
    systemctl enable --now docker
fi

docker compose version >/dev/null
systemctl enable --now docker
systemctl disable --now lastmileuy-update.timer 2>/dev/null || true
systemctl enable caddy
systemctl stop caddy || true

usermod -aG docker "$USUARIO"

echo "PASO: Preparando codigo de LastMileUY en el servidor..."
if [[ ! -e "$DESTINO/.git" ]]; then
  [[ ! -e "$DESTINO" || -z "$(ls -A "$DESTINO" 2>/dev/null)" ]] || { echo 'Destino ocupado' >&2; exit 1; }
  git clone --branch main "$REPO_URL" "$DESTINO"
fi

[[ "$(git -C "$DESTINO" remote get-url origin)" == "$REPO_URL" ]]
[[ -z "$(git -C "$DESTINO" status --porcelain)" ]] || { echo 'Repositorio con cambios locales' >&2; exit 1; }
[[ "$(git -C "$DESTINO" branch --show-current)" == main ]] || { echo 'La rama no es main' >&2; exit 1; }
# IMPORTANTE: en reanudaciones nunca actualizar un checkout ya fijado al
# commit del respaldo; restaurar-vm.sh administra esa compatibilidad.

# El commit de los scripts de recuperacion debe estar publicado en main.
# El nodo Mnesia del respaldo se fija con un override independiente durante
# la restauracion: no depender de que un commit historico incluya esa opcion.
[[ -f "$DESTINO/docker-compose.azure.yml" && -f "$DESTINO/compose.deploy.yaml" ]] || {
  echo 'Faltan archivos Compose en el repositorio' >&2; exit 1;
}
chown -R "$USUARIO:$USUARIO" "$DESTINO"

echo "PASO: Instalando unidades systemd del actualizador..."
install -d -o "$USUARIO" -g "$USUARIO" -m 750 "$ESTADO"
install -m 644 "$DESTINO/scripts/deploy/lastmileuy-update.service" /etc/systemd/system/lastmileuy-update.service
install -m 644 "$DESTINO/scripts/deploy/lastmileuy-update.timer" /etc/systemd/system/lastmileuy-update.timer

systemctl daemon-reload
systemctl disable --now lastmileuy-update.timer 2>/dev/null || true
echo INSTALACION_BASE_OK

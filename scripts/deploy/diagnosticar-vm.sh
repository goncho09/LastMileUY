#!/usr/bin/env bash
set -Eeuo pipefail
PROJECT=/opt/lastmileuy
STATE=/var/lib/lastmileuy-deploy
printf '=== VERSION ===\n'
if [[ -f "$STATE/version" ]]; then cat "$STATE/version"; else echo 'Sin version registrada'; fi
printf '=== TIMER ===\n'
systemctl is-enabled lastmileuy-update.timer || true
systemctl is-active lastmileuy-update.timer || true
printf '=== CONTENEDORES ===\n'
docker ps --format 'table {{.Names}}\t{{.Status}}'
printf '=== POSTGRES ===\n'
docker exec lastmileuy-postgres pg_isready -U lastmileuy -d lastmileuy
printf '=== MIGRACIONES EF CORE ===\n'
docker exec lastmileuy-postgres psql -U lastmileuy -d lastmileuy -Atc 'SELECT count(*) FROM public."__EFMigrationsHistory"'
printf '=== RABBITMQ ===\n'
docker exec lastmileuy-rabbitmq rabbitmq-diagnostics -q ping
docker exec lastmileuy-rabbitmq rabbitmqctl -q list_queues -p / name
printf '=== HTTP LOCAL ===\n'
curl --fail --silent --show-error --max-time 20 'http://127.0.0.1:8080/health'
printf '\nDIAGNOSTICO_OK\n'


#!/usr/bin/env bash
set -euo pipefail

IP_PUBLICA="${1:?Uso: configurar-caddy.sh IP_PUBLICA}"

# Validar IPv4
if [[ ! "$IP_PUBLICA" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
    echo "IP publica invalida"
    exit 1
fi

DOMINIO="${IP_PUBLICA//./-}.sslip.io"

cat > /etc/caddy/Caddyfile <<EOF
api.${DOMINIO} {
    reverse_proxy 127.0.0.1:8080
}

backoffice.${DOMINIO} {
    reverse_proxy 127.0.0.1:8081
}

comercio.${DOMINIO} {
    reverse_proxy 127.0.0.1:8082
}

repartidor.${DOMINIO} {
    reverse_proxy 127.0.0.1:8083
}

seguimiento.${DOMINIO} {
    reverse_proxy 127.0.0.1:8084
}

receptor.${DOMINIO} {
    reverse_proxy 127.0.0.1:8085
}
EOF

caddy fmt --overwrite /etc/caddy/Caddyfile
caddy validate --config /etc/caddy/Caddyfile

systemctl enable --now caddy
systemctl reload caddy

echo "Caddy configurado para ${DOMINIO}"

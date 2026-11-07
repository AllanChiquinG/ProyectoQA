#!/usr/bin/env bash
# Publica el ambiente QA en internet con ngrok, en un contenedor. Solo funciona mientras este equipo, Docker y QA estén encendidos.
# Uso: scripts/qa-publicar.sh up | status | down
# El token va en .env.ngrok (ignorado por Git), con la línea NGROK_AUTHTOKEN=<tu token>.
# QA expuesto debe usar una contraseña fuerte y un límite bajo de inicios de sesión: AUTH_PERMITS_PER_MINUTE=30 scripts/qa.sh up
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1

domain="${NGROK_DOMAIN:-playoff-freebie-eardrum.ngrok-free.dev}"
name="medicore-qa-ngrok"
image="ngrok/ngrok:3-alpine"

case "${1:-status}" in
  up)
    [ -f .env.ngrok ] || { echo "Falta .env.ngrok con NGROK_AUTHTOKEN=<tu token>"; exit 1; }
    docker inspect medicore-qa-proxy-1 >/dev/null 2>&1 || { echo "QA no está levantado: ejecuta scripts/qa.sh up"; exit 1; }
    docker rm -f "$name" >/dev/null 2>&1 || true
    docker run -d --name "$name" --restart unless-stopped --network medicore-qa_edge --env-file .env.ngrok \
      "$image" http proxy:80 --url="https://$domain" --log=stdout >/dev/null
    for _ in $(seq 1 15); do
      code="$(curl -s -o /dev/null -w '%{http_code}' -H 'ngrok-skip-browser-warning: 1' "https://$domain/api/health" || true)"
      [ "$code" = 200 ] && break
      sleep 2
    done
    echo "GET https://$domain/api/health = ${code:-sin respuesta}"
    [ "${code:-}" = 200 ] || { docker logs --tail 8 "$name"; exit 1; }
    ;;
  status)
    docker ps --filter "name=$name" --format '{{.Names}} {{.Status}}'
    ;;
  down)
    docker rm -f "$name"
    ;;
  *)
    echo "Uso: scripts/qa-publicar.sh up|status|down"; exit 2
    ;;
esac

#!/usr/bin/env bash
# Levanta la API y la web de MediCore en contenedores Linux (SDK de .NET), conectadas a la red de docker compose (medicore_default), para
# poder ejecutar Cypress en equipos Windows con Smart App Control (que bloquea los .dll sin firma recién compilados).
#
# - Usa las dependencias que ya levantó "docker compose up -d" (postgres, redis, rabbitmq) por su nombre de servicio.
# - NO crea ni borra volúmenes ni redes; "down" solo elimina los dos contenedores medicore-dev-api y medicore-dev-web.
# - Los secretos salen de .env (ignorado por Git) y de los user-secrets de la API; se escriben en un archivo temporal FUERA del repositorio
#   (carpeta temporal del sistema) que "down" borra. Nunca se imprimen.
# - La API corre en Development: aplica las migraciones pendientes a la base de desarrollo y siembra los usuarios ficticios (idempotente).
#
# Uso:  scripts/dev-stack-linux.sh up | down | logs [api|web] | status
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -W 2>/dev/null || pwd)"
image="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"
network="${COMPOSE_NETWORK:-medicore_default}"
envfile="${TMPDIR:-${TEMP:-/tmp}}/medicore-dev-stack.env"

action="${1:?Indica: up | down | logs | status}"

make_env() {
  python "$root/scripts/dev-stack-env.py" "$root" "$envfile"
}

case "$action" in
  up)
    make_env
    MSYS_NO_PATHCONV=1 docker rm -f medicore-dev-api medicore-dev-web >/dev/null 2>&1 || true
    common=(-d --network "$network" -v "$root:/repo:ro" -v medicore-nuget:/root/.nuget/packages --env-file "$envfile")
    prepare='set -e; mkdir -p /work; tar -C /repo --exclude=./.git --exclude="*/bin" --exclude="*/obj" --exclude=node_modules --exclude=./reports -cf - . | tar -C /work -xf -; cd /work; rm -f global.json'
    MSYS_NO_PATHCONV=1 docker run "${common[@]}" --name medicore-dev-api -p 127.0.0.1:5011:5011 -e ASPNETCORE_URLS=http://0.0.0.0:5011 "$image" \
      bash -c "$prepare; dotnet build src/MediCore.Api -v q && dotnet run --no-build --no-launch-profile --project src/MediCore.Api" >/dev/null
    MSYS_NO_PATHCONV=1 docker run "${common[@]}" --name medicore-dev-web -p 127.0.0.1:5298:5298 -e ASPNETCORE_URLS=http://0.0.0.0:5298 -e Api__BaseUrl=http://medicore-dev-api:5011 "$image" \
      bash -c "$prepare; dotnet build src/MediCore.Web -v q && dotnet run --no-build --no-launch-profile --project src/MediCore.Web" >/dev/null
    echo "Esperando a que la API y la web respondan..."
    for i in $(seq 1 90); do
      api=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5011/api/health 2>/dev/null || true)
      web=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5298/Login 2>/dev/null || true)
      if [ "$api" = "200" ] && [ "$web" = "200" ]; then echo "Listas (API $api, web $web) tras $((i*3)) s."; exit 0; fi
      sleep 3
    done
    echo "No respondieron a tiempo (API $api, web $web). Revisa: scripts/dev-stack-linux.sh logs" >&2
    exit 1
    ;;
  down)
    MSYS_NO_PATHCONV=1 docker rm -f medicore-dev-api medicore-dev-web >/dev/null 2>&1 || true
    rm -f "$envfile"
    echo "Contenedores medicore-dev-api y medicore-dev-web eliminados (volúmenes y red de compose intactos)."
    ;;
  logs)
    docker logs --tail 80 "medicore-dev-${2:-api}"
    ;;
  status)
    docker ps -a --filter name=medicore-dev- --format '{{.Names}} {{.Status}}'
    ;;
  *)
    echo "Acción no reconocida: $action" >&2
    exit 2
    ;;
esac

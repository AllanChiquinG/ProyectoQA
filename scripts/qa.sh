#!/usr/bin/env bash
# Operación del ambiente QA con Docker Compose (docker-compose.yml + docker-compose.qa.yml, proyecto "medicore-qa").
# Funciona igual en el equipo local y en la VM de Azure (Ansible usa estos mismos archivos).
#
#   scripts/qa.sh init      crea .env.qa con contraseñas aleatorias (no sobrescribe uno existente)
#   scripts/qa.sh up        construye las imágenes y levanta todo (migra antes de arrancar la API)
#   scripts/qa.sh seed      siembra usuarios y datos FICTICIOS de QA (idempotente)
#   scripts/qa.sh status    estado y salud de cada servicio
#   scripts/qa.sh logs [servicio]
#   scripts/qa.sh restart   reinicia los contenedores SIN borrar volúmenes
#   scripts/qa.sh down      detiene y elimina los contenedores y la red; CONSERVA los volúmenes (datos)
#   scripts/qa.sh destroy   igual que down pero BORRA los volúmenes de QA (medicore-qa_*); pide escribir el nombre del proyecto
#   scripts/qa.sh smoke     pruebas de humo de web y API a través del proxy
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
project="medicore-qa"
envfile="${QA_ENV_FILE:-.env.qa}"
compose=(docker compose -p "$project" --env-file "$envfile" -f docker-compose.yml -f docker-compose.qa.yml)

random() { head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "${1:-24}"; }

need_env() {
  [ -f "$envfile" ] || { echo "Falta $envfile. Ejecuta: scripts/qa.sh init" >&2; exit 1; }
}

case "${1:-}" in
  init)
    if [ -f "$envfile" ]; then echo "$envfile ya existe: no se sobrescribe."; exit 0; fi
    sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(random 28)|" \
        -e "s|^REDIS_PASSWORD=.*|REDIS_PASSWORD=$(random 28)|" \
        -e "s|^RABBITMQ_DEFAULT_PASS=.*|RABBITMQ_DEFAULT_PASS=$(random 28)|" \
        -e "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=$(random 48)|" \
        -e "s|^SEED_DEV_PASSWORD=.*|SEED_DEV_PASSWORD=Qa-$(random 20)-1!|" \
        .env.qa.example > "$envfile"
    chmod 600 "$envfile" 2>/dev/null || true
    echo "Creado $envfile con contraseñas aleatorias (no se versiona)."
    ;;
  up)
    need_env
    "${compose[@]}" up -d --build --remove-orphans
    "${compose[@]}" ps
    ;;
  seed)
    need_env
    "${compose[@]}" --profile seed run --rm seed
    ;;
  status)
    need_env
    "${compose[@]}" ps -a --format 'table {{.Service}}\t{{.State}}\t{{.Health}}\t{{.Status}}'
    ;;
  logs)
    need_env
    "${compose[@]}" logs --tail 100 "${@:2}"
    ;;
  restart)
    need_env
    "${compose[@]}" restart
    ;;
  down)
    need_env
    "${compose[@]}" down --remove-orphans
    echo "Contenedores eliminados. Los volúmenes de datos se conservan (medicore-qa_*)."
    ;;
  destroy)
    need_env
    echo "Esto BORRA los volúmenes de QA (${project}_*): base de datos, Redis, RabbitMQ y claves. No toca los del desarrollo (medicore_*)."
    read -r -p "Escribe el nombre del proyecto (${project}) para confirmar: " answer
    [ "$answer" = "$project" ] || { echo "Cancelado."; exit 1; }
    "${compose[@]}" down --remove-orphans --volumes
    ;;
  smoke)
    exec bash scripts/qa-smoke.sh
    ;;
  *)
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac

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
#   scripts/qa.sh backup    crea un respaldo cifrado ahora (además de los automáticos)
#   scripts/qa.sh backups   lista los respaldos
#   scripts/qa.sh export <carpeta>           copia los respaldos fuera de Docker; guarda esa copia en otro disco
#   scripts/qa.sh import <archivo.dump.enc>  trae un respaldo exportado (y su .sha256) de vuelta al volumen
#   scripts/qa.sh restore <archivo>          REEMPLAZA la base con un respaldo; pide escribir el nombre del proyecto
set -euo pipefail
export MSYS_NO_PATHCONV=1  # Git Bash convertiría las rutas de los contenedores en rutas de Windows

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
        -e "s|^BACKUP_PASSPHRASE=.*|BACKUP_PASSPHRASE=$(random 48)|" \
        -e "s|^SEED_DEV_PASSWORD=.*|SEED_DEV_PASSWORD=Qa-$(random 20)-1!|" \
        .env.qa.example > "$envfile"
    chmod 600 "$envfile" 2>/dev/null || true
    echo "Creado $envfile con contraseñas aleatorias (no se versiona)."
    ;;
  up)
    need_env
    docker volume create medicore-qa-respaldos >/dev/null
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
    # El pipeline confirma con QA_CONFIRM=medicore-qa: cada corrida parte de datos limpios.
    answer="${QA_CONFIRM:-}"
    [ -n "$answer" ] || read -r -p "Escribe el nombre del proyecto (${project}) para confirmar: " answer
    [ "$answer" = "$project" ] || { echo "Cancelado."; exit 1; }
    "${compose[@]}" down --remove-orphans --volumes
    ;;
  smoke)
    exec bash scripts/qa-smoke.sh
    ;;
  backup)
    need_env
    "${compose[@]}" exec -T backup sh /usr/local/bin/respaldo.sh una
    ;;
  backups)
    need_env
    "${compose[@]}" exec -T backup sh -c 'ls -lh /backups/*.dump.enc'
    ;;
  export)
    need_env
    target="${2:?Indica la carpeta de destino}"
    mkdir -p "$target"
    "${compose[@]}" cp backup:/backups/. "$target"
    echo "Copiado a $target. Guárdalo en otro disco, junto con BACKUP_PASSPHRASE: sin esa clave el respaldo no se puede leer."
    ;;
  import)
    need_env
    file="${2:?Indica el archivo .dump.enc}"
    "${compose[@]}" cp "$file" "backup:/backups/$(basename "$file")"
    "${compose[@]}" cp "$file.sha256" "backup:/backups/$(basename "$file").sha256"
    echo "Importado: $(basename "$file")"
    ;;
  restore)
    need_env
    file="${2:?Indica el archivo; usa scripts/qa.sh backups}"
    echo "Esto REEMPLAZA la base de datos de QA con $(basename "$file") y cierra todas las sesiones."
    answer="${QA_CONFIRM:-}"
    [ -n "$answer" ] || read -r -p "Escribe el nombre del proyecto (${project}) para confirmar: " answer
    [ "$answer" = "$project" ] || { echo "Cancelado."; exit 1; }
    "${compose[@]}" stop proxy web worker api
    "${compose[@]}" exec -T backup sh /usr/local/bin/respaldo.sh restaurar "$(basename "$file")"
    # Cache y sesiones de Redis ya no corresponden a la base restaurada.
    "${compose[@]}" exec -T redis sh -c 'redis-cli -a "$REDIS_PASSWORD" --no-auth-warning FLUSHALL'
    "${compose[@]}" up -d --wait api worker web proxy
    ;;
  *)
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac

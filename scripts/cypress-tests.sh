#!/usr/bin/env bash
# Ejecuta Cypress en un contenedor contra el ambiente QA, con un límite de inicios de sesión amplio solo durante la corrida.
# Uso: scripts/cypress-tests.sh [spec]
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
envfile="${QA_ENV_FILE:-.env.qa}"
value() { grep "^$1=" "$envfile" | cut -d= -f2-; }
compose=(docker compose -p medicore-qa --env-file "$envfile" -f docker-compose.yml -f docker-compose.qa.yml)
mkdir -p reports/cypress reports/accesibilidad

AUTH_PERMITS_PER_MINUTE=100000 "${compose[@]}" up -d --wait api >/dev/null
restore() { "${compose[@]}" up -d --wait api >/dev/null; }
trap restore EXIT

spec=()
[ -n "${1:-}" ] && spec=(--spec "$1")
MSYS_NO_PATHCONV=1 docker run --rm --network medicore-qa_backend --entrypoint bash \
  -v "$root:/repo" -w /repo/tests/cypress \
  -e MEDICORE_WEB_URL=http://proxy -e MEDICORE_API_URL=http://proxy \
  -e MEDICORE_E2E_DB="postgres://$(value POSTGRES_USER):$(value POSTGRES_PASSWORD)@127.0.0.1:5433/$(value POSTGRES_DB)" \
  -e CYPRESS_MEDICORE_PASSWORD="$(value SEED_DEV_PASSWORD)" \
  "${CYPRESS_IMAGE:-cypress/included:16.2.0}" -c \
  "node /repo/ci/tcp-forward.js 5433 postgres 5432 & cypress run --browser chrome ${spec[*]:-}" 2>&1 | tee reports/cypress/resultado.txt | tail -40
exit "${PIPESTATUS[0]}"

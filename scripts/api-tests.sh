#!/usr/bin/env bash
# Ejecuta la colección de Insomnia contra el ambiente QA en contenedores (el proxy se alcanza por la red interna).
# Requiere: scripts/qa.sh up && scripts/qa.sh seed. Escribe el reporte en reports/insomnia/.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
envfile="${QA_ENV_FILE:-.env.qa}"
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
image="${INSO_IMAGE:-kong/inso:11.6.1}"
mkdir -p reports/insomnia

MSYS_NO_PATHCONV=1 docker run --rm --network medicore-qa_edge \
  -v "$(pwd -W 2>/dev/null || pwd)/tests/insomnia:/data:ro" -w /data \
  "$image" run collection -w /data/medicore.insomnia.json -e env_medicore wrk_medicore \
  --env-var "base_url=${QA_INTERNAL_URL:-http://proxy}" --env-var "password=$password" \
  --reporter "${INSO_REPORTER:-spec}" --delay-request 50 2>&1 | tee reports/insomnia/resultado.txt
exit "${PIPESTATUS[0]}"

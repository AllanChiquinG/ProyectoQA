#!/usr/bin/env bash
# Dispara muchas reservas a la vez contra QA y luego comprueba que no haya doble reserva.
# Uso: scripts/perf-citas.sh [cantidad] [distintas|mismo]
#   distintas: todas en horarios libres, se crean todas.
#   mismo: todas al mismo horario del mismo médico, solo una puede ganar.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
citas="${1:-100}"
modo="${2:-distintas}"
envfile="${QA_ENV_FILE:-.env.qa}"
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
root="$(pwd -W 2>/dev/null || pwd)"
mkdir -p reports/k6

MSYS_NO_PATHCONV=1 docker run --rm --network medicore-qa_edge --user 0:0 \
  -v "$root/tests/k6:/scripts:ro" -v "$root/reports/k6:/reportes" \
  -e BASE_URL="${QA_INTERNAL_URL:-http://proxy}" -e MEDICORE_PASSWORD="$password" -e CITAS="$citas" -e MODO="$modo" \
  "${K6_IMAGE:-grafana/k6:0.57.0}" run --quiet /scripts/citas.js
code=$?

echo
bash scripts/perf-integridad.sh
exit "$code"

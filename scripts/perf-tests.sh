#!/usr/bin/env bash
# Ejecuta k6 contra el ambiente QA en contenedores. Uso: scripts/perf-tests.sh smoke|load|stress|spike
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
perfil="${1:-smoke}"
envfile="${QA_ENV_FILE:-.env.qa}"
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
root="$(pwd -W 2>/dev/null || pwd)"
mkdir -p reports/k6

MSYS_NO_PATHCONV=1 docker run --rm --network medicore-qa_edge --user 0:0 \
  -v "$root/tests/k6:/scripts:ro" -v "$root/reports/k6:/reportes" \
  -e BASE_URL="${QA_INTERNAL_URL:-http://proxy}" -e MEDICORE_PASSWORD="$password" -e PERFIL="$perfil" \
  "${K6_IMAGE:-grafana/k6:0.57.0}" run --quiet /scripts/medicore.js

#!/usr/bin/env bash
# OWASP ZAP sobre el ambiente QA en contenedores: web (pasivo y activo acotado) y API con sesión real. Guarda HTML y JSON en reports/zap/.
# Solo se usa contra el ambiente propio. La decisión sobre los hallazgos la toma scripts/quality-gate.py.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
image="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
target="${QA_INTERNAL_URL:-http://proxy}"
envfile="${QA_ENV_FILE:-.env.qa}"
mkdir -p reports/zap
chmod 777 reports/zap 2>/dev/null || true

MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 --network medicore-qa_edge -v "$root/reports/zap:/zap/wrk:rw" "$image" \
  zap-full-scan.py -t "$target/Login" -m 3 -T 25 -I -r zap.html -J zap.json
web_code=$?

# API: contrato OpenAPI (documentacion/openapi-v1.json) con el token de un usuario administrador de QA.
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
token="$(MSYS_NO_PATHCONV=1 docker run --rm --network medicore-qa_edge curlimages/curl:8.13.0 -s -X POST "$target/api/auth/login" \
  -H 'Content-Type: application/json' -d "{\"email\":\"admin@example.com\",\"password\":\"$password\"}" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')"
python3 scripts/zap-openapi.py reports/zap/openapi-v1.json 2>/dev/null || python scripts/zap-openapi.py reports/zap/openapi-v1.json
MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 --network medicore-qa_edge -v "$root/reports/zap:/zap/wrk:rw" \
  -e ZAP_AUTH_HEADER=Authorization -e ZAP_AUTH_HEADER_VALUE="Bearer $token" "$image" \
  zap-api-scan.py -t /zap/wrk/openapi-v1.json -f openapi -O "$target" -T 12 -I -r zap-api.html -J zap-api.json
api_code=$?
rm -f reports/zap/openapi-v1.json
exit $((web_code + api_code))

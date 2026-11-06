#!/usr/bin/env bash
# OWASP ZAP sobre el ambiente QA en contenedores: exploración pasiva y activa acotada. Guarda HTML y JSON en reports/zap/.
# Solo se usa contra el ambiente propio. La decisión sobre los hallazgos la toma scripts/quality-gate.py.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
image="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
target="${QA_INTERNAL_URL:-http://proxy}"
mkdir -p reports/zap
chmod 777 reports/zap 2>/dev/null || true

MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 --network medicore-qa_edge -v "$root/reports/zap:/zap/wrk:rw" "$image" \
  zap-full-scan.py -t "$target/Login" -m 3 -T 25 -I -r zap.html -J zap.json

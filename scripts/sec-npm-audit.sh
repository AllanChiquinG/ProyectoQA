#!/usr/bin/env bash
# npm audit del único proyecto Node del repositorio (tests/cypress). Guarda el JSON y no falla aquí: decide scripts/quality-gate.py.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
mkdir -p reports/npm-audit
MSYS_NO_PATHCONV=1 docker run --rm -v "$root/tests/cypress:/app:ro" -w /app node:22-alpine npm audit --json > reports/npm-audit/audit.json
python3 -c 'import json; print(json.load(open("reports/npm-audit/audit.json"))["metadata"]["vulnerabilities"])' 2>/dev/null \
  || python -c 'import json; print(json.load(open("reports/npm-audit/audit.json"))["metadata"]["vulnerabilities"])'

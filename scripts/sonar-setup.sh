#!/usr/bin/env bash
# Crea en SonarQube el Quality Gate del proyecto. "Sonar way" solo evalúa código nuevo y en un primer análisis aprueba sin condiciones.
# Variables: SONAR_ADMIN_PASSWORD. Se ejecuta una vez por servidor.
set -euo pipefail
host="${SONAR_LOCAL_URL:-http://127.0.0.1:9000}"
api() { curl -s -f -u "admin:$SONAR_ADMIN_PASSWORD" -X POST "$host/api/$1" "${@:2}"; }
api qualitygates/create --data-urlencode name=MediCore >/dev/null || true
condition() { api qualitygates/create_condition --data-urlencode gateName=MediCore -d "metric=$1&op=$2&error=$3" >/dev/null || true; }
condition vulnerabilities GT 0
condition security_rating GT 1
condition reliability_rating GT 2
condition sqale_rating GT 1
condition duplicated_lines_density GT 3
condition coverage LT "${SONAR_MIN_COVERAGE:-60}"
api qualitygates/select --data-urlencode gateName=MediCore -d projectKey=medicore
echo "Quality Gate MediCore asignado."

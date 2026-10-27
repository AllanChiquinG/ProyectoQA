#!/usr/bin/env bash
# Pruebas de humo del ambiente QA a través del proxy inverso (web + API + dependencias). No crea datos.
# Variables: QA_URL (por defecto http://127.0.0.1:8080), QA_ENV_FILE (por defecto .env.qa; para leer SEED_DEV_PASSWORD).
# Sale con código distinto de 0 si algo falla; imprime una línea por comprobación.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
base="${QA_URL:-http://127.0.0.1:8080}"
envfile="${QA_ENV_FILE:-.env.qa}"
failures=0
check() {  # check "descripción" comando...
  local what="$1"; shift
  if "$@" >/dev/null 2>&1; then echo "OK    $what"; else echo "FALLA $what"; failures=$((failures + 1)); fi
}
status_is() { [ "$(curl -s -o /dev/null -w '%{http_code}' "${@:2}")" = "$1" ]; }
body_has() { curl -s "${@:2}" | grep -q "$1"; }

check "API liveness (GET /api/health = 200)" status_is 200 "$base/api/health"
check "API readiness con PostgreSQL (GET /api/health/ready = 200)" status_is 200 "$base/api/health/ready"
check "Readiness informa postgres ok" body_has '"postgres":"ok"' "$base/api/health/ready"
check "API exige autenticación (GET /api/patients = 401)" status_is 401 "$base/api/patients"
check "Web: página de inicio de sesión (GET /Login = 200)" status_is 200 "$base/Login"
check "Web: la página de inicio de sesión está en español" body_has "Iniciar sesión" "$base/Login"
check "Web: sin sesión redirige al inicio de sesión (GET / = 302)" status_is 302 "$base/"
check "Web: cabeceras de seguridad (CSP)" bash -c "curl -sI '$base/Login' | grep -qi '^content-security-policy'"
check "El proxy no expone la documentación OpenAPI en QA (GET /openapi/v1.json != 200)" bash -c "[ \"\$(curl -s -o /dev/null -w '%{http_code}' '$base/openapi/v1.json')\" != 200 ]"

if [ -f "$envfile" ] && grep -q '^SEED_DEV_PASSWORD=' "$envfile"; then
  password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
  login="$(curl -s -X POST "$base/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"elena.vargas@example.com\",\"password\":\"$password\"}")"
  if echo "$login" | grep -q accessToken; then
    echo "OK    Inicio de sesión de Recepción por la API (usuarios ficticios sembrados)"
    token="$(echo "$login" | sed 's/.*"accessToken":"\([^"]*\)".*/\1/')"
    check "API autenticada: listado de médicos (GET /api/doctors = 200)" status_is 200 "$base/api/doctors" -H "Authorization: Bearer $token"
  else
    echo "OMIT  Inicio de sesión (¿se sembró con scripts/qa.sh seed?)"
  fi
fi

[ "$failures" -eq 0 ] && echo "Humo correcto." || { echo "$failures comprobación(es) fallaron."; exit 1; }

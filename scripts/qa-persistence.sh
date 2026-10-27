#!/usr/bin/env bash
# Comprueba la PERSISTENCIA del ambiente QA: crea datos por la API, reinicia los contenedores y vuelve a crearlos desde cero (down + up) SIN borrar
# volúmenes, y verifica que los datos, la notificación asíncrona (worker + RabbitMQ) y la sesión de la web sobreviven.
# Requiere el ambiente levantado y sembrado (scripts/qa.sh up && scripts/qa.sh seed). Usa solo datos ficticios nuevos.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
base="${QA_URL:-http://127.0.0.1:8080}"
envfile="${QA_ENV_FILE:-.env.qa}"
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2-)"
stamp="$(date +%s)"
failures=0
ok() { echo "OK    $1"; }
fail() { echo "FALLA $1"; failures=$((failures + 1)); }
json() { python -c "import sys,json; d=json.load(sys.stdin); print(eval(sys.argv[1]))" "$1"; }
login() { curl -s -X POST "$base/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$password\"}" | json "d['accessToken']"; }
api() { local token="$1" method="$2" path="$3"; shift 3; curl -s -X "$method" "$base$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' "$@"; }
wait_healthy() {
  for _ in $(seq 1 60); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/api/health/ready")" = "200" ] && [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/Login")" = "200" ] && return 0
    sleep 3
  done
  return 1
}

echo "== 1. Datos nuevos por la API"
recep="$(login elena.vargas@example.com)"; lab="$(login daniela.reyes@example.com)"; paciente="$(login e2e.paciente@example.com)"
pid="$(api "$recep" POST /api/patients -d "{\"fullName\":\"Paciente Persistencia $stamp\",\"identificationNumber\":\"PERS-$stamp\",\"birthDate\":\"1990-01-01\",\"phone\":\"5550 1234\",\"email\":\"persistencia@example.com\"}" | json "d['id']")"
epid="$(api "$lab" GET "/api/labs/patients?q=CYP-E2E-004" | json "d[0]['id']")"
rid="$(api "$lab" POST /api/labs/results -d "{\"patientId\":\"$epid\",\"examType\":\"Persistencia $stamp\",\"performedOn\":\"$(date -d yesterday +%F 2>/dev/null || date -v-1d +%F)\",\"parameters\":[{\"name\":\"Glucosa\",\"value\":\"90\",\"unit\":\"mg/dL\",\"referenceRange\":\"70-100\"}]}" | json "d['id']")"
[ -n "$pid" ] && [ -n "$rid" ] && ok "Paciente $pid y resultado $rid creados" || { fail "No se pudieron crear los datos"; exit 1; }

verify() {  # verify "etapa"
  local stage="$1"
  recep="$(login elena.vargas@example.com)"; lab="$(login daniela.reyes@example.com)"; paciente="$(login e2e.paciente@example.com)"
  [ "$(api "$recep" GET "/api/patients/$pid" | json "d['id']")" = "$pid" ] && ok "[$stage] el paciente creado sigue en PostgreSQL" || fail "[$stage] falta el paciente"
  [ "$(api "$lab" GET "/api/labs/results/$rid" | json "d['id']")" = "$rid" ] && ok "[$stage] el resultado de laboratorio sigue en PostgreSQL" || fail "[$stage] falta el resultado"
  # La notificación la genera el worker de forma asíncrona (outbox -> RabbitMQ -> consumidor): se espera hasta 60 s.
  local seen=""
  for _ in $(seq 1 30); do
    seen="$(api "$paciente" GET "/api/notifications?pageSize=100" | json "[n['targetId'] for n in d['items']].count('$rid')")"
    [ "$seen" = "1" ] && break
    sleep 2
  done
  [ "$seen" = "1" ] && ok "[$stage] la notificación del resultado existe una sola vez (worker + RabbitMQ)" || fail "[$stage] notificación: $seen"
}

verify "inicial"

echo "== 2. Reinicio de contenedores (docker compose restart)"
scripts/qa.sh restart >/dev/null 2>&1; wait_healthy && ok "El ambiente volvió a responder" || fail "El ambiente no volvió"
verify "tras restart"

echo "== 3. Eliminar y recrear los contenedores (down + up, volúmenes intactos)"
volumes_before="$(docker volume ls --format '{{.Name}}' | grep -c '^medicore-qa_')"
scripts/qa.sh down >/dev/null 2>&1
volumes_after="$(docker volume ls --format '{{.Name}}' | grep -c '^medicore-qa_')"
[ "$volumes_before" = "$volumes_after" ] && [ "$volumes_after" -gt 0 ] && ok "Los $volumes_after volúmenes medicore-qa_* se conservaron tras down" || fail "Volúmenes: antes $volumes_before, después $volumes_after"
scripts/qa.sh up >/dev/null 2>&1; wait_healthy && ok "El ambiente volvió a levantarse (la migración no repite ni pierde nada)" || fail "No levantó"
verify "tras down + up"

[ "$failures" -eq 0 ] && echo "Persistencia correcta." || { echo "$failures comprobación(es) fallaron."; exit 1; }

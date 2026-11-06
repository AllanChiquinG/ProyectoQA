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

# API: un análisis por rol, con identificadores reales de QA. Administrador y Recepción no tienen acceso clínico; Médico, Laboratorio y Paciente sí.
# scripts/zap-cobertura.py mide en el registro del proxy qué rutas se alcanzaron (reports/zap/cobertura-api.json).
password="$(grep '^SEED_DEV_PASSWORD=' "$envfile" | cut -d= -f2- | tr -d '\r')"
python_cmd="$(command -v python3 >/dev/null 2>&1 && python3 -c '' 2>/dev/null && echo python3 || echo python)"
api_code=0
rm -f reports/zap/cobertura-api.json
# Cita pasada sin consulta: sin ella, POST /api/consultations no tiene ningún dato válido que analizar. Una reserva por la API no puede quedar en el pasado.
docker exec medicore-qa-postgres-1 sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
  insert into appointments (\"Id\",\"PatientId\",\"DoctorId\",\"StartsAtLocal\",\"EndsAtLocal\",\"Status\",\"Reason\",\"CreatedByUserId\",\"CreatedAtUtc\")
  select gen_random_uuid(), p.\"Id\", d.\"Id\", date_trunc(\$\$minute\$\$, now()) - interval \$\$3 days\$\$, date_trunc(\$\$minute\$\$, now()) - interval \$\$3 days\$\$ + interval \$\$30 minutes\$\$,
         \$\$Confirmed\$\$, \$\$Cita ficticia para ZAP\$\$, d.\"UserId\", now()
  from patients p, doctors d where p.\"Email\" = \$\$ana.garcia@example.com\$\$ and d.\"Email\" = \$\$lucia.mendez@example.com\$\$"' || true
# Orden necesario: el análisis activo altera datos. Laboratorio crea el resultado que Médico y Paciente leen; Administrador va al final porque modifica
# médicos y pacientes, y después de eso ninguna reserva sería válida.
for profile in laboratorio:daniela.reyes@example.com recepcion:elena.vargas@example.com medico:lucia.mendez@example.com                paciente:ana.garcia@example.com administrador:admin@example.com; do
  role="${profile%%:*}"
  email="${profile#*:}"
  suffix="-$role"; auth=""
  if [ "$role" = administrador ]; then suffix=""; auth="--auth"; fi  # conserva zap-api.json y es el único que analiza el login
  if [ "$role" = laboratorio ]; then auth="--lab-fixture Ana"; fi
  since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  token="$(MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 --network medicore-qa_edge -e ZAP_PASSWORD="$password" \
    -v "$root/reports/zap:/zap/wrk:rw" -v "$root/scripts:/scripts:ro" -v "$root/documentacion:/doc:ro" --entrypoint python3 "$image" \
    /scripts/zap-openapi.py --url "$target" --email "$email" --spec /doc/openapi-v1.json --out "/zap/wrk/openapi-$role.json" $auth)" \
    || { api_code=$((api_code + 1)); continue; }
  MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 --network medicore-qa_edge -v "$root/reports/zap:/zap/wrk:rw" \
    -e ZAP_AUTH_HEADER=Authorization -e ZAP_AUTH_HEADER_VALUE="Bearer $token" "$image" \
    zap-api-scan.py -t "/zap/wrk/openapi-$role.json" -f openapi -O "$target" -T 12 -I -z "-config scanner.maxScanDurationInMins=12" \
    -r "zap-api$suffix.html" -J "zap-api$suffix.json"
  api_code=$((api_code + $?))
  rm -f "reports/zap/openapi-$role.json"
  docker logs --since "$since" medicore-qa-proxy-1 2>&1 | "$python_cmd" scripts/zap-cobertura.py "$role" reports/zap/cobertura-api.json
done
exit $((web_code + api_code))

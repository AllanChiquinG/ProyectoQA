#!/usr/bin/env bash
# Simulacro de desastre del ambiente QA: respalda, exporta, DESTRUYE la base y el volumen de respaldos, reconstruye y restaura desde la copia exportada.
# Compara cada tabla (filas y huella md5) antes y después. Solo QA. La evidencia queda en reports/respaldos/simulacro.txt, sin datos: solo nombres, conteos y huellas.
set -euo pipefail
cd "$(dirname "$0")/.."

[ "${QA_CONFIRM:-}" = "medicore-qa" ] || { echo 'Destruye QA. Ejecuta con QA_CONFIRM=medicore-qa'; exit 1; }
copy="artifacts/respaldos"
out="reports/respaldos/simulacro.txt"
mkdir -p "$(dirname "$out")"
rm -rf "$copy"

fingerprint() {
  docker exec medicore-qa-postgres-1 sh -c '
    export PGOPTIONS="-c client_min_messages=warning"
    psql -q -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select tablename from pg_tables where schemaname = '"'"'public'"'"' order by 1" |
    while read -r t; do
      psql -q -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select '"'"'$t'"'"', count(*), coalesce(md5(string_agg(x::text, '"'"''"'"' order by x::text)), '"'"'vacia'"'"') from public.\"$t\" x"
    done'
}

fingerprint > "$(dirname "$out")/antes.tmp"

bash scripts/qa.sh backup
file="$(bash scripts/qa.sh backups | awk '{print $NF}' | sort | tail -1)"
file="$(basename "$file")"
bash scripts/qa.sh export "$copy"
exported_at="$(date +%s)"

# Desde aquí corre el reloj: todo lo que sigue es lo que tomaría recuperarse.
start="$(date +%s)"
bash scripts/qa.sh destroy
docker volume rm medicore-qa-respaldos
bash scripts/qa.sh up
tables_empty="$(docker exec medicore-qa-postgres-1 sh -c 'psql -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select count(*) from users"')"
bash scripts/qa.sh import "$copy/$file"
bash scripts/qa.sh restore "$file"
# Se compara antes del humo: su inicio de sesión agrega una fila legítima a la auditoría.
fingerprint > "$(dirname "$out")/despues.tmp"
bash scripts/qa.sh smoke
end="$(date +%s)"

equal="no"
diff "$(dirname "$out")/antes.tmp" "$(dirname "$out")/despues.tmp" && equal="sí"

{
  echo "Simulacro de restauración de MediCore QA: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Copia usada: $file (exportada fuera de Docker; volumen original destruido)"
  echo "Usuarios tras reconstruir y antes de restaurar: $tables_empty"
  echo "Tablas comparadas: $(wc -l < "$(dirname "$out")/antes.tmp")"
  echo "Filas antes: $(awk -F'|' '{s+=$2} END {print s}' "$(dirname "$out")/antes.tmp")"
  echo "Filas después: $(awk -F'|' '{s+=$2} END {print s}' "$(dirname "$out")/despues.tmp")"
  echo "Tablas idénticas: $equal"
  echo "Tiempo de recuperación (destruir, reconstruir, restaurar, humo): $((end - start)) s"
  echo "Pérdida en este simulacro: 0 (copia tomada $((start - exported_at)) s antes del desastre)"
  echo
  echo "tabla|filas|md5 después"
  cat "$(dirname "$out")/despues.tmp"
} > "$out"
rm -f "$(dirname "$out")/antes.tmp" "$(dirname "$out")/despues.tmp"
cat "$out"
[ "$equal" = "sí" ]

#!/usr/bin/env bash
# Comprueba el estado después de una prueba de carga: solapes, eventos pendientes y mensajes fallidos.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
compose=(docker compose -p medicore-qa --env-file "${QA_ENV_FILE:-.env.qa}" -f docker-compose.yml -f docker-compose.qa.yml)
overlap() {
  echo "select '$1', count(*) from appointments a join appointments b on a.\"Id\" < b.\"Id\" and a.\"$2\" = b.\"$2\" and a.\"Status\" = 'Confirmed' and b.\"Status\" = 'Confirmed' and a.\"StartsAtLocal\" < b.\"EndsAtLocal\" and b.\"StartsAtLocal\" < a.\"EndsAtLocal\""
}
{
  "${compose[@]}" exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -F" | " -f -' <<SQL
select 'citas confirmadas', count(*) from appointments where "Status" = 'Confirmed';
$(overlap 'solapes por medico' DoctorId);
$(overlap 'solapes por paciente' PatientId);
select 'eventos pendientes', count(*) from outbox_messages where "ProcessedAtUtc" is null;
select 'notificaciones', count(*) from notifications;
SQL
  "${compose[@]}" exec -T rabbitmq rabbitmqctl list_queues name messages 2>/dev/null | tail -3
} | tee reports/k6/integridad.txt

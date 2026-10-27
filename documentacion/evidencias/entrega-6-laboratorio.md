# Evidencia de la entrega 6: laboratorio y notificaciones asíncronas

Ejecutada el 2026-10-27 12:39:44 -0600 con `scripts/evidencia_laboratorio.py` contra la pila de desarrollo (PostgreSQL, Redis y RabbitMQ de docker compose; API en Development).
Solo datos ficticios `CYP-E2E-*`. El informe contiene identificadores y estados, no contenido clínico.

## A. Broker disponible: registro, notificación asíncrona y lectura del paciente
- `POST /api/labs/results` → **201** (resultado `67b037c0-5ae3-465a-8347-6f590b79450c`, estado inicial de la notificación: `Pending`).
- La notificación pasó a `Sent` de forma asíncrona: **True**; notificaciones guardadas para el resultado: **1**.
- Paciente `GET /api/notifications?unreadOnly=true` → **200**, notificaciones del resultado: **1** (tipo `LabResultReady`, sin leer: `True`).
- `GET /api/notifications/unread-count` → **200**, sin leer: **1**.
- `POST /api/notifications/{id}/read` → **204**.
- El paciente lee su propio resultado: `GET /api/labs/results/{id}` → **200**.

## B. RabbitMQ detenido: el resultado no se pierde y el evento pendiente se recupera
- `docker compose stop rabbitmq` → código 0.
- Con el broker detenido, `POST /api/labs/results` → **201** (resultado `eb2e4876-eb71-422b-83ce-ccfa7b7e7da8` guardado).
- `GET /api/labs/results/{id}` → **200**, estado de la notificación: `Pending`.
- Evento en el outbox: intentos de publicación fallidos **1**, procesado: `pendiente` (sigue pendiente, con espera creciente).
- Notificaciones generadas mientras el broker está caído: **0**.
- `docker compose start rabbitmq` → código 0.
- Tras volver el broker, el evento pendiente se publicó y se consumió: notificación generada **True**; intentos fallidos acumulados 3; evento procesado a las `18:40:23`.
- Estado final de la notificación: `Sent`; notificaciones del resultado: **1**.

## C. Reentrega del mismo evento: sin notificaciones duplicadas
- Se marcó de nuevo como pendiente el evento ya consumido (simula la caída entre "publicado" y "marcado") y el publicador lo volvió a publicar con el mismo `MessageId`.
- Evento republicado: **True**. Notificaciones del resultado después de la reentrega: **1** (debe ser 1).

## D. Autorización y validación
| Comprobación | Esperado | Obtenido | Resultado |
|---|---|---|---|
| Recepción registra un resultado | 403 | 403 | OK |
| Recepción lista resultados | 403 | 403 | OK |
| Recepción lee un resultado | 403 | 403 | OK |
| Médico sin relación lee el resultado | 404 | 404 | OK |
| Médico sin relación lista los del paciente | 404 | 404 | OK |
| Paciente intenta registrar un resultado | 403 | 403 | OK |
| Personal lee notificaciones de pacientes | 403 | 403 | OK |
| Sin sesión | 401 | 401 | OK |
| Resultado sin parámetros | 400 | 400 | OK |
| Fecha futura | 400 | 400 | OK |
| Paciente inexistente | 400 | 400 | OK |
| Modificar un resultado (PUT) | 405 | 405 | OK |
| Borrar un resultado (DELETE) | 405 | 405 | OK |

## Limpieza
- Se borraron los resultados, notificaciones y eventos de los pacientes `CYP-E2E-*` creados por esta ejecución. No se tocó ningún otro dato ni volumen.

**Resultado global: CORRECTO** (13/13 comprobaciones de la sección D).

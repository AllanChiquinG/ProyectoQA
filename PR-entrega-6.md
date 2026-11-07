## Entrega 6: laboratorio y notificaciones asíncronas

Un solo commit sobre `main` (que ya incluye las entregas 4 y 5). No hay app móvil, portal web del paciente ni cloud.

### Funciones implementadas
**Laboratorio**
- WEB-16 (resultados con estado de notificación), WEB-17 (registrar: 1–10 parámetros), WEB-18 (detalle, "Reintentar notificación" solo Laboratorio) y pestaña Laboratorio del expediente.
- API: `POST /api/labs/results`, listado, detalle, `labs/patients` (datos mínimos), `patients/{id}/lab-results`, `labs/results/me`, `POST .../notification/retry`. Resultados inmutables (PUT/PATCH/DELETE → 405).
- Autorización por rol y recurso: Laboratorio todos; Médico solo con relación válida de atención; Paciente solo los suyos; Recepción y Administrador nunca (403). Ajeno → 404 + `clinical.access_denied`. Auditoría y bitácoras sin tipo de examen, valores ni observaciones.

**Notificaciones asíncronas (RabbitMQ real)**
- Outbox transaccional: resultado y evento `LabResultReady` (solo ids) en la misma transacción de PostgreSQL.
- Publicador en segundo plano (confirmaciones, `FOR UPDATE SKIP LOCKED`, espera creciente); si el broker cae, el evento queda pendiente y se publica al volver.
- Consumidor con ack manual, idempotente (índices únicos por `Type+SourceId` y `EventId`), reintentos con espera y DLQ.
- API para la futura app móvil: `GET /api/notifications`, `unread-count`, `POST /{id}/read`.
- Migración `LaboratorioYNotificaciones`; `docker-compose.yml` con nombre de nodo fijo de RabbitMQ.

### Resultados
| Prueba | Resultado |
|---|---|
| xUnit unitarias | 44/44 |
| xUnit integración (PostgreSQL, Redis y RabbitMQ en Testcontainers) | 182/182 (43 laboratorio, 8 mensajería) |
| Cypress Chrome 155 | 26/26 (laboratorio 8, médico 10, regresión Recepción 8) |
| Cypress Electron 146 | 26/26 |

### Evidencias (`documentacion/evidencias/`)
`entrega-6-laboratorio.md` (generada con el RabbitMQ real de compose) y `entrega-6-pruebas.md`:
- **RabbitMQ detenido:** `POST` → 201, resultado legible, notificación `Pending`, intentos fallidos en el outbox.
- **Recuperación del outbox:** al volver el broker se publica, se consume y queda `Sent` con una sola notificación.
- **Duplicados:** evento republicado con el mismo `MessageId` y mensaje nuevo para el mismo resultado → 1 notificación.
- **Fallos del consumidor:** falla 2 veces y procesa; falla siempre → DLQ (solo tipo de error) y tras corregir, "reintentar" crea una sola notificación; mensaje ilegible/inexistente → DLQ directa.
- 13/13 comprobaciones de autorización y validación contra la API real.

### Ejecutar en Linux con Docker (Windows con Smart App Control)
Smart App Control bloquea los `.dll` sin firma recién compilados; no se desactivó. Los scripts usan contenedores del SDK oficial, sin tocar volúmenes ni redes de compose (solo crean el volumen de caché `medicore-nuget`).
```bash
docker compose up -d
scripts/dotnet-linux.sh test MediCore.slnx          # build + xUnit
scripts/dotnet-linux.sh ef-migration <Nombre>       # migraciones
scripts/dev-stack-linux.sh up                       # API (5011) y web (5298)
cd tests/cypress && CYPRESS_MEDICORE_PASSWORD=<Seed:DevPassword> npx cypress run --browser chrome
MEDICORE_PASSWORD=<Seed:DevPassword> python scripts/evidencia_laboratorio.py
scripts/dev-stack-linux.sh down
```
Configuración: `ConnectionStrings:RabbitMq` en user-secrets (ver README) y sección `Messaging`.

### Limitaciones y pendientes de verificar
- Nada se ejecutó de forma nativa en Windows (Smart App Control); repetir en un equipo sin el bloqueo.
- `dotnet format` en Linux no es concluyente (diferencias en archivos no tocados); formato no verificado.
- Sin rendimiento (k6/JMeter), accesibilidad ni móvil; Insomnia y Maestro pendientes; Cypress sin smoke general ni Administrador.
- "RabbitMQ detenido" no está en Cypress (no controla Docker); se cubre con `MessagingTests` y el script.
- No probado: varias instancias de la API publicando a la vez ni pérdida definitiva de la conexión más allá de detener/iniciar el broker.
- `docker-compose.yml` (nombre de nodo) no se aplicó al contenedor en ejecución; al recrearlo RabbitMQ empieza con colas vacías dentro del mismo volumen.
- Decisión revisable: Laboratorio ve todos los resultados (equipo compartido).
- Solo `LabResultReady`; el resto de eventos sigue en la entrega 7.
- La API se ejecutó en Development contra la base de desarrollo (migración solo aditiva, sin borrar datos).

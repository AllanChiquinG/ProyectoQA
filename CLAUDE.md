# MediCore

Plataforma hospitalaria (Proyecto 2 de `Docs/Proyecto Final QA.docx`, curso 048 Aseguramiento de la Calidad, UMG). Web para el personal, app Android para el paciente, API backend. Todo el producto en **español**.

## Reglas del proyecto

- **No modificar `Docs/` ni `Referencias/`.** Son entradas de solo lectura. (Windows no distingue mayúsculas: la documentación propia va en `documentacion/`, nunca en `docs/`.)
- **No inventar requisitos.** Alcance = Proyecto 2 + requisitos generales del documento. Ver `documentacion/01-alcance-y-pantallas.md`.
- **Fuera de alcance:** portal web del paciente, módulos no listados, microservicios.
- **Diseño:** la referencia es `Referencias/medicore/diseno/`. Apariencia final = `style.css` y luego `appearance.css` (copias sin modificar en `src/MediCore.Web/wwwroot/css/`; ajustes propios van en un tercer archivo cargado después). `app.js` define pantallas, ids (WEB-xx / MOB-xx), textos y recorridos.
- **Nunca copiar** la barra del prototipo (selector Web/Móvil y rol) ni sus datos/reglas en memoria. Acceso y navegación salen del usuario autenticado.
- Cambios de accesibilidad/adaptación permitidos sin alterar la identidad visual; explicarlos al hacerlos.
- **Sin secretos en el repo.** Solo datos ficticios. Configuración sensible por variables de entorno / user-secrets; `.env.example` con valores de ejemplo.
- Pendientes obligatorios de pruebas, siempre visibles en la matriz: ampliar Cypress (iniciado: Recepción y Médico), Insomnia, Maestro y k6/JMeter (las pruebas xUnit no los sustituyen). Registro de hallazgos y verificación manual: `documentacion/04-registro-de-hallazgos.md`.
- Una entrega pequeña a la vez; actualizar `documentacion/03-matriz-trazabilidad.md` en cada una. No declarar comprobaciones que no se ejecutaron.

## Decisiones técnicas acordadas

- .NET 10 / C#. Solución `MediCore.slnx`, SDK fijado en `global.json` (10.0.401, `latestFeature`).
- Monolito modular: un despliegue, módulos con límites claros (Identidad, Pacientes, Médicos, Agenda, Consultas, Recetas, Laboratorio, Notificaciones, Auditoría). Sin microservicios.
- Web del personal: ASP.NET Core Razor Pages (`src/MediCore.Web`). Backend: ASP.NET Core Web API con controladores (`src/MediCore.Api`), OpenAPI documentado.
- Móvil: .NET MAUI solo Android. `src/MediCore.Mobile.Core` (lógica sin MAUI: cliente de la API, sesión, ViewModels; en `MediCore.slnx` y probada con xUnit) y `src/MediCore.Mobile` (vistas XAML, almacenamiento seguro, Shell; **fuera** de `MediCore.slnx`; requiere workload `maui-android` + Android SDK). Ver `documentacion/05-app-movil.md`.
- Datos: EF Core + PostgreSQL. Cache/sesiones: Redis. Mensajería: RabbitMQ (productor, consumidor, reintentos, DLQ, idempotencia). Para que una caída del broker no pierda datos clínicos: guardar primero en BD y publicar con outbox transaccional.
- Agenda: la prevención de doble reserva se garantiza en la base de datos (restricción única + transacción/concurrencia optimista), no en memoria ni solo en cache.
- Infra: Azure + Terraform (ruta A), Ansible, Docker/Compose, GitHub, Jenkins.
- Autorización por rol **y** por recurso (un paciente solo ve lo suyo; un médico solo lo autorizado). Auditoría de accesos y cambios clínicos.
- Autenticación (entrega 2): la API emite JWT de 15 min + token de refresco de un solo uso que rota; la sesión vive en Redis (8 h absolutas) y cada petición verifica que siga existiendo (cerrar sesión o cambiar el rol la revoca; si Redis cae, falla cerrado). La web actúa como BFF: guarda los tokens en la cookie cifrada (HttpOnly) y llama a la API con ellos; renueva el acceso en `SessionTokens.ValidateAsync`. Reutilizar un refresh ya rotado revoca la sesión. Contraseñas con `PasswordHasher` de ASP.NET; bloqueo tras 5 fallos (15 min); límite por IP en login/refresh; mensaje único para todos los fallos de login.
- Seguro por defecto: la política fallback exige usuario autenticado en API y web; lo público se marca con `[AllowAnonymous]`. Roles en `MediCore.Contracts.Roles` (deben coincidir con `UserRole`). Auditoría en `audit_entries` vía `IAuditLogger` (solo códigos, sin datos personales ni clínicos en `Details`).
- Autorización por recurso (entrega 3): `PatientAuthorizationHandler` (IAuthorizationService, `PatientOperations.Read/Update`). Recepción y Administrador gestionan todo; el Paciente solo lee su propio registro (`Patient.UserId`); el Médico accede con una cita no cancelada o una consulta registrada; Laboratorio no accede. Un acceso ajeno devuelve 404 (no confirma existencia) y se audita como `patients.access_denied`. Toda lectura de datos de pacientes se audita (`patients.read`, `patients.searched`) sin guardar datos personales.
- Cache (entrega 3): `ICacheService` falla abierto (sin Redis consulta PostgreSQL); el catálogo de médicos se cachea 5 min (`Cache:DoctorCatalogTtl`) y se invalida al crear/editar. Las sesiones, en cambio, fallan cerrado.
- Unicidad de la identificación del paciente: índice único en PostgreSQL (maneja altas simultáneas); la API responde 409.
- Agenda (entrega 4): citas de 30 minutos con hora de reloj de la clínica (`timestamp without time zone`; `IClinicClock`, `Clinic:TimeZone`). PostgreSQL impide el solapamiento con dos `EXCLUDE USING gist` (médico y paciente, solo citas `Confirmed`) creadas por SQL en la migración `AgendaYCitas`. Como las exclusiones concurrentes pueden acabar en deadlock, la alta se serializa con `pg_advisory_xact_lock` por horario del médico y por paciente y se reintenta ante 40P01/40001; la restricción sigue siendo la garantía final. La cancelación es un `UPDATE` condicional (idempotente, sin pérdida de actualizaciones).
- Idempotencia de reservas: encabezado `Idempotency-Key` (índice único por usuario y clave); sin clave, repetir la misma reserva (mismo paciente, médico y hora) devuelve la cita existente. Otra clave con distinta solicitud → 409.
- Acceso a citas (`AppointmentAuthorizationHandler`): Recepción todas; Médico solo las de su agenda (lectura); Paciente solo las suyas (lectura y cancelación); Administrador y Laboratorio, ninguna. Relación médico–paciente (`PatientAccess.HasCareRelationship`, única fuente para listado y detalle): el médico lee pacientes con quienes tiene una cita `Confirmed` (pasada o futura) **o una consulta registrada**; cancelar una cita solo retira el acceso si no queda otra relación válida. Accesos ajenos → 404 + `appointments.access_denied`/`patients.access_denied`.
- Cache de disponibilidad: `AvailabilityService` guarda por médico y fecha los horarios y ocupados; lo que depende de la hora actual (horarios ya pasados) se calcula al responder. Reservar o cancelar invalida el día; cambiar el horario del médico invalida todas sus fechas (`RemoveByPrefixAsync`). Las citas ya reservadas no se cancelan al cambiar el horario.
- Expediente (entrega 5): `Consultation` (una por cita, índice único en `AppointmentId`; paciente, médico y fecha salen de la cita; solo citas `Confirmed` de hoy o anteriores) y `Prescription` con `PrescriptionItem` (1 a 10 medicamentos con dosis, frecuencia, duración e indicaciones; no se editan: una corrección es una receta nueva). `ClinicalController`.
- **Expediente COMPARTIDO entre médicos (decisión vigente, reemplaza la de "solo lo propio" de la primera versión de la entrega 5):** un médico con relación de atención válida con el paciente (cita `Confirmed` o consulta registrada, `ICareRelationship`/`PatientAccess.HasCareRelationship`) **lee** las consultas y recetas del paciente escritas por **cualquier** médico; los DTO traen `IsOwn` y `DoctorName`. **Nadie modifica registros de otro profesional**: no existe operación de edición ni de borrado de consultas ni recetas (PUT/PATCH/DELETE → 405, tampoco para el autor); solo el autor crea recetas sobre su consulta (si no, 404 + `clinical.access_denied`), y solo se registra la consulta de una cita propia. Sin relación válida → 404 y auditoría; al cancelarse la última relación se pierde la lectura (el autor de una consulta conserva el acceso por ella). La lectura de un registro de otro autor se audita con `Details = other_author` (y los listados con `count=n;others=m`), nunca con contenido.
- El paciente lee únicamente sus recetas, de cualquier médico (`PrescriptionAuthorizationHandler`: autor, médico con relación o paciente dueño; Recepción, Administrador y Laboratorio, nunca). Accesos ajenos → 404 + `clinical.access_denied`.
- Contenido clínico (motivo, notas, diagnóstico, medicamentos, indicaciones): nunca va a `audit_entries`, a `Details` ni a las bitácoras; la auditoría guarda actor, recurso, acción y conteos (`count=n`, `items=n`). EF no registra valores de parámetros (no activar `EnableSensitiveDataLogging`). Hay una prueba que lo verifica.
- Web: páginas de error vacías se muestran con `/Estado` (conserva el código HTTP). Los formularios con manejadores (`asp-page-handler`) llevan `action` explícito y `asp-antiforgery="true"`.
- Laboratorio (entrega 6): `LabResult` con 1–10 parámetros (nombre, valor; unidad y rango opcionales); no se editan ni se borran (corrección = resultado nuevo; PUT/PATCH/DELETE → 405). `LabsController`: `POST /api/labs/results`, listado/detalle, `labs/patients` (búsqueda mínima de pacientes para Laboratorio), `GET /api/patients/{id}/lab-results`, `GET /api/labs/results/me` y `POST /api/labs/results/{id}/notification/retry`. Autorización (`LabResultAuthorizationHandler`): **Laboratorio** registra y lee todos los resultados (equipo compartido; decisión revisable hacia "solo los propios"); el **Médico** solo con relación válida de atención (`ICareRelationship`); el **Paciente** solo los suyos (`Patient.UserId`); **Recepción y Administrador nunca**. Acceso ajeno → 404 + `clinical.access_denied`; rol sin permiso → 403. Auditoría `labs.*` sin tipo de examen, valores ni observaciones.
- Mensajería (entrega 6, RabbitMQ real): **outbox transaccional**: el resultado y el evento `LabResultReady` (solo identificadores) se guardan en la misma transacción de PostgreSQL. `OutboxPublisherService` (segundo plano) lee los pendientes con `FOR UPDATE SKIP LOCKED`, publica con confirmación del broker y recién entonces marca `ProcessedAtUtc`; si el broker falla, el evento sigue pendiente con espera creciente (`NextAttemptAtUtc`, tope `Messaging:PublishMaxBackoff`) y se publica al volver el broker. Entrega al menos una vez; el consumidor es idempotente. Topología: exchange `medicore.events` (topic) → cola `medicore.notifications`; fallos → `medicore.notifications.retry` (espera por mensaje, vuelve a la cola de trabajo) → tras `Messaging:ConsumerMaxAttempts` a `medicore.notifications.dlq`; mensajes ilegibles o de resultados inexistentes van directo a la DLQ. `NotificationConsumerService` confirma (ack) solo tras guardar la `Notification`; unicidad en BD por (`Type`,`SourceId`) y por `EventId` (un mensaje reentregado no duplica). La conexión es `ConnectionStrings:RabbitMq` (user-secrets / variable de entorno); `Messaging:Enabled=false` apaga publicador y consumidor (lo usan las pruebas sin broker). La conexión con el broker se recupera sola (no se reemplaza mientras se recupera, para no dejar al consumidor sin canal). Bitácoras: solo id de mensaje, tipo de evento, intento y tipo de error.
- App móvil (entrega 7): usa solo la API real; sesión en `SecureStorage` (nunca la contraseña); renueva el acceso con candado (el refresco es de un solo uso); la reserva usa `Idempotency-Key` reutilizado en los reintentos y un comando que ignora pulsaciones repetidas; un 409 conserva médico y fecha. La API sigue siendo la que autoriza (el personal que inicia sesión en la app es descartado y su token revocado). HTTP solo hacia 10.0.2.2/localhost (`network_security_config.xml`). APK de desarrollo: `scripts/android-build-linux.sh` (contenedor Linux; compila pero no prueba en Android). Flujos Maestro en `tests/maestro` (8) con datos del paciente aislado `e2e.paciente` (CYP-E2E-004); `maestro_run.py` no escribe evidencia si no pudo ejecutarlos.
- Notificaciones del paciente (API para la app móvil, MOB-12): `GET /api/notifications` (`unreadOnly`, paginado), `GET /api/notifications/unread-count`, `POST /api/notifications/{id}/read` (idempotente). Solo el rol Paciente y solo las suyas (otra → 404). No hay portal web del paciente.

## Estructura

```
MediCore.slnx          solución (abrir en Visual Studio)
src/MediCore.Api       Web API
src/MediCore.Contracts  DTO y nombres de rol compartidos (API, Web, móvil)
src/MediCore.Mobile.Core  lógica de la app móvil (sin MAUI)
src/MediCore.Mobile    app Android (.NET MAUI), fuera de MediCore.slnx
tests/MediCore.Mobile.Tests  pruebas de la lógica móvil
tests/maestro          flujos Maestro de la app
src/MediCore.Domain    entidades por módulo, sin dependencias externas
src/MediCore.Infrastructure  EF Core/Npgsql, migraciones (Persistence/Migrations), Redis (sesiones, cache), agenda; futuro RabbitMQ
docker-compose.yml     Postgres 17 (5433), Redis 7, RabbitMQ 4; credenciales desde .env
src/MediCore.Web       Razor Pages (personal)
tests/MediCore.Api.Tests  integración: WebApplicationFactory + Testcontainers (Postgres, Redis y, en `MessagingTests`, RabbitMQ), entorno "Testing" (no lee user-secrets)
scripts/               ejecución en contenedor Linux (dotnet-linux.sh, dev-stack-linux.sh) y evidencia de laboratorio (evidencia_laboratorio.py)
tests/MediCore.UnitTests  reglas de dominio
tests/cypress          pruebas end-to-end de la web (Cypress)
documentacion/         alcance, plan, trazabilidad (propios)
Docs/, Referencias/    entradas, no tocar
```

Puertos de desarrollo: API `http://localhost:5011` (https 7293), Web `http://localhost:5298` (https 7216).

## Comandos

```bash
dotnet build MediCore.slnx
dotnet test MediCore.slnx
dotnet run --project src/MediCore.Api --launch-profile http
dotnet run --project src/MediCore.Web --launch-profile http
dotnet format MediCore.slnx --verify-no-changes
```

Base de datos (ver README para variables):

```bash
docker compose up -d
dotnet ef migrations add <Nombre> --project src/MediCore.Infrastructure --startup-project src/MediCore.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/MediCore.Infrastructure --startup-project src/MediCore.Api   # requiere ConnectionStrings__Postgres en el entorno
```

Secretos en user-secrets de la API (`ConnectionStrings:Postgres`, `ConnectionStrings:Redis` con 127.0.0.1, `Jwt:SigningKey`, `Seed:DevPassword`); nunca en appsettings. En Development la API migra y crea usuarios ficticios al arrancar (`DevelopmentBootstrap`). `dotnet ef` usa `DesignTimeDbContextFactory` (variable de entorno). Flujo Git: ramas `feature/*`, PR hacia `main`; `main` debe protegerse en GitHub.

Advertencias tratadas como errores (`Directory.Build.props`).

**Equipos Windows con Smart App Control activado** (bloquea cargar los `.dll` sin firma recién compilados: migraciones, pruebas, API; no se desactiva): ejecutar en un contenedor Linux con el SDK oficial, sin tocar los volúmenes de compose:

```bash
scripts/dotnet-linux.sh test MediCore.slnx                 # build + todas las pruebas (usa el socket de Docker para Testcontainers)
scripts/dotnet-linux.sh ef-migration <Nombre>              # genera la migración y la copia al repositorio
scripts/dev-stack-linux.sh up                              # API (5011) y web (5298) en contenedores, para Cypress; "down" los elimina
```

Cypress: `cd tests/cypress && CYPRESS_MEDICORE_PASSWORD=<Seed:DevPassword> npx cypress run --browser chrome` (requiere API y web en marcha; ver `tests/cypress/README.md`). Los archivos estáticos de la web (`wwwroot`) se publican con huella al compilar: tras cambiar un `.js` o `.css` hay que recompilar y reiniciar la web.

La web usa `wwwroot/css/app.css` (tercer archivo, después de style.css y appearance.css) para ajustes propios; CSP estricta: sin estilos ni scripts en línea.

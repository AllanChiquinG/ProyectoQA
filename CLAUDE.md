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
- Una entrega pequeña a la vez; actualizar `documentacion/03-matriz-trazabilidad.md` en cada una. No declarar comprobaciones que no se ejecutaron.

## Decisiones técnicas acordadas

- .NET 10 / C#. Solución `MediCore.slnx`, SDK fijado en `global.json` (10.0.401, `latestFeature`).
- Monolito modular: un despliegue, módulos con límites claros (Identidad, Pacientes, Médicos, Agenda, Consultas, Recetas, Laboratorio, Notificaciones, Auditoría). Sin microservicios.
- Web del personal: ASP.NET Core Razor Pages (`src/MediCore.Web`). Backend: ASP.NET Core Web API con controladores (`src/MediCore.Api`), OpenAPI documentado.
- Móvil: .NET MAUI solo Android (`src/MediCore.Mobile`, aún no creado; requiere workload `maui-android` + Android SDK).
- Datos: EF Core + PostgreSQL. Cache/sesiones: Redis. Mensajería: RabbitMQ (productor, consumidor, reintentos, DLQ, idempotencia). Para que una caída del broker no pierda datos clínicos: guardar primero en BD y publicar con outbox transaccional.
- Agenda: la prevención de doble reserva se garantiza en la base de datos (restricción única + transacción/concurrencia optimista), no en memoria ni solo en cache.
- Infra: Azure + Terraform (ruta A), Ansible, Docker/Compose, GitHub, Jenkins.
- Autorización por rol **y** por recurso (un paciente solo ve lo suyo; un médico solo lo autorizado). Auditoría de accesos y cambios clínicos.
- Autenticación (entrega 2): la API emite JWT de 15 min + token de refresco de un solo uso que rota; la sesión vive en Redis (8 h absolutas) y cada petición verifica que siga existiendo (cerrar sesión o cambiar el rol la revoca; si Redis cae, falla cerrado). La web actúa como BFF: guarda los tokens en la cookie cifrada (HttpOnly) y llama a la API con ellos; renueva el acceso en `SessionTokens.ValidateAsync`. Reutilizar un refresh ya rotado revoca la sesión. Contraseñas con `PasswordHasher` de ASP.NET; bloqueo tras 5 fallos (15 min); límite por IP en login/refresh; mensaje único para todos los fallos de login.
- Seguro por defecto: la política fallback exige usuario autenticado en API y web; lo público se marca con `[AllowAnonymous]`. Roles en `MediCore.Contracts.Roles` (deben coincidir con `UserRole`). Auditoría en `audit_entries` vía `IAuditLogger` (solo códigos, sin datos personales ni clínicos en `Details`).
- Autorización por recurso (entrega 3): `PatientAuthorizationHandler` (IAuthorizationService, `PatientOperations.Read/Update`). Recepción y Administrador gestionan todo; el Paciente solo lee su propio registro (`Patient.UserId`); el Médico NO accede hasta que exista relación de atención (se añade con las citas, entrega 4); Laboratorio no accede. Un acceso ajeno devuelve 404 (no confirma existencia) y se audita como `patients.access_denied`. Toda lectura de datos de pacientes se audita (`patients.read`, `patients.searched`) sin guardar datos personales.
- Cache (entrega 3): `ICacheService` falla abierto (sin Redis consulta PostgreSQL); el catálogo de médicos se cachea 5 min (`Cache:DoctorCatalogTtl`) y se invalida al crear/editar. Las sesiones, en cambio, fallan cerrado.
- Unicidad de la identificación del paciente: índice único en PostgreSQL (maneja altas simultáneas); la API responde 409.
- Agenda (entrega 4): citas de 30 minutos con hora de reloj de la clínica (`timestamp without time zone`; `IClinicClock`, `Clinic:TimeZone`). PostgreSQL impide el solapamiento con dos `EXCLUDE USING gist` (médico y paciente, solo citas `Confirmed`) creadas por SQL en la migración `AgendaYCitas`. Como las exclusiones concurrentes pueden acabar en deadlock, la alta se serializa con `pg_advisory_xact_lock` por horario del médico y por paciente y se reintenta ante 40P01/40001; la restricción sigue siendo la garantía final. La cancelación es un `UPDATE` condicional (idempotente, sin pérdida de actualizaciones).
- Idempotencia de reservas: encabezado `Idempotency-Key` (índice único por usuario y clave); sin clave, repetir la misma reserva (mismo paciente, médico y hora) devuelve la cita existente. Otra clave con distinta solicitud → 409.
- Acceso a citas (`AppointmentAuthorizationHandler`): Recepción todas; Médico solo las de su agenda (lectura); Paciente solo las suyas (lectura y cancelación); Administrador y Laboratorio, ninguna. Relación médico–paciente: el médico lee pacientes con quienes tiene una cita `Confirmed` (`PatientAccess.HasCareRelationship`); cancelarla retira el acceso. Accesos ajenos → 404 + `appointments.access_denied`/`patients.access_denied`.
- Cache de disponibilidad: `AvailabilityService` guarda por médico y fecha los horarios y ocupados; lo que depende de la hora actual (horarios ya pasados) se calcula al responder. Reservar o cancelar invalida el día; cambiar el horario del médico invalida todas sus fechas (`RemoveByPrefixAsync`). Las citas ya reservadas no se cancelan al cambiar el horario.
- Pendiente: autorización por recurso de consultas, recetas y resultados (entregas 5–6).

## Estructura

```
MediCore.slnx          solución (abrir en Visual Studio)
src/MediCore.Api       Web API
src/MediCore.Contracts  DTO y nombres de rol compartidos (API, Web, móvil)
src/MediCore.Domain    entidades por módulo, sin dependencias externas
src/MediCore.Infrastructure  EF Core/Npgsql, migraciones (Persistence/Migrations), Redis (sesiones, cache), agenda; futuro RabbitMQ
docker-compose.yml     Postgres 17 (5433), Redis 7, RabbitMQ 4; credenciales desde .env
src/MediCore.Web       Razor Pages (personal)
tests/MediCore.Api.Tests  integración: WebApplicationFactory + Testcontainers (Postgres, Redis), entorno "Testing" (no lee user-secrets)
tests/MediCore.UnitTests  reglas de dominio
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

La web usa `wwwroot/css/app.css` (tercer archivo, después de style.css y appearance.css) para ajustes propios; CSP estricta: sin estilos ni scripts en línea.

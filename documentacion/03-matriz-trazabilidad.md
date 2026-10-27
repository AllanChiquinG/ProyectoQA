# MediCore · Matriz inicial de trazabilidad

Estados: ✔ hecho y verificado · ◐ parcial · ○ pendiente. "Implementación" y "Pruebas" apuntan a rutas reales cuando existan; hoy solo hay lo de la entrega 0.

## Entregables obligatorios PENDIENTES (las pruebas actuales no los sustituyen)

Las 155 pruebas xUnit actuales (28 unitarias y 127 de integración contra PostgreSQL y Redis reales; ninguna omitida) son complementarias. El documento del proyecto exige, además, los siguientes entregables, **todavía no iniciados**:

| Entregable (documento, secciones 4, 8 y 12) | ID | Qué exige | Estado | Entrega |
|---|---|---|---|---|
| **Cypress** (web) | GEN-10 | Smoke, flujo funcional crítico, regresión y al menos un escenario negativo | ◐ **INICIADO** — `tests/cypress`: 2 specs y 15 pruebas (Recepción: 8; Médico: 7), 15/15 en Chrome 155 y Electron 146. Incluye varios negativos (conflicto de horario, consulta incompleta, paciente sin relación). **Faltan**: smoke general, flujos de Administrador y Laboratorio, regresión formal y más negativos | 10 |
| **Insomnia** (API) | GEN-17 | Colección con pruebas positivas, negativas, de autenticación, autorización y errores | ○ **PENDIENTE** — no existe la colección | 10 |
| **Maestro** (móvil) | GEN-11 | Entre 5 y 8 flujos de la app, con al menos un escenario negativo | ○ **PENDIENTE** — la app móvil aún no existe | 9–10 |
| **Rendimiento con k6 o JMeter** | GEN-12, CRI-01, CRI-02 | Load, Stress y Spike con umbrales declarados antes de ejecutar (p95, tasa de error, throughput) y el escenario de alta demanda de citas con lecturas de agenda | ○ **PENDIENTE** — no hay scripts ni resultados | 13 |

Cypress ya existe para el recorrido de Recepción (WEB-01 a WEB-04) y el de Médico (WEB-11 a WEB-15), pero **no cubre** el smoke general, a Administrador ni a Laboratorio, ni una regresión formal.

Lo que sí existe hoy y se reutilizará: pruebas de concurrencia por API (12 solicitudes al mismo horario, 24 pacientes por 4 horarios con lecturas simultáneas), que sirven como base del escenario pero **no cuentan como prueba de rendimiento** (no miden p95 ni throughput y no usan umbrales previos). La verificación manual en navegador está descrita en `04-registro-de-hallazgos.md` y tampoco sustituye a Cypress.

## Funcionales

| ID | Requisito (fuente) | Pantallas | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|---|
| RF-01 | Gestión de pacientes y médicos (P2) | WEB-05…08 | `PatientsController`, `DoctorsController`, `Pages/Pacientes`, `Pages/Medicos` | `PatientsApiTests`, `DoctorsApiTests`, `PatientRulesTests`; Cypress pendiente | 3 | ◐ |
| RF-02 | Agenda y reserva de citas (P2) | WEB-02, 03, 04, 09, 11; MOB-03…07 (API lista) | `AppointmentsController`, `DoctorSchedulesController`, `Pages/Agenda`, `Pages/MiAgenda`, `Pages/Medicos/Disponibilidad` | `AppointmentsApiTests`, `AvailabilityApiTests`, `SchedulingRulesTests`; Cypress: recorrido de Recepción (`recepcion-reserva.cy.js`, 8 pruebas); Maestro pendiente | 4 | ◐ |
| RF-03 | Consultas médicas (P2) | WEB-12, 13 | `ClinicalController` (consultas), `Pages/Pacientes/Expediente`, `RegistrarConsulta`; entidad `Consultation` (una por cita, con paciente, médico y cita) | `ClinicalApiTests`, `ResourceAuthorizationTests`; Cypress `medico-expediente.cy.js` | 5 | ◐ (la lectura por el paciente no está en el diseño) |
| RF-04 | Recetas (P2) | WEB-14, 15; MOB-10, 11 (API lista) | `ClinicalController` (recetas con 1 a 10 medicamentos), `Pages/Pacientes/CrearReceta`, `Receta`; `GET /api/prescriptions/me` para el móvil | `ClinicalApiTests`, `ResourceAuthorizationTests`; Cypress `medico-expediente.cy.js`; Maestro pendiente | 5, 9 | ◐ |
| RF-05 | Registro/consulta de resultados de laboratorio (P2) | WEB-16…18; MOB-08, 09 | — | Unit; integración; API; Maestro | 6 | ○ |
| RF-06 | Notificaciones al paciente (P2) | WEB-18; MOB-12 | — | Integración con RabbitMQ; Maestro | 7 | ○ |
| RF-07 | Aplicación móvil para pacientes (P2) | MOB-01…12 | — | Maestro 5–8 flujos | 9 | ○ |
| RF-08 | Usuarios y roles (diseño) | WEB-10 | `UsersController`, `Pages/Usuarios.cshtml` | `IdentityApiTests` (RBAC, cambio de rol); Cypress pendiente | 2 | ◐ |

## API de referencia

| ID | Endpoint | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| API-00 | `GET /api/health` y `/api/health/ready` (técnico) | `src/MediCore.Api/Controllers/HealthController.cs` | `BasicEndpointTests`, `PostgresIntegrationTests` (API↔PostgreSQL) | 0–1 | ✔ |
| API-A1 | `POST /api/auth/login`, `/refresh`, `/logout`, `GET /api/auth/me` (base de MOB-01) | `AuthController` | `IdentityApiTests` | 2 | ✔ |
| API-A2 | `GET /api/users`, `PUT /api/users/{id}/role`, `GET /api/audit` (administrador) | `UsersController`, `AuditController` | `IdentityApiTests` | 2 | ✔ |
| API-01 | `GET /api/doctors` (+ `GET/POST/PUT /api/doctors/{id}`, administrador) | `DoctorsController` | `DoctorsApiTests`; Insomnia pendiente | 3 | ✔ |
| API-02 | `GET /api/doctors/{id}/availability?date=` (+ `GET/PUT /api/doctors/{id}/schedule`) | `DoctorSchedulesController`, `AvailabilityService` | `AvailabilityApiTests`; Insomnia pendiente | 4 | ✔ |
| API-03 | `POST /api/appointments` (+ `GET` agenda, `GET /{id}`, `GET /me`, `POST /{id}/cancel`) con `Idempotency-Key` | `AppointmentsController` | `AppointmentsApiTests` (concurrencia, idempotencia, cancelación, autorización, datos inválidos); Insomnia pendiente | 4 | ✔ |
| API-04 | `GET /api/patients/{id}` (+ listado/búsqueda, `me`, alta y edición) | `PatientsController`, `PatientAuthorizationHandler` | `PatientsApiTests` (IDOR, roles, duplicados, concurrencia); Insomnia pendiente | 3 | ✔ |
| API-05 | `POST /api/labs/results` | — | Insomnia; caída de RabbitMQ | 6 | ○ |
| API-06 | `GET /api/prescriptions/{id}` (+ crear, listar por paciente y `me`) y consultas | `ClinicalController`, `PrescriptionAuthorizationHandler` | `ClinicalApiTests` (autor y paciente dueño; terceros 404; roles 403); Insomnia pendiente | 5 | ✔ |

## Redis y RabbitMQ

| ID | Requisito | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| RED-01 | Cache de disponibilidad médica (TTL, invalidación al reservar/cancelar o cambiar el horario) | `Infrastructure/Scheduling/AvailabilityService.cs`, `CacheKeys.Availability`, `RemoveByPrefixAsync` | `AvailabilityApiTests`: MISS, HIT, TTL, invalidación, prefijo por médico | 4 | ✔ |
| RED-02 | Cache de catálogos y consultas frecuentes | Catálogo de médicos: `Infrastructure/Caching/CacheService.cs`, TTL 5 min, invalida al crear/editar | `DoctorsApiTests`: MISS, HIT, TTL, invalidación | 3–4 | ◐ (falta disponibilidad: entrega 4) |
| RED-03 | Sesiones en Redis | `Infrastructure/Identity/SessionStore.cs` | `IdentityApiTests`: logout, refresh, reutilización, Redis caído | 2 | ✔ |
| RED-04 | Recuperación si Redis no está disponible (degradar a BD) | El cache falla abierto y consulta PostgreSQL (catálogo y disponibilidad); las sesiones fallan cerrado (RED-03) | `DoctorsApiTests` y `AvailabilityApiTests` (consulta y reserva con Redis inalcanzable), `IdentityApiTests` | 3–4 | ✔ |
| MQ-01 | Eventos AppointmentCreated / AppointmentCancelled | — | Integración API↔RabbitMQ | 7 | ○ |
| MQ-02 | LabResultReady, PrescriptionCreated, PatientNotification | — | Integración | 7 | ○ |
| MQ-03 | Consumidor con reintentos, DLQ e idempotencia | — | Integración consumidor↔RabbitMQ; mensaje duplicado/envenenado | 7 | ○ |
| MQ-04 | Caída del broker no pierde datos clínicos (outbox) | Tabla `outbox_messages` (`src/MediCore.Domain/Messaging/OutboxMessage.cs`, migración `Inicial`); falta el publicador | Escenario crítico: detener RabbitMQ tras registrar resultado | 6–7 | ○ |

## Escenario crítico y seguridad

| ID | Requisito | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| CRI-01 | Sin doble reserva ni colisión de horario bajo alta demanda | Restricciones de exclusión en PostgreSQL (médico y paciente) + candados de asesoría por horario + reintento ante deadlock | `AppointmentsApiTests`: 12 solicitudes al mismo horario → 1 reserva; 24 pacientes por 4 horarios + lecturas; mismo paciente con 6 médicos; reenvíos simultáneos con la misma clave. Carga con k6 pendiente (entrega 13) | 4, 13 | ◐ |
| CRI-02 | Consistencia con lecturas concurrentes de agenda/expediente | — | k6 mixto | 13 | ○ |
| CRI-03 | RabbitMQ interrumpido después de registrar un resultado | — | Integración | 7 | ○ |
| SEC-01 | RBAC por rol clínico/administrativo | `[Authorize(Roles)]`, política fallback, `Contracts/Roles.cs` | `IdentityApiTests.Solo_el_administrador…`, `BasicEndpointTests` | 2 | ◐ (roles clínicos con sus módulos) |
| SEC-02 | Autorización a nivel de recurso (IDOR) | Pacientes, citas, recetas y consultas: `PatientAuthorizationHandler`, `AppointmentAuthorizationHandler`, `PrescriptionAuthorizationHandler`; el médico ve solo lo que él escribió; 404 y auditoría ante intento ajeno | `PatientsApiTests`, `AppointmentsApiTests`, `ClinicalApiTests`, `ResourceAuthorizationTests` (unitarias, sin BD) | 3–6 | ◐ (falta laboratorio: entrega 6) |
| SEC-03 | Exposición de datos sensibles (respuestas, logs, errores) | Información clínica solo para quien corresponde; la auditoría y las bitácoras no contienen motivo, notas, diagnóstico ni medicamentos; el paciente no ve contacto de médicos; ProblemDetails | `ClinicalApiTests.Ni_la_auditoria_ni_las_bitacoras_contienen_el_contenido_clinico` (con control del capturador), `DoctorsApiTests`; revisión ZAP pendiente | 3–6 | ◐ |
| SEC-04 | Sesiones y autenticación (expiración, bloqueo, logout) | `AuthController`, JWT, `User` (bloqueo) | `IdentityApiTests` (expiración, manipulación, bloqueo, logout), `UserTests`; Cypress negativo pendiente | 2 | ✔ |
| SEC-05 | Seguridad de API (validación, rate limit, CORS, cabeceras) | ProblemDetails, validación de entrada, rate limit en login/refresh, CSP y cabeceras en la web | `IdentityApiTests.Login_se_limita_por_ip`; Insomnia y ZAP pendientes | 1–2 | ◐ |
| SEC-06 | Trazabilidad y auditoría de accesos y cambios clínicos | `audit_entries`, `IAuditLogger`, `GET /api/audit` | `IdentityApiTests`; pacientes y médicos (`PatientsApiTests`); citas: creadas, reenviadas, conflictos, canceladas, leídas, agenda consultada, accesos denegados, horario cambiado (`AppointmentsApiTests`, `AvailabilityApiTests`) | 2–6 | ◐ |
| SEC-07 | Secretos fuera del repositorio | `.gitignore`, `.env.example` | Revisión + escaneo en pipeline | 0 | ◐ |

## Generales / DevSecOps / documentos

| ID | Requisito | Implementación | Entrega | Estado |
|---|---|---|---|---|
| GEN-01 | Repo GitHub: ramas, PR, Issues, Releases, protección de `main` | Remoto en GitHub, ramas `feature/*` y PR #1 fusionado; falta protección de `main`, Issues y Releases | 0, 11 | ◐ |
| GEN-02 | Jenkinsfile con las 18 etapas + bloqueo por Quality Gate | — | 11 | ○ |
| GEN-03 | Dockerfiles + docker-compose | `docker-compose.yml` (Postgres, Redis, RabbitMQ; faltan Dockerfiles de API/Web) | 1, 11 | ◐ |
| GEN-04 | Azure + Terraform (aprovisionar, modificar, destruir) | — | 12 | ○ |
| GEN-05 | Ansible (SO, Docker, reverse proxy) | — | 12 | ○ |
| GEN-06 | API REST documentada (OpenAPI) | `AddOpenApi` + `/openapi/v1.json` en Development (sin esquema Bearer ni comentarios aún) | 1 | ◐ |
| GEN-07 | SonarQube + Quality Gate | — | 11 | ○ |
| GEN-08 | Fortify, Dependency-Check, npm audit | — | 11 | ○ |
| GEN-09 | OWASP ZAP sobre QA | — | 11 | ○ |
| GEN-10 | Cypress (smoke, crítico, regresión, negativo) | `tests/cypress/e2e/recepcion-reserva.cy.js` y `medico-expediente.cy.js` (ver pendientes arriba) | 10 | ◐ |
| GEN-11 | Maestro (5–8 flujos) | — | 10 | ○ |
| GEN-12 | k6/JMeter: Load, Stress, Spike con umbrales previos | — | 13 | ○ |
| GEN-13 | Usabilidad (≥5 participantes) + Lighthouse/axe | — | 13 | ○ |
| GEN-14 | Registro de defectos con evidencia | — | continuo | ○ |
| GEN-15 | Plan de pruebas (20 secciones), riesgos C/I/D, casos, informe GO / NO-GO | — | 14 | ○ |
| GEN-16 | Ambiente reproducible desde código y documentación | README + compose + migraciones | 1, 12 | ◐ |
| GEN-17 | Insomnia: colección +/−/authz | — | 10 | ○ |

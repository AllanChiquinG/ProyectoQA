# MediCore · Matriz inicial de trazabilidad

Estados: ✔ hecho y verificado · ◐ parcial · ○ pendiente. "Implementación" y "Pruebas" apuntan a rutas reales cuando existan; hoy solo hay lo de la entrega 0.

## Funcionales

| ID | Requisito (fuente) | Pantallas | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|---|
| RF-01 | Gestión de pacientes y médicos (P2) | WEB-05…08 | — | Unit reglas; API CRUD; Cypress | 3 | ○ |
| RF-02 | Agenda y reserva de citas (P2) | WEB-02…04, 11; MOB-03…07 | — | Unit; concurrencia; API; Cypress; Maestro | 4 | ○ |
| RF-03 | Consultas médicas (P2) | WEB-12, 13 | — | Unit; API; Cypress | 5 | ○ |
| RF-04 | Recetas (P2) | WEB-14, 15; MOB-10, 11 | — | Unit; API; Maestro | 5 | ○ |
| RF-05 | Registro/consulta de resultados de laboratorio (P2) | WEB-16…18; MOB-08, 09 | — | Unit; integración; API; Maestro | 6 | ○ |
| RF-06 | Notificaciones al paciente (P2) | WEB-18; MOB-12 | — | Integración con RabbitMQ; Maestro | 7 | ○ |
| RF-07 | Aplicación móvil para pacientes (P2) | MOB-01…12 | — | Maestro 5–8 flujos | 9 | ○ |
| RF-08 | Usuarios y roles (diseño) | WEB-10 | — | API autorización; Cypress | 2 | ○ |

## API de referencia

| ID | Endpoint | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| API-00 | `GET /api/health` y `/api/health/ready` (técnico) | `src/MediCore.Api/Controllers/HealthController.cs` | `HealthEndpointTests`, `PostgresIntegrationTests` (API↔PostgreSQL), `ErrorHandlingTests` | 0–1 | ✔ |
| API-01 | `GET /api/doctors` | — | Insomnia +/−/authz | 3 | ○ |
| API-02 | `GET /api/doctors/{id}/availability` | — | Insomnia; cache HIT/MISS/TTL | 4 | ○ |
| API-03 | `POST /api/appointments` | — | Insomnia; concurrencia doble reserva | 4 | ○ |
| API-04 | `GET /api/patients/{id}` | — | Insomnia; IDOR | 3 | ○ |
| API-05 | `POST /api/labs/results` | — | Insomnia; caída de RabbitMQ | 6 | ○ |
| API-06 | `GET /api/prescriptions/{id}` | — | Insomnia; IDOR | 5 | ○ |

## Redis y RabbitMQ

| ID | Requisito | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| RED-01 | Cache de disponibilidad médica (TTL, invalidación al reservar/cancelar) | — | Integración API↔Redis: HIT, MISS, TTL, invalidación | 4 | ○ |
| RED-02 | Cache de catálogos y consultas frecuentes | — | Integración | 3–4 | ○ |
| RED-03 | Sesiones en Redis | — | Integración; expiración | 2 | ○ |
| RED-04 | Recuperación si Redis no está disponible (degradar a BD) | — | Integración deteniendo Redis | 4 | ○ |
| MQ-01 | Eventos AppointmentCreated / AppointmentCancelled | — | Integración API↔RabbitMQ | 7 | ○ |
| MQ-02 | LabResultReady, PrescriptionCreated, PatientNotification | — | Integración | 7 | ○ |
| MQ-03 | Consumidor con reintentos, DLQ e idempotencia | — | Integración consumidor↔RabbitMQ; mensaje duplicado/envenenado | 7 | ○ |
| MQ-04 | Caída del broker no pierde datos clínicos (outbox) | Tabla `outbox_messages` (`src/MediCore.Domain/Messaging/OutboxMessage.cs`, migración `Inicial`); falta el publicador | Escenario crítico: detener RabbitMQ tras registrar resultado | 6–7 | ○ |

## Escenario crítico y seguridad

| ID | Requisito | Implementación | Pruebas previstas | Entrega | Estado |
|---|---|---|---|---|---|
| CRI-01 | Sin doble reserva ni colisión de horario bajo alta demanda | — | Prueba de concurrencia (N solicitudes simultáneas al mismo horario); k6 | 4, 13 | ○ |
| CRI-02 | Consistencia con lecturas concurrentes de agenda/expediente | — | k6 mixto | 13 | ○ |
| CRI-03 | RabbitMQ interrumpido después de registrar un resultado | — | Integración | 7 | ○ |
| SEC-01 | RBAC por rol clínico/administrativo | — | Pruebas de autorización por rol | 2 | ○ |
| SEC-02 | Autorización a nivel de recurso (IDOR) | — | Paciente A no lee datos de B; médico no autorizado | 3–6 | ○ |
| SEC-03 | Exposición de datos sensibles (respuestas, logs, errores) | — | API; ZAP; revisión de logs | 3–6 | ○ |
| SEC-04 | Sesiones y autenticación (expiración, bloqueo, logout) | — | API; Cypress negativo | 2 | ○ |
| SEC-05 | Seguridad de API (validación, rate limit, CORS, cabeceras) | ProblemDetails sin detalles internos (parcial) | Insomnia; ZAP | 1–2 | ◐ |
| SEC-06 | Trazabilidad y auditoría de accesos y cambios clínicos | — | Integración; consulta de auditoría | 2–6 | ○ |
| SEC-07 | Secretos fuera del repositorio | `.gitignore`, `.env.example` | Revisión + escaneo en pipeline | 0 | ◐ |

## Generales / DevSecOps / documentos

| ID | Requisito | Implementación | Entrega | Estado |
|---|---|---|---|---|
| GEN-01 | Repo GitHub: ramas, PR, Issues, Releases, protección de `main` | Git local inicializado; remoto pendiente | 0, 11 | ◐ |
| GEN-02 | Jenkinsfile con las 18 etapas + bloqueo por Quality Gate | — | 11 | ○ |
| GEN-03 | Dockerfiles + docker-compose | `docker-compose.yml` (Postgres, Redis, RabbitMQ; faltan Dockerfiles de API/Web) | 1, 11 | ◐ |
| GEN-04 | Azure + Terraform (aprovisionar, modificar, destruir) | — | 12 | ○ |
| GEN-05 | Ansible (SO, Docker, reverse proxy) | — | 12 | ○ |
| GEN-06 | API REST documentada (OpenAPI) | `AddOpenApi` + `/openapi/v1.json` en Development | 1 | ◐ |
| GEN-07 | SonarQube + Quality Gate | — | 11 | ○ |
| GEN-08 | Fortify, Dependency-Check, npm audit | — | 11 | ○ |
| GEN-09 | OWASP ZAP sobre QA | — | 11 | ○ |
| GEN-10 | Cypress (smoke, crítico, regresión, negativo) | — | 10 | ○ |
| GEN-11 | Maestro (5–8 flujos) | — | 10 | ○ |
| GEN-12 | k6/JMeter: Load, Stress, Spike con umbrales previos | — | 13 | ○ |
| GEN-13 | Usabilidad (≥5 participantes) + Lighthouse/axe | — | 13 | ○ |
| GEN-14 | Registro de defectos con evidencia | — | continuo | ○ |
| GEN-15 | Plan de pruebas (20 secciones), riesgos C/I/D, casos, informe GO / NO-GO | — | 14 | ○ |
| GEN-16 | Ambiente reproducible desde código y documentación | README + compose + migraciones | 1, 12 | ◐ |
| GEN-17 | Insomnia: colección +/−/authz | — | 10 | ○ |

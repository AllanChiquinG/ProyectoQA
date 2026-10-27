# MediCore · Plan de trabajo por entregas

Cada entrega es pequeña, termina compilando, con pruebas verdes y la matriz de trazabilidad actualizada. Un flujo a la vez, comparado contra el diseño de referencia.

| # | Entrega | Contenido | Estado |
|---|---|---|---|
| 0 | Preparación | Solución, API y web iniciales, CLAUDE.md, README, plan y trazabilidad, prueba de humo | **Hecha** |
| 1 | Base técnica | Capas del monolito modular (dominio/aplicación/infraestructura por módulo), EF Core + PostgreSQL con migraciones, `docker-compose` (Postgres, Redis, RabbitMQ), `.env.example`, configuración y secretos locales (user-secrets), manejo global de errores, Swagger/OpenAPI | **Hecha** (PR #1 fusionado) |
| 2 | Identidad y acceso | Usuarios, roles, login web (cookie) y API (JWT corto + refresh), sesiones en Redis, políticas por rol y por recurso, auditoría base, WEB-01, MOB-01 (API), usuarios y roles (WEB-10) | **Hecha** (PR #2 fusionado) |
| 3 | Pacientes y médicos | CRUD, búsqueda, `GET /api/patients/{id}`, `GET /api/doctors`, WEB-05…08, auditoría de accesos a datos clínicos | **Hecha** (PR #3 fusionado) |
| 4 | Agenda y citas | Disponibilidad (WEB-09), `GET /api/doctors/{id}/availability`, `POST /api/appointments`, cancelación, **anti doble reserva** (restricción única en BD + concurrencia optimista/transacción), cache Redis de disponibilidad con invalidación, WEB-02/03/04/11 | Pendiente de fusión: el PR sigue abierto (origin/main no lo contiene) |
| 5 | Consultas y recetas | Expediente, consultas, recetas, `GET /api/prescriptions/{id}`, WEB-12…15, autorización por recurso | Implementada en la rama `feature/entrega-5-consultas-recetas` (apilada sobre la 4; pendiente de PR y fusión) |
| 6 | Laboratorio | `POST /api/labs/results`, WEB-16…18, resultado guardado antes de publicar el evento (outbox) | Pendiente |
| 7 | Mensajería y notificaciones | RabbitMQ: productor, consumidor, reintentos con espera, DLQ, idempotencia; los 5 eventos; outbox transaccional; reintento de notificación | Pendiente |
| 8 | Web del personal completa | Pulido contra el diseño, accesibilidad, responsive, español | Pendiente |
| 9 | App móvil | .NET MAUI Android (MOB-01…12); requiere instalar el workload MAUI y Android SDK | Pendiente |
| 10 | Pruebas | Unitarias, integración (BD/Redis/RabbitMQ con Testcontainers), API (Insomnia), concurrencia de agenda, Cypress, Maestro | En curso (Cypress iniciado con el recorrido de Recepción) |
| 11 | DevSecOps | Jenkinsfile (18 etapas), SonarQube, Dependency-Check, npm audit, Fortify, ZAP, Dockerfiles | Pendiente |
| 12 | Cloud | Terraform (Azure), Ansible, aprovisionar/modificar/destruir, ambiente QA | Pendiente |
| 13 | Rendimiento y calidad | k6/JMeter con umbrales previos, Lighthouse/axe, prueba de usabilidad | Pendiente |
| 14 | Cierre | Plan de pruebas (20 secciones), matriz de riesgos, casos/evidencias, defectos, informe GO / NO-GO, presentación | Pendiente |

Decisión aceptada (entregas 3–4): el médico accede a pacientes con quienes tiene una cita no cancelada **o una consulta registrada**; la regla se valida en la API (`PatientAuthorizationHandler`). Cancelar una cita solo retira el acceso si no queda otra relación válida (otra cita con ese médico o una consulta). La parte de consultas se implementó y se prueba en la entrega 5 (la prueba que estaba omitida ya está activa).

Pendientes obligatorios de pruebas (ver matriz de trazabilidad): Cypress, Insomnia, Maestro y rendimiento con k6/JMeter.

Decisión aceptada (entrega 5, ajuste): **expediente compartido entre médicos autorizados.** Un médico con relación de atención válida lee las consultas y recetas que registraron otros médicos; nadie modifica registros de otro profesional; sin relación válida no hay acceso; los demás roles mantienen sus restricciones. Detalle en `CLAUDE.md` y en `04-registro-de-hallazgos.md`.

Dependencias clave: 2 antes de 3–6; 4 y 6 usan los eventos de 7 (se publican vía outbox desde 4 y 6 y se consumen en 7); 9 requiere API estable (≥ entrega 6).

Los entregables de documentación (plan de pruebas, riesgos, casos) se van construyendo en cada entrega y se consolidan en la 14.

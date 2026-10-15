# MediCore · Alcance, pantallas y requisitos

Fuente: `Docs/Proyecto Final QA.docx` (Proyecto 2 + secciones generales 1–9, 11, 12) y `Referencias/medicore/`. El documento se leyó completo, incluidas las tablas (extraídas del XML del .docx). No se implementan los Proyectos 1, 3 y 4.

## Alcance

**Dentro:** gestión de pacientes y médicos; agenda y reserva de citas; consultas; recetas; resultados de laboratorio; notificaciones al paciente; app móvil Android del paciente; autenticación, autorización por rol y por recurso, auditoría.

**Fuera:** portal web del paciente (cancelado en la revisión del diseño), cualquier módulo no listado, microservicios.

## Roles (del prototipo)

Recepción, Médico, Laboratorio, Administrador (web del personal) y Paciente (móvil). El rol sale del usuario autenticado; la barra Web/Móvil + rol del prototipo **no** se copia.

## Inventario de pantallas

### Web del personal (WEB)

| ID | Pantalla | Roles | Notas |
|---|---|---|---|
| WEB-01 | Inicio de sesión | todos | Sin credenciales precargadas en el producto final |
| WEB-02 | Agenda de citas | Recepción, Médico (WEB-11) | Calendario, filtro por médico, horarios 08:00–11:30 cada 30 min |
| WEB-03 | Reservar cita | Recepción | Paciente, médico, fecha, horario; error si el horario está ocupado |
| WEB-04 | Detalle de cita | Recepción | Cancelar cita (con confirmación) |
| WEB-05 | Gestión de pacientes | Recepción, Administrador, Médico (solo autorizados) | Búsqueda por nombre/identificación |
| WEB-06 | Registrar / editar paciente | Recepción, Administrador | Identificación única |
| WEB-07 | Gestión de médicos | Administrador | |
| WEB-08 | Registrar / editar médico | Administrador | |
| WEB-09 | Disponibilidad médica | Administrador | Días, inicio/fin; fin > inicio |
| WEB-10 | Usuarios y roles | Administrador | Asignar rol |
| WEB-11 | Mi agenda | Médico | Vista del día; abre expediente |
| WEB-12 | Expediente del paciente | Médico | Pestañas Consultas / Recetas / Laboratorio |
| WEB-13 | Registrar consulta | Médico | Guardar / guardar y crear receta |
| WEB-14 | Crear receta | Médico | |
| WEB-15 | Detalle de receta | Médico | |
| WEB-16 | Resultados de laboratorio | Laboratorio | Estado de notificación Enviada/Pendiente |
| WEB-17 | Registrar resultado | Laboratorio | El resultado se guarda aunque la notificación quede pendiente |
| WEB-18 | Detalle de resultado | Laboratorio, Médico | Reintentar notificación |

### App móvil del paciente (MOB)

| ID | Pantalla |
|---|---|
| MOB-01 | Inicio de sesión |
| MOB-02 | Inicio (próxima cita, accesos) |
| MOB-03 | Mis citas |
| MOB-04 | Seleccionar médico (paso 1/3) |
| MOB-05 | Fecha y horario (paso 2/3) |
| MOB-06 | Confirmar cita (paso 3/3) |
| MOB-07 | Detalle de cita (cancelar) |
| MOB-08 | Mis resultados |
| MOB-09 | Detalle de resultado |
| MOB-10 | Mis recetas |
| MOB-11 | Detalle de receta |
| MOB-12 | Notificaciones |

## Requisitos obligatorios (resumen; IDs en la matriz de trazabilidad)

**Funcionales (Proyecto 2):** pacientes y médicos, agenda/citas, consultas, recetas, laboratorio, notificaciones, app móvil.

**API mínima de referencia:** `GET /api/doctors`, `GET /api/doctors/{id}/availability`, `POST /api/appointments`, `GET /api/patients/{id}`, `POST /api/labs/results`, `GET /api/prescriptions/{id}`.

**Redis:** cache de disponibilidad médica, catálogos, sesiones y consultas frecuentes; probar datos vencidos, TTL, invalidación y recuperación ante indisponibilidad.

**RabbitMQ:** eventos `AppointmentCreated`, `AppointmentCancelled`, `LabResultReady`, `PrescriptionCreated`, `PatientNotification`; productor, consumidor, reintentos y manejo de errores (DLQ o equivalente); una caída del broker no debe perder información clínica ya almacenada.

**Escenario crítico:** alta demanda de citas mientras los médicos consultan agendas y expedientes → sin doble reserva ni colisiones de horario, consistencia, y comportamiento correcto cuando RabbitMQ se interrumpe después de registrar un resultado.

**Seguridad:** RBAC clínico/administrativo, autorización a nivel de recurso (IDOR), exposición de datos sensibles, sesiones y autenticación, seguridad de API, trazabilidad y auditoría.

**Rendimiento:** pacientes pidiendo citas + profesionales consultando; p95, tasa de error, throughput, estabilidad; umbrales declarados antes de ejecutar; Load, Stress y Spike.

**Móvil:** solicitar/consultar citas, ver resultados y recetas, notificaciones; Maestro con 5–8 flujos (incluye al menos un negativo).

**Generales:** GitHub (ramas, PR, Issues, Releases, protección de `main`); Jenkins + Jenkinsfile; Docker/Compose; Azure + Terraform; Ansible; API REST documentada; Insomnia (positivas, negativas, autorización); SonarQube + Quality Gate; Fortify, Dependency-Check, npm audit; ZAP; Cypress (smoke, flujo crítico, regresión, ≥1 negativo); JMeter o k6; usabilidad (≥5 participantes) + Lighthouse/axe; registro de defectos; secretos fuera del repo; ambiente reproducible desde el repo.

**Pipeline mínimo (18 etapas):** checkout, dependencias, lint, unitarias, SonarQube, npm audit, Dependency-Check, Fortify, Docker build, deploy QA, pruebas API, Cypress, Maestro, ZAP, rendimiento, Quality Gate, aprobación, deploy final. Prueba crítica fallida, vulnerabilidad crítica no aceptada o Quality Gate rechazado bloquean el deploy final.

**Entregables documentales:** plan de pruebas (20 secciones obligatorias, sección 7 del documento), matriz de riesgos (C/I/D), casos y evidencias, registro de defectos, informe final con métricas y GO / NO-GO, presentación (10–15 min) y demo.

## Observaciones sobre el documento y el diseño

- El documento no fija el modelo de sesiones/JWT; se decide en la entrega de autenticación (ver CLAUDE.md).
- El prototipo permite que Recepción/Administrador gestionen pacientes y que el Médico vea "Pacientes autorizados": la regla de "autorizado" (relación médico–paciente por cita/consulta) hay que definirla; no está en el documento. Propuesta pendiente de validar.
- El prototipo trae un rol "Paciente" en Usuarios y roles, usado por la app móvil.
- Hallazgo del prototipo: la reserva/cancelación y disponibilidad usan solo el día del mes (sin mes/año) y datos en memoria; no es modelo válido de concurrencia.
- Numeración del documento: salta de la sección 9 a la 11 (no existe la 10). No afecta al alcance.

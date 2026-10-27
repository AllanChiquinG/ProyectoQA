# MediCore · Registro de hallazgos y verificación manual

Registro de los errores que aparecieron al construir cada entrega, su causa, la corrección y cómo se comprobó. Es el punto de partida del registro formal de defectos (entregable de la entrega 14: pasos, esperado, obtenido, severidad, prioridad y evidencia).

Tipos: **Producto** (defecto del sistema), **Prueba** (defecto de la prueba o de su dato), **Entorno** (herramienta o infraestructura local).

## Entrega 4 · disponibilidad, agenda y citas

| ID | Tipo | Qué ocurrió | Causa | Corrección | Resultado |
|---|---|---|---|---|---|
| H4-01 | Producto | Con 12 reservas simultáneas al mismo horario, 11 respondían 500 en lugar de 409 (`40P01: deadlock detected`). | Las restricciones de exclusión de PostgreSQL insertan la fila y luego buscan conflictos; dos inserciones en conflicto se ven mutuamente sin confirmar y se bloquean entre sí. | La alta se serializa con `pg_advisory_xact_lock` por horario del médico y por paciente dentro de una transacción, y se reintenta (hasta 3 veces) ante 40P01/40001. Las restricciones siguen siendo la garantía final. | Las pruebas de concurrencia pasan: 1 reserva y 11 conflictos limpios. Repetidas 3 veces seguidas sin fallos. |
| H4-02 | Producto | `GET /api/health` y las rutas protegidas respondían 500 en pruebas sin base de datos (3 pruebas fallaron). | `PatientAuthorizationHandler` recibía `MediCoreDbContext` por constructor y ASP.NET lo instancia en cada autorización; sin cadena de conexión, crear el contexto lanzaba excepción. | El manejador recibe `IServiceProvider` y obtiene el contexto solo cuando evalúa al médico. | `BasicEndpointTests` pasan; la API vuelve a arrancar y responder sin PostgreSQL configurado. |
| H4-03 | Producto (web) | Toda página con `@section Scripts` (Agenda, Mi agenda, detalle de cita) daba 500: "sections defined but not rendered". | `_Shell.cshtml` es un layout anidado y no reenviaba la sección al layout base. | `_Shell` declara `@section Scripts` y la reenvía con `RenderSectionAsync`. | Agenda, Mi agenda y detalle cargan; comprobado en navegador. |
| H4-04 | Producto (web) | En "Reservar cita", tras cambiar paciente o fecha, **Confirmar cita** no reservaba y la página quedaba igual. | El formulario no tenía `action`: después de "Actualizar horarios" la URL conservaba `?handler=Actualizar` y el envío volvía a ese manejador. | `action` explícito a `/Agenda/Reservar`. | Ver H4-05. |
| H4-05 | Producto (web) | Con el `action` explícito, el envío daba una página vacía (HTTP 400). | Al declarar `action` a mano el *tag helper* no genera el token antifalsificación. | `asp-antiforgery="true"` en el formulario. | La reserva se completa y redirige al detalle de la cita; comprobado en navegador (envío con JavaScript, ver más abajo). |
| H4-06 | Prueba | `Administrador_configura_y_consulta_el_horario…` fallaba aunque los valores eran iguales. | Comparaba tuplas que contenían un arreglo (se comparan por referencia). | Se comparan por separado el arreglo de días y el resto. | Pasa. |
| H4-07 | Prueba | `No_se_puede_cancelar_una_cita_que_ya_paso` lanzaba `DbUpdateException` al preparar el dato. | Se insertaba un `DateTime` con `Kind=Utc` en una columna `timestamp without time zone` (Npgsql lo rechaza). | El asistente de pruebas fuerza `DateTimeKind.Unspecified`. | Pasa. |
| H4-08 | Prueba | Una aserción de `Alta_demanda…` estaba mal escrita (expresión sin sentido) y habría dado un resultado engañoso. | Error al redactar la prueba. | Detectada al revisarla antes de depender de ella; reescrita con conteos explícitos (4 creadas, 20 conflictos, 20 lecturas 200). | Pasa y es legible. |
| H4-09 | Producto (datos de desarrollo) | Advertencia de EF Core `10103` (`First` sin `OrderBy`) al sembrar las citas de demostración. | Consulta sin orden determinista en `ClinicalDemoSeeder`. | `OrderBy` por correo normalizado. | Sin advertencia. |
| H4-10 | Entorno | Al empezar, `/api/health/ready` respondía `unavailable`; los contenedores estaban detenidos. | Docker Desktop reiniciado; no es un defecto del sistema. | `docker compose up -d`. | PostgreSQL, Redis y RabbitMQ saludables. |
| H4-11 | Entorno | Capturas y clics del panel del navegador fallaban a veces ("Screenshot timed out"). | El panel no terminaba de dibujar con la ventana en segundo plano. | Se reintentó y, cuando no hubo forma de hacer el clic, se envió el formulario con JavaScript (ver más abajo). | Limita lo que cuenta como interacción real; ver detalle. |

Hallazgo de diseño aceptado (no es un error): el calendario del prototipo tenía la leyenda "Seguimiento" (color azul) que no corresponde a ningún estado del sistema; se retiró y solo queda "Confirmada".

## Verificación en navegador de la entrega 4: qué fue interacción real y qué fue JavaScript

Las pruebas automatizadas (129: 128 superadas y 1 omitida) no usan navegador. La revisión manual en el navegador integrado se hizo así; **no sustituye** a Cypress.

### Interacción real (herramienta de navegador: teclas, clics, formularios y capturas)

| Comprobación | Cómo |
|---|---|
| Inicio de sesión de Recepción (Elena) | Campos con la herramienta de formularios y envío con la **tecla Enter**. |
| Pantalla WEB-02 (agenda) con captura de pantalla | Navegación y captura; coincide visualmente con el diseño. |
| Cambio de médico en el filtro de la agenda | Valor del `select` fijado con la herramienta de formularios; la **propia página** disparó su envío automático y mostró las 4 citas de la Dra. Méndez. |
| Reservar cita: elegir paciente | Igual que arriba; la página se recargó sola con los horarios. |
| Reservar cita: clic en el horario y en "Confirmar cita" | **Clics reales**, hechos en un intento anterior; ahí se descubrió el defecto H4-04. Después de la corrección, los clics fallaron por tiempo agotado del panel (H4-11). |
| Pantalla WEB-09 (disponibilidad) con captura | Captura; coincide con el diseño. |
| Lectura del texto de la agenda del médico y del listado de pacientes del médico | Lectura de la página (no cambia datos). |

### Ejecutado con JavaScript (código que inyecté en la página; no es un usuario haciendo clic)

| Comprobación | Qué hice | Qué **no** queda probado |
|---|---|---|
| Reserva correcta de la cita de las 09:30 | `form.requestSubmit(...)` | El clic en el botón. |
| Conflicto de horario (10:30 reservado por la API antes del envío) | `requestSubmit` y lectura del mensaje y del estado de los campos | El clic; el aspecto visual del mensaje. |
| Cancelación de la cita | `form.submit()` | El cuadro de confirmación "¿Confirmas que deseas cancelar esta cita?" **no se mostró ni se probó**; `submit()` lo omite. |
| Cierre de sesión y nuevo inicio de sesión (Lucía y administrador) | `submit()` / `requestSubmit()` con valores asignados por código | El flujo con teclado y clics. |
| Disponibilidad: hora final anterior a la inicial, guardado válido y restauración | Valores asignados por código y `requestSubmit` | Los clics y los campos de hora con el selector del navegador. |

### Lo que no se verificó en navegador

- Cuadro de confirmación al cancelar, navegación por mes del calendario (‹ ›), clic en "＋ Horario disponible", "Ver detalle ↗".
- Navegación solo con teclado, lector de pantalla, vista móvil y pantallas pequeñas.
- La web con la sesión de Recepción tras cambiar la zona horaria o cerca de medianoche.

Esto debe cubrirlo Cypress (smoke, flujo crítico, regresión y escenario negativo) y la revisión de accesibilidad con Lighthouse o axe.

## Entregas anteriores

Los hallazgos de las entregas 0 a 3 se trasladarán al registro formal de defectos en la entrega 14. Los principales: login con 503 por usar `localhost` para Redis (el cliente probaba IPv6 y Docker publica solo IPv4; corregido con `127.0.0.1`), una prueba que comparaba el `traceId` de dos respuestas de error, una prueba del catálogo de médicos afectada por la caché compartida con otras pruebas (se descarta la clave antes de insertar datos directos) y la interfaz de `IAsyncLifetime` de xUnit v2.

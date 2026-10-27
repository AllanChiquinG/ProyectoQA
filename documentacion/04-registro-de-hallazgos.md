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

| H4-12 | Producto (web) | Con Cypress, escribir la fecha en el filtro de la agenda no la aplicaba (la URL conservaba la fecha anterior). | El envío automático escuchaba solo `change`. Cypress emite `input` y `change` solo al perder el foco; además, en navegadores de escritorio un campo de fecha emite `change` en cada edición válida mientras se teclea (por ejemplo, al escribir el primer dígito del año), lo que podría recargar la página a medias. Esto último es el comportamiento conocido del navegador: **no lo reproduje con un teclado físico**. | `autosubmit.js` y `reservar.js` escuchan `input` y `change` de los campos de fecha, esperan 700 ms a que termine la escritura y envían al instante al salir del campo. Las listas se siguen enviando al instante. | Cypress 8/8 en Electron y en Chrome. |
| H4-13 | Prueba | Todas las pruebas fallaban en el `before` con "`Cypress.env()` was removed in Cypress version 16". | Cypress 16 eliminó `Cypress.env()`. | Valores no sensibles con `expose` y la contraseña con `cy.env()`. | Pasa. |
| H4-14 | Prueba | Al usar `clear()` en la fecha, la agenda recargaba con la fecha vacía (la API toma "hoy"). | Borrar un campo de fecha lo cambia, y el envío automático lo envía. Comportamiento esperado, no un defecto. | Las pruebas escriben la nueva fecha sin borrar antes. | Pasa. |
| H4-15 | Prueba y producto (límite) | Elegir el paciente justo después de cambiar la fecha fallaba: "this element is `disabled`" (`--submitting form--`). | El navegador estaba enviando la actualización de horarios. En uso real, un cambio hecho en los pocos cientos de milisegundos de una recarga en curso se pierde. | La prueba espera cada recarga (`cy.intercept` + `cy.wait`). El límite del producto se acepta y queda documentado. | Pasa. |
| H4-16 | Entorno | Cambiar los `.js` no tenía efecto y la prueba seguía fallando igual. | Los archivos estáticos se publican con huella en la compilación (`MapStaticAssets`): hay que recompilar y reiniciar la web. | Compilar antes de probar (anotado en `CLAUDE.md`). | Una ejecución perdida. |
| H4-17 | Herramienta | Las capturas de Cypress salían recortadas y con barra de desplazamiento horizontal. | La ventana de Electron era menor que el *viewport* configurado (1440). | *Viewport* 1280×800. El desbordamiento horizontal se descartó midiendo `scrollWidth == clientWidth` en Agenda, Reservar y Pacientes, en Electron y en Chrome. | Capturas correctas. |

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

### Verificación con Cypress, sin inyectar JavaScript (entrega 4, repetida y ampliada)

Suite: `tests/cypress/e2e/recepcion-reserva.cy.js` (8 pruebas). Todas las acciones del usuario van por la interfaz (escribir, elegir, pulsar); la API solo prepara y limpia datos y simula a *otra persona* que ocupa el horario en el paso 7. Resultado: **8 de 8 superadas**, en **Electron 146** (4 ejecuciones seguidas) y en **Chrome 155** (2 ejecuciones), todas sin intervención manual y repetibles (los datos de prueba se limpian y reutilizan).

| Paso pedido | Prueba | Resultado | Qué se comprobó |
|---|---|---|---|
| 1. Iniciar sesión como Recepción | 1 | ✔ | Escribir correo y contraseña, pulsar "Iniciar sesión"; aparece "Elena Vargas · Recepción". |
| 2. Elegir médico, fecha, paciente y horario | 2 y 3 | ✔ | Elegir médico y fecha en la agenda (la página se actualiza sola), "Reservar cita", cambiar la fecha en el formulario y volver (los horarios se actualizan), elegir paciente y pulsar el horario 09:00. |
| 3. Confirmar con el botón | 2 y 3 | ✔ | Botón "Confirmar cita →"; abre el detalle con aviso "Cita confirmada correctamente", paciente, médico, hora 09:00 y estado Confirmada. |
| 4. Abrir "Ver detalle" | 4 | ✔ | Desde la agenda, "Ver detalle ↗" de la cita lleva a la misma cita. |
| 5. Cancelar rechazando la confirmación | 5a | ✔ | El cuadro pide exactamente "¿Confirmas que deseas cancelar esta cita?", se responde que no, **no se envía ninguna petición**, el estado sigue Confirmada y la cita sigue en la agenda. |
| 5. Cancelar aceptando | 5b | ✔ | Se envía la petición, aviso "La cita fue cancelada", estado Cancelada y desaparece el botón. |
| 6. El horario vuelve a estar disponible | 6 | ✔ | En la agenda 09:00 muestra "＋ Horario disponible" sin el paciente, y al pulsarlo el formulario trae 09:00 habilitado y elegido. |
| 7. Conflicto de horario conservando lo ingresado | 7 | ✔ | Otra persona reserva 10:00 mientras se llena el formulario; al confirmar aparece "Este horario ya está ocupado. Selecciona otro para continuar.", **se conservan paciente, médico y fecha**, el horario 10:00 queda deshabilitado y sin elegir, y se puede reservar 10:30 sin empezar de nuevo. |
| 8. Cambiar de mes en el calendario | 8 | ✔ | "Mes siguiente" muestra el mes siguiente con el día 1 marcado, otra vez el siguiente, "Mes anterior" dos veces regresa, y elegir el día 15 cambia la fecha de la agenda. |

Evidencia: `reports/cypress/resultados-electron.xml`, `reports/cypress/resultados-chrome.xml` y capturas `reports/cypress/screenshots/recepcion-reserva.cy.js/01…08` (de la ejecución en Chrome).

Límites de esta verificación (no se marca como probado):

- Cypress dispara eventos del navegador; **no es una persona con teclado y ratón físicos**. En particular, escribir la fecha con teclado real (H4-12) no se reprodujo.
- El cuadro `confirm()` lo atiende Cypress: se comprobó que la página lo pide con el texto correcto y que respeta la respuesta, no su aspecto.
- Ejecución *headless*; no se midió accesibilidad (Lighthouse/axe), teclado, lector de pantalla ni móvil.
- Solo cubre Recepción. Médico, Administrador, pantallas de pacientes y médicos, smoke general y regresión siguen pendientes.

Con esta suite quedan **sustituidas** las comprobaciones con JavaScript de reserva, conflicto y cancelación de la sección anterior; las de cierre de sesión, inicio de sesión del médico y del administrador y de la pantalla de disponibilidad siguen siendo solo las descritas arriba (JavaScript).

### Lo que no se verificó en navegador

- Aún sin verificar con interacción real: cierre de sesión, inicio de sesión de Médico y Administrador, y la pantalla de disponibilidad (WEB-09), que solo se probaron con JavaScript.
- Navegación solo con teclado, lector de pantalla, vista móvil y pantallas pequeñas.
- La web con la sesión de Recepción tras cambiar la zona horaria o cerca de medianoche.

Falta ampliar Cypress (smoke, otros roles, regresión y más escenarios negativos) y la revisión de accesibilidad con Lighthouse o axe.

## Entrega 5 · expediente, consultas y recetas

| ID | Tipo | Qué ocurrió | Causa | Corrección | Resultado |
|---|---|---|---|---|---|
| H5-01 | Entorno / proceso | Se indicó que el PR de la entrega 4 estaba fusionado, pero `origin/main` seguía en la fusión del PR #3 (`00e510d`). | La fusión no llegó a `origin/main` (no se verifica con `gh`; se comprobó con `git ls-remote`). | La rama `feature/entrega-5-consultas-recetas` se creó **sobre `feature/entrega-4-agenda-citas`** (rama apilada) para no perder la entrega 4. | Al fusionar el PR de la entrega 4, el de la 5 mostrará solo sus cambios. **Pendiente de tu confirmación.** |
| H5-02 | Prueba | Al ejecutar varias suites seguidas, las preparaciones por API fallaban con `429 Too Many Requests` y todo quedaba en 0 pruebas. | El límite de intentos de inicio de sesión (10 por minuto por IP) funcionó como debe; las suites hacen muchos inicios de sesión. | En Development el límite sube a 60 por minuto (`appsettings.Development.json`); en otros ambientes sigue en 10. | 15/15 pruebas, 4 ejecuciones seguidas por navegador. |
| H5-03 | Herramienta | La captura de pantalla de Electron se colgaba de forma intermitente (cerca de 2 de cada 6 ejecuciones) y hacía fallar pruebas funcionales que sí pasaban. | Electron está obsoleto en Cypress 16; el problema es de la captura, no de la aplicación. | Las capturas de evidencia se toman solo en navegadores reales (Chrome); en Electron se ejecutan todas las aserciones. | Sin fallos en 4 ejecuciones seguidas en Electron. |
| H5-04 | Producto (web) | Abrir el expediente de un paciente sin relación mostraba una página en blanco (404 sin cuerpo). | Las respuestas vacías no tenían página. | Página `/Estado` con `UseStatusCodePagesWithReExecute`: mensaje en español y se conserva el código HTTP. | Cypress paso 8: estado 404 y "No encontramos lo que buscas", sin datos del paciente. |
| H5-05 | Prueba | Riesgo de falso negativo en la prueba de bitácoras: si el capturador no recibiera nada, "no contiene el dato" pasaría igual. | — | Se agregó un control: se escribe un texto conocido en la bitácora y se exige que el capturador lo vea antes de comprobar que el contenido clínico no aparece. | Pasa y es significativa. |
| H5-06 | Herramienta | Una edición de archivo fue rechazada ("modified since read") tras cambiar de rama. | El archivo cambió al cambiar de rama. | Se volvió a leer y a escribir. | Sin efecto en el resultado. |

Decisiones y desviaciones del diseño en esta entrega (no son errores):

- **Varios medicamentos (WEB-14):** el diseño muestra un solo medicamento; se agregó "＋ Agregar medicamento" y "Quitar este medicamento" (hasta 10), cada uno con dosis, frecuencia, duración e indicaciones, como se pidió. Funciona sin JavaScript.
- **Cita atendida (WEB-13):** se agregó la lista "Cita atendida" para asociar la consulta a la cita (requisito); se propone la cita desde la que se abrió el expediente.
- **"Crear receta ↗"** en cada consulta del expediente (el diseño solo ofrece "Guardar y crear receta" al registrar).
- **Laboratorio:** el diseño dice "Sin resultados registrados"; como el módulo no existe, se indica que está **pendiente** y no se muestra nada.
- **Qué ve el médico:** en el expediente solo lo que él escribió, no las notas de otros médicos del mismo paciente (mínimo privilegio). Es una decisión de seguridad que puedes cambiar.

### Verificación de la entrega 5

- **Cypress `medico-expediente.cy.js` (10 pruebas tras el ajuste del expediente compartido), sin inyectar JavaScript:** iniciar sesión como Médico; abrir el expediente desde la agenda; recorrer Consultas, Recetas y Laboratorio (pendiente); guardar una consulta incompleta (errores); registrar la consulta; crear una receta con dos medicamentos (agregar y quitar filas conservando lo escrito); ver el detalle; volver al expediente y abrir la receta desde su tarjeta; comprobar que la cita ya no admite otra consulta; y que un paciente sin relación devuelve 404. **10/10 y la regresión de Recepción 8/8, en Chrome 155 y Electron 146** (una ejecución por navegador tras el ajuste; en la primera ejecución la prueba 10 falló porque `cy.visit` rechaza el 403 de Acceso denegado: se corrigió la prueba con `failOnStatusCode: false`, no la aplicación). No se repitió a distintas horas del día; la independencia horaria se debe al diseño (cita pasada insertada directamente).
- **Regresión de Recepción `recepcion-reserva.cy.js` (8 pruebas):** 8/8 en ambos navegadores, tras los cambios de esta entrega.
- **Límites:** Cypress dispara eventos del navegador (no es una persona con teclado físico); `confirm()` no interviene en este recorrido; no se midió accesibilidad ni móvil; cada ejecución agrega una consulta y una receta de prueba (la información clínica no se borra) con un texto único.

## Entrega 5 · ajustes posteriores (expediente compartido y pruebas independientes de la hora)

**Decisión de producto (reemplaza la de la primera versión de la entrega 5):** el expediente es **compartido entre médicos autorizados**.

- Un médico con relación de atención válida con el paciente (una cita no cancelada o una consulta registrada) **lee** las consultas y recetas escritas por otros médicos; cada registro indica su autor (`IsOwn`, `DoctorName`) y la lectura de lo ajeno se audita como `other_author`, sin contenido.
- **No puede modificar** registros de otro profesional: solo el autor crea recetas sobre su consulta, solo se registra la consulta de una cita propia y no existe operación de edición ni de borrado de consultas ni de recetas (PUT, PATCH y DELETE devuelven 405, también para el autor).
- Sin relación válida no hay acceso (404 y `clinical.access_denied`); al cancelarse la última cita sin consulta se pierde la lectura. Recepción, Administrador y Laboratorio mantienen sus restricciones; el paciente lee solo sus recetas, de cualquiera de sus médicos.
- Motivo y riesgo: la relación de atención es una barrera baja (basta una cita no cancelada, incluso futura, con ese médico). Con el expediente compartido, esa cita da lectura de todo el historial clínico del paciente. Se acepta por indicación del equipo; si se quisiera endurecer, bastaría exigir una cita ya atendida o una consulta, cambiando `PatientAccess.HasCareRelationship` (una sola definición).

| ID | Tipo | Qué ocurrió | Causa | Corrección | Resultado |
|---|---|---|---|---|---|
| H5-07 | Prueba | El spec de Cypress del médico dependía de la hora del día: necesitaba un horario libre **más tarde hoy** y fallaba en los últimos minutos del día. | Para registrar una consulta hace falta una cita de hoy o anterior; la API no permite reservar en el pasado, así que la prueba reservaba "hoy, más tarde". | Cita ya pasada creada con una tarea de Cypress directamente en la base de datos **local** (`db:createPastAppointment`; se niega a conectar con cualquier servidor que no sea local). No se debilita ninguna validación de la API. | La prueba no mira el reloj. |
| H5-08 | Prueba | La prueba del médico **cambiaba temporalmente el horario de la Dra. Méndez** (un médico de uso normal) y lo restauraba al final. | Necesitaba que atendiera todo el día. | Dos médicos dedicados a Cypress, sembrados solo en Development (`e2e.medico` y `e2e.colega`, ver `E2eFixtureSeeder`). Ningún médico de uso normal se toca. | Sin cambios en agendas reales. |
| H5-09 | Prueba | Cada ejecución del spec del médico dejaba una consulta y una receta nuevas. | La información clínica no se borra por la API. | Tarea `db:cleanE2eClinical` al empezar y al terminar: borra consultas, recetas y citas **solo** de los pacientes `CYP-E2E-*`. | Sin acumulación; el estado vacío del expediente ahora se comprueba. |
| H5-10 | Producto (diseño de código) | Con el expediente compartido, `PrescriptionAuthorizationHandler` necesitaba la relación de atención y la consulta estaba escrita dentro de otro manejador. | Duplicación. | Servicio `ICareRelationship` (usado por ambos manejadores; mismo criterio que el listado de pacientes). Las pruebas unitarias lo simulan sin base de datos. | 31 pruebas unitarias. |

## Entrega 6 · laboratorio y notificaciones asíncronas

### Qué se implementó, qué se ejecutó y qué no

**Implementado:** resultados de laboratorio (dominio, migración `LaboratorioYNotificaciones`, `LabsController`, `LabResultAuthorizationHandler`), pantallas WEB-16/17/18 y la pestaña Laboratorio del expediente, outbox transaccional con publicador en segundo plano, consumidor de `LabResultReady` con reintentos, DLQ e idempotencia, API de notificaciones del paciente (para la futura app móvil), scripts de ejecución en contenedor Linux y de evidencia.

**Ejecutado (todo con resultado real):**

| Qué | Dónde | Resultado |
|---|---|---|
| `dotnet test MediCore.slnx` (`scripts/dotnet-linux.sh test MediCore.slnx`) | contenedor Linux `dotnet/sdk:10.0`, Testcontainers con PostgreSQL 17, Redis 7 y RabbitMQ 4 | **226/226**: 44 unitarias y 182 de integración (43 de `LaboratoryApiTests`, 8 de `MessagingTests`) |
| Cypress `laboratorio-resultados`, `medico-expediente`, `recepcion-reserva` | Chrome 155 y Electron 146, contra la API y la web en contenedores Linux conectados al compose real | **26/26** en cada navegador (8 + 10 + 8) |
| `scripts/evidencia_laboratorio.py` | RabbitMQ real de compose (se detiene y se vuelve a iniciar) | Escenarios A–D correctos; informe en `documentacion/evidencias/entrega-6-laboratorio.md` |
| Migración | generada en el contenedor y aplicada a la base de desarrollo al arrancar la API (solo agrega tablas y una columna; no borra datos) | aplicada sin errores |

**No ejecutado o no verificado:**
- Nada se ejecutó de forma nativa en Windows: **Smart App Control** (activo) bloquea los `.dll` sin firma recién compilados; no se desactivó (ver H6-01). El código se compila y prueba en Linux.
- `dotnet format --verify-no-changes` en Linux reporta diferencias de espacios en archivos que esta entrega no tocó (por ejemplo `AgendaDay.cs`), así que no es concluyente; **no se afirma** que el formato esté verificado.
- No se midió rendimiento (k6/JMeter pendiente), accesibilidad ni uso móvil; no hay Insomnia ni Maestro.
- El caso "RabbitMQ detenido" no se automatiza en Cypress (no controla Docker); se cubre con `MessagingTests` y con el script de evidencia.
- No se probó la pérdida definitiva de la conexión con el broker más allá de detenerlo y volver a iniciarlo, ni varias instancias de la API publicando a la vez (la consulta usa `FOR UPDATE SKIP LOCKED`, pero no hay prueba con dos procesos).
- Cambio de `docker-compose.yml` (nombre de nodo fijo `medicore-rabbitmq`) **sin aplicar**: el contenedor de RabbitMQ en ejecución no se recreó para no tocar los datos existentes; al recrearlo, RabbitMQ empieza con un almacén de colas vacío dentro del mismo volumen (los datos anteriores no se borran).

### Decisiones y adaptaciones al diseño

- **Quién ve qué en laboratorio:** Laboratorio ve todos los resultados (equipo compartido; decisión revisable hacia "solo los propios"); el Médico, solo con relación válida de atención; el Paciente, solo los suyos; Recepción y Administrador, ninguno. Acceso ajeno → 404 (no confirma existencia) + `clinical.access_denied`; rol sin permiso → 403.
- **Resultados inmutables:** no hay edición ni borrado (PUT/PATCH/DELETE → 405); una corrección es un resultado nuevo, como las recetas.
- **WEB-17:** el diseño tiene un solo parámetro; se permiten de 1 a 10 (filas que se agregan y quitan sin perder lo escrito, también sin JavaScript). Unidad y rango son opcionales (hay resultados cualitativos). Se agrega un filtro para elegir al paciente. **No se copió** la casilla "Simular envío de notificación pendiente" del prototipo: la notificación pendiente es un estado real (broker caído o consumidor con fallos), no una simulación.
- **WEB-18:** "Reintentar notificación" aparece solo para Laboratorio y solo si la notificación sigue pendiente. El médico ve el detalle y vuelve a su expediente.
- **Estado de la notificación:** "Enviada" = existe la notificación interna del paciente; "Pendiente" = todavía no (el resultado ya está guardado).
- **El evento lleva solo identificadores**; el título y el cuerpo de la notificación (que incluye el tipo de examen) se arman al consumirlo y se guardan en la tabla `notifications`, que solo lee el propio paciente.
- **Alcance de mensajería:** solo `LabResultReady`; los demás eventos siguen en la entrega 7 (ver plan).

### Hallazgos

| ID | Tipo | Qué ocurrió | Causa | Corrección | Resultado |
|---|---|---|---|---|---|
| H6-01 | Entorno | Migraciones, pruebas y API dejaron de cargar los `.dll` del proyecto: "Una directiva de Control de aplicaciones bloqueó este archivo" (0x800711C7). | Smart App Control (estado activo) de Windows bloquea binarios sin firma recién compilados; antes de esta sesión no bloqueaba. | **No se desactivó** (es un ajuste de seguridad del equipo). Se añadieron `scripts/dotnet-linux.sh` y `scripts/dev-stack-linux.sh`: build, migraciones, pruebas y la pila de desarrollo corren en contenedores Linux del SDK oficial, sin tocar volúmenes ni redes de compose. | Todo lo ejecutado en esta entrega usó ese camino; falta repetirlo de forma nativa en un equipo sin el bloqueo. |
| H6-02 | Defecto de producto (detectado por `MessagingTests`) | Tras detener y volver a iniciar RabbitMQ, el consumidor dejaba de recibir mensajes y las notificaciones no se generaban. | El publicador, al ver la conexión cerrada mientras el cliente la recuperaba, creaba una conexión nueva y cerraba la anterior, que era la del consumidor. | `RabbitMqConnection` ya no reemplaza una conexión existente (se recupera sola, incluido el consumidor). | `Con_el_broker_detenido…` y las demás pruebas de mensajería pasan; la evidencia B lo repite contra el RabbitMQ real. |
| H6-03 | Prueba | Tres pruebas fallaban con `operator does not exist: jsonb ~~ jsonb`. | `Payload.Contains(...)` sobre una columna `jsonb` se traduce a `LIKE`. | Ayudante `OutboxIdsForLabResultAsync` con `Payload->>'labResultId'`. | 43/43 de laboratorio. |
| H6-04 | Prueba | `BrokerFixture` fallaba con "ports are not available ... Intento de acceso a un socket no permitido". | El puerto fijo elegido caía en un rango reservado por Windows (Hyper-V). | Puerto fijo aleatorio con reintento ante ese error (hace falta puerto fijo para detener y reiniciar el broker con la misma dirección). | 8/8 de mensajería. |
| H6-05 | Prueba | La validación de los atributos de un `record` posicional no se detectaba con `Validator.TryValidateObject`. | Los atributos están en los parámetros del constructor, no en propiedades; ASP.NET Core sí los aplica. | Ayudante que valida el valor de cada parámetro contra sus atributos. | 12 pruebas unitarias de validación. |
| H6-06 | Prueba existente | El recorrido 2 de Cypress del médico esperaba el aviso "laboratorio: pendiente". | La pestaña Laboratorio ahora lista resultados. | Espera "Sin resultados registrados." y el spec y la limpieza (`db:cleanE2eClinical`) incluyen resultados, notificaciones y eventos de laboratorio de los pacientes `CYP-E2E-*`. | 10/10 y regresión de Recepción 8/8. |
| H6-07 | Riesgo abierto | `dotnet format` no es concluyente en Linux; el consumidor se probó con un solo proceso; el almacén de colas de RabbitMQ cambia al recrear el contenedor con el nuevo nombre de nodo. | Ver "No ejecutado o no verificado". | Documentado; sin corrección en esta entrega. | Abierto. |

Evidencia reproducible: `documentacion/evidencias/entrega-6-laboratorio.md` (generada por `python scripts/evidencia_laboratorio.py`) y `documentacion/evidencias/entrega-6-pruebas.md` (comandos y resultados de las ejecuciones).

## Entregas anteriores

Los hallazgos de las entregas 0 a 3 se trasladarán al registro formal de defectos en la entrega 14. Los principales: login con 503 por usar `localhost` para Redis (el cliente probaba IPv6 y Docker publica solo IPv4; corregido con `127.0.0.1`), una prueba que comparaba el `traceId` de dos respuestas de error, una prueba del catálogo de médicos afectada por la caché compartida con otras pruebas (se descarta la clave antes de insertar datos directos) y la interfaz de `IAsyncLifetime` de xUnit v2.

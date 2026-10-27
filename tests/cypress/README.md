# Pruebas end-to-end (Cypress)

Pruebas de la web del personal. Hoy hay dos specs (18 pruebas):

- `recepcion-reserva.cy.js` (8): **Recepción** — agenda, reserva, detalle, cancelación con y sin confirmación, conflicto de horario y calendario.
- `medico-expediente.cy.js` (10): **Médicos** — agenda, expediente (consultas, recetas y laboratorio pendiente), registrar consulta, receta con varios medicamentos, acceso denegado a un paciente sin relación y **expediente compartido**: un colega con relación de atención lee lo que escribió otro médico, no puede crear recetas sobre su consulta (ni por la pantalla ni por la API) y pierde el acceso si deja de tener relación.

Faltan el *smoke* general, los flujos de Administrador y Laboratorio, una regresión formal y más escenarios negativos (entrega 10).

## Requisitos

- Node.js 20 o superior (probado con 24) y, para `--browser chrome`, Google Chrome instalado.
- Servicios en marcha: `docker compose up -d`, la API (`http://localhost:5011`) y la web (`http://localhost:5298`) en Development con los usuarios de desarrollo sembrados.
- La contraseña de los usuarios de desarrollo (`Seed:DevPassword`) en la variable `CYPRESS_MEDICORE_PASSWORD`. No se guarda en el repositorio.

## Ejecutar

```bash
cd tests/cypress
npm ci
export CYPRESS_MEDICORE_PASSWORD=<valor de Seed:DevPassword>
npx cypress run --browser chrome        # navegador de referencia (capturas de evidencia)
npx cypress run --browser electron      # obsoleto en Cypress 16: se ejecutan las aserciones pero no las capturas
npx cypress open                        # modo interactivo
```

Otras URL: `MEDICORE_WEB_URL` y `MEDICORE_API_URL`. Las capturas de evidencia se guardan en `reports/cypress/`.

## Qué hace y qué no

- **Todas las acciones del usuario van por la interfaz**: escribir, elegir en las listas, pulsar botones y enlaces. El inicio de sesión también (`cy.session` con el formulario real).
- **Aislamiento y hora del día:** el spec del médico usa dos médicos dedicados a Cypress (`e2e.medico@example.com` y `e2e.colega@example.com`, sembrados solo en Development con la contraseña de `Seed:DevPassword`) y los pacientes `Cypress…`; **no toca la agenda de ningún médico de uso normal**. Su cita es una cita **ya pasada** creada con la tarea `db:createPastAppointment` directamente en la base de datos local (la API no permite reservar en el pasado y una consulta solo se registra sobre citas de hoy o anteriores), por lo que **no depende de la hora del día**. Al empezar y al terminar la tarea `db:cleanE2eClinical` borra las consultas, recetas y citas de los pacientes `CYP-E2E-*` (y de nadie más), así que no se acumulan datos.
- **Acceso a la base de datos de las tareas:** variable `MEDICORE_E2E_DB` (`postgres://usuario:clave@127.0.0.1:5433/medicore`) o, si no existe, el archivo `.env` de la raíz (ignorado por Git). Las tareas **se niegan a conectar con un servidor que no sea local**: nunca deben apuntar a QA ni a producción. Hace falta `npm ci` (instala `pg`).
- En Development el límite de inicios de sesión es de 60 por minuto; con el valor de producción (10) varias suites seguidas reciben `429`.
- La API se usa solo para **preparar y limpiar datos** (un médico y dos pacientes con nombres fijos "Cypress…", sus horarios y las citas del día) y para simular a **otra persona** que ocupa un horario durante la prueba del conflicto. Esos registros se reutilizan entre ejecuciones y no se acumulan.
- El cuadro `confirm()` del navegador lo atiende Cypress (`window:confirm`): se comprueba que la página lo pide con el texto correcto y que respeta la respuesta, no cómo se ve el cuadro.
- Cypress dispara eventos del navegador; no es una persona con teclado y ratón. La accesibilidad, el uso solo con teclado y el móvil requieren otras pruebas (Lighthouse/axe, revisión manual).

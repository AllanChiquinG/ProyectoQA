# Pruebas end-to-end (Cypress)

Pruebas de la web del personal. Hoy hay dos specs (15 pruebas):

- `recepcion-reserva.cy.js` (8): **Recepción** — agenda, reserva, detalle, cancelación con y sin confirmación, conflicto de horario y calendario.
- `medico-expediente.cy.js` (7): **Médico** — agenda, expediente (consultas, recetas y laboratorio pendiente), registrar consulta, receta con varios medicamentos y acceso denegado a un paciente sin relación.

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
- El spec del médico amplía temporalmente el horario de la Dra. Méndez (lo restaura al terminar), reserva una cita de hoy para el paciente de prueba y la cancela al final. Cada ejecución agrega una consulta y una receta de prueba con un texto único; para empezar de cero, recrear la base de desarrollo (`docker compose down -v`).
- En Development el límite de inicios de sesión es de 60 por minuto; con el valor de producción (10) varias suites seguidas reciben `429`.
- La API se usa solo para **preparar y limpiar datos** (un médico y dos pacientes con nombres fijos "Cypress…", sus horarios y las citas del día) y para simular a **otra persona** que ocupa un horario durante la prueba del conflicto. Esos registros se reutilizan entre ejecuciones y no se acumulan.
- El cuadro `confirm()` del navegador lo atiende Cypress (`window:confirm`): se comprueba que la página lo pide con el texto correcto y que respeta la respuesta, no cómo se ve el cuadro.
- Cypress dispara eventos del navegador; no es una persona con teclado y ratón. La accesibilidad, el uso solo con teclado y el móvil requieren otras pruebas (Lighthouse/axe, revisión manual).

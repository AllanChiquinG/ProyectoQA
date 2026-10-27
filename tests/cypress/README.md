# Pruebas end-to-end (Cypress)

Pruebas de la web del personal. Hoy cubren el recorrido de **Recepción** (agenda, reserva, detalle, cancelación, conflicto de horario y calendario). Faltan el *smoke* general, los flujos de Médico, Laboratorio y Administrador, la regresión y más escenarios negativos (entrega 10).

## Requisitos

- Node.js 20 o superior (probado con 24) y, para `--browser chrome`, Google Chrome instalado.
- Servicios en marcha: `docker compose up -d`, la API (`http://localhost:5011`) y la web (`http://localhost:5298`) en Development con los usuarios de desarrollo sembrados.
- La contraseña de los usuarios de desarrollo (`Seed:DevPassword`) en la variable `CYPRESS_MEDICORE_PASSWORD`. No se guarda en el repositorio.

## Ejecutar

```bash
cd tests/cypress
npm ci
export CYPRESS_MEDICORE_PASSWORD=<valor de Seed:DevPassword>
npx cypress run --browser chrome        # o --browser electron
npx cypress open                        # modo interactivo
```

Otras URL: `MEDICORE_WEB_URL` y `MEDICORE_API_URL`. Las capturas de evidencia se guardan en `reports/cypress/`.

## Qué hace y qué no

- **Todas las acciones del usuario van por la interfaz**: escribir, elegir en las listas, pulsar botones y enlaces. El inicio de sesión también (`cy.session` con el formulario real).
- La API se usa solo para **preparar y limpiar datos** (un médico y dos pacientes con nombres fijos "Cypress…", sus horarios y las citas del día) y para simular a **otra persona** que ocupa un horario durante la prueba del conflicto. Esos registros se reutilizan entre ejecuciones y no se acumulan.
- El cuadro `confirm()` del navegador lo atiende Cypress (`window:confirm`): se comprueba que la página lo pide con el texto correcto y que respeta la respuesta, no cómo se ve el cuadro.
- Cypress dispara eventos del navegador; no es una persona con teclado y ratón. La accesibilidad, el uso solo con teclado y el móvil requieren otras pruebas (Lighthouse/axe, revisión manual).

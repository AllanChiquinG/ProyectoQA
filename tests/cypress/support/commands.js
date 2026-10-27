// Acciones de apoyo. El inicio de sesión se hace SIEMPRE por la interfaz; la API solo se usa para preparar y limpiar datos.
// Cypress 16: los valores no sensibles van en "expose" (cypress.config.js) y los sensibles en cy.env().

const apiUrl = () => Cypress.expose('apiUrl');

/** Contraseña de los usuarios de desarrollo (Seed:DevPassword), desde la variable CYPRESS_MEDICORE_PASSWORD. */
const devPassword = () =>
  cy.env(['MEDICORE_PASSWORD']).then(({ MEDICORE_PASSWORD }) => {
    if (!MEDICORE_PASSWORD) throw new Error('Define la contraseña de los usuarios de desarrollo: CYPRESS_MEDICORE_PASSWORD=<valor de Seed:DevPassword>');
    return MEDICORE_PASSWORD;
  });

/** Inicia sesión escribiendo en el formulario y pulsando el botón, y conserva la sesión entre pruebas. */
Cypress.Commands.add('loginUi', (email) => {
  cy.session(['ui', email], () => {
    cy.visit('/Login');
    cy.get('input[type=email]').type(email);
    devPassword().then((password) => cy.get('input[type=password]').type(password, { log: false }));
    cy.contains('button', 'Iniciar sesión').click();
    cy.location('pathname').should('eq', '/');
  });
});

/** Token de la API (solo para preparar y limpiar datos; no se usa para las acciones que se prueban). */
Cypress.Commands.add('apiToken', (email) =>
  devPassword().then((password) =>
    cy.request('POST', `${apiUrl()}/api/auth/login`, { email, password }).its('body.accessToken')));

Cypress.Commands.add('api', (token, method, path, body) =>
  cy.request({ method, url: `${apiUrl()}${path}`, body, headers: { Authorization: `Bearer ${token}` }, failOnStatusCode: false }));

/**
 * Captura de evidencia. Solo en navegadores reales: la captura de Electron (obsoleto en Cypress 16) se cuelga de forma
 * intermitente y haría fallar pruebas funcionales que sí pasaron. Las aserciones se ejecutan siempre.
 */
export const snap = (name) => {
  if (Cypress.browser.name === 'electron') return;
  cy.screenshot(name, { capture: 'viewport' });
};

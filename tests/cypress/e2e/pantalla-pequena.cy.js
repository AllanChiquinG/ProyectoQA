import { assertControlsLabelled, assertNoHorizontalOverflow } from '../support/a11y';

/**
 * Transversal · pantalla pequeña (375 × 812): el inicio de cada rol, el inicio de sesión y las pantallas de pacientes no ensanchan la página, y el
 * menú de cada rol sigue alcanzable. Las demás pantallas se comprueban a 375 px en el spec de su módulo.
 */
const ROLES = {
  Recepción: { email: 'elena.vargas@example.com', menu: 'Agenda de citas' },
  Administrador: { email: 'admin@example.com', menu: 'Usuarios y roles' },
  Médico: { email: 'lucia.mendez@example.com', menu: 'Mi agenda' },
  Laboratorio: { email: 'daniela.reyes@example.com', menu: 'Resultados' },
};

describe('Pantalla pequeña (375 px)', () => {
  beforeEach(() => cy.viewport(375, 812));

  it('1. el inicio de sesión cabe y sus campos tienen etiqueta', () => {
    cy.visit('/Login');
    assertNoHorizontalOverflow('Login');
    assertControlsLabelled();
    cy.contains('button', 'Iniciar sesión').should('be.visible');
  });

  Object.entries(ROLES).forEach(([rol, { email, menu }]) => {
    it(`2. ${rol}: el inicio no desborda y el menú se puede recorrer`, () => {
      cy.loginUi(email);
      cy.visit('/');
      assertNoHorizontalOverflow(`inicio de ${rol}`);
      cy.contains('nav a.nav', menu).scrollIntoView().should('be.visible').click();
      assertNoHorizontalOverflow(`pantalla ${menu}`);
    });
  });

  it('3. Recepción: pacientes y alta de paciente no desbordan y las etiquetas están completas', () => {
    cy.loginUi('elena.vargas@example.com');
    ['/Pacientes', '/Pacientes/Formulario'].forEach((ruta) => {
      cy.visit(ruta);
      assertNoHorizontalOverflow(ruta);
      assertControlsLabelled();
    });
  });

  it('4. el aviso de acceso restringido cabe', () => {
    cy.loginUi('elena.vargas@example.com');
    cy.visit('/Usuarios', { failOnStatusCode: false });
    assertNoHorizontalOverflow('acceso restringido');
  });
});

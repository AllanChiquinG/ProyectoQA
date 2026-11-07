import { snap } from '../support/screenshots';

/**
 * Recepción · pacientes (WEB-05 y WEB-06): lista, búsqueda, paginación, registro con validaciones, edición, teclado, etiquetas y pantalla pequeña.
 * Datos aislados: pacientes "Cypress…" con identificación CYP-E2E-W*; la tarea db:cleanE2eWebPatients los borra al empezar y al terminar
 * (solo esos). Todas las acciones del usuario van por la interfaz; la API solo prepara los 22 pacientes de la paginación.
 */
const RECEPCION = 'elena.vargas@example.com';
const PREFIJO_PAGINACION = 'Cypress Paginación';
const idPag = (n) => `CYP-E2E-W-PG-${String(n).padStart(2, '0')}`;

const BASE = { nombre: `Cypress Alta ${Date.now().toString(36)}`, identificacion: `CYP-E2E-W-AL-${Date.now().toString(36).toUpperCase()}`, nacimiento: '1991-04-20', telefono: '5550 1234', correo: 'alta.cypress@example.com' };

const llenar = (datos) => {
  if (datos.nombre !== undefined) cy.get('input[name="Input.FullName"]').clear().type(datos.nombre || '{backspace}', { parseSpecialCharSequences: false });
  if (datos.identificacion !== undefined) cy.get('input[name="Input.IdentificationNumber"]').clear().type(datos.identificacion || '{backspace}', { parseSpecialCharSequences: false });
  if (datos.nacimiento !== undefined) cy.get('input[name="Input.BirthDate"]').type(datos.nacimiento);
  if (datos.telefono !== undefined) cy.get('input[name="Input.Phone"]').clear().type(datos.telefono || '{backspace}');
  if (datos.correo !== undefined) cy.get('input[name="Input.Email"]').clear().type(datos.correo || '{backspace}');
};

const buscar = (texto) => {
  cy.get('input[name=q]').clear();
  if (texto) cy.get('input[name=q]').type(texto);
  cy.contains('button', 'Buscar').click();
};

describe('Recepción · pacientes', () => {
  before(() => {
    cy.task('db:cleanE2eWebPatients');
    cy.apiToken(RECEPCION).then((token) => {
      for (let n = 1; n <= 22; n += 1) {
        cy.api(token, 'POST', '/api/patients', {
          fullName: `${PREFIJO_PAGINACION} ${String(n).padStart(2, '0')}`, identificationNumber: idPag(n), birthDate: '1980-01-01', phone: '5550 0000', email: `pag${n}@example.com`,
        }).its('status').should('eq', 201);
      }
    });
  });

  after(() => {
    cy.task('db:cleanE2eWebPatients');
  });

  beforeEach(() => {
    cy.loginUi(RECEPCION);
  });

  describe('Lista y búsqueda (WEB-05)', () => {
    it('1. muestra el encabezado, el contador, las columnas y las acciones de Recepción', () => {
      cy.visit('/Pacientes');
      cy.contains('h1', 'Gestión de pacientes');
      cy.contains('.sub', 'Información general, organizada y a tu alcance.');
      cy.contains('a.btn', 'Registrar paciente').should('have.attr', 'href').and('include', '/Pacientes/Formulario');
      cy.get('thead th').then(($th) => expect([...$th].map((t) => t.textContent.trim())).to.deep.equal(['Paciente', 'Identificación', 'Contacto', 'Acciones']));
      cy.get('tbody tr').should('have.length.greaterThan', 0).first().within(() => {
        cy.get('.avatar').should('not.be.empty');
        cy.contains('a.link', 'Consultar / editar');
      });
      cy.get('.toolbar .sub').invoke('text').should('match', /^\d+ pacientes?$/);
      snap('43-pacientes-lista');
    });

    it('2. busca por nombre (sin distinguir mayúsculas), por identificación y por parte del texto', () => {
      cy.visit('/Pacientes');
      buscar('cypress paginación 07');
      cy.get('tbody tr').should('have.length', 1).and('contain', 'Cypress Paginación 07').and('contain', idPag(7));
      cy.get('.toolbar .sub').should('have.text', '1 paciente');
      buscar(idPag(12));
      cy.get('tbody tr').should('have.length', 1).and('contain', 'Cypress Paginación 12');
      buscar('Paginación 0');
      cy.get('tbody tr').should('have.length', 9);
    });

    it('3. una búsqueda sin resultados lo dice, y los caracteres especiales no rompen la pantalla', () => {
      cy.visit('/Pacientes');
      buscar('zzz-no-existe-zzz');
      cy.get('tbody tr').should('not.exist');
      cy.contains('.emptymsg', 'No se encontraron pacientes.');
      cy.get('.toolbar .sub').should('have.text', '0 pacientes');
      buscar('100%_\\[x]');
      cy.contains('.emptymsg', 'No se encontraron pacientes.');
      buscar('');
      cy.get('tbody tr').should('have.length.greaterThan', 5);
    });

    it('4. la paginación recorre las páginas, conserva la búsqueda y deshabilita lo que no aplica', () => {
      cy.visit('/Pacientes');
      buscar(PREFIJO_PAGINACION);
      cy.get('.toolbar .sub').should('have.text', '22 pacientes');
      cy.get('tbody tr').should('have.length', 20);
      cy.get('nav.pager').within(() => {
        cy.contains('Página 1');
        cy.contains('a', '← Anterior').should('not.exist');
        cy.contains('a', 'Siguiente →').click();
      });
      cy.location('search').should('contain', 'pageNumber=2').and('contain', 'q=Cypress');
      cy.get('input[name=q]').should('have.value', PREFIJO_PAGINACION);
      cy.get('tbody tr').should('have.length', 2);
      cy.get('nav.pager').within(() => {
        cy.contains('Página 2');
        cy.contains('a', 'Siguiente →').should('not.exist');
        cy.contains('a', '← Anterior').click();
      });
      cy.get('tbody tr').should('have.length', 20);
      snap('44-pacientes-paginacion');
    });
  });

  describe('Registro (WEB-06)', () => {
    it('5. el formulario tiene etiquetas, marca los obligatorios, y el teclado recorre los campos en orden', () => {
      cy.visit('/Pacientes/Formulario');
      cy.contains('h1', 'Registrar paciente');
      cy.contains('.sub', 'Los campos marcados con * son obligatorios.');
      ['Nombre completo *', 'Identificación *', 'Fecha de nacimiento *', 'Teléfono *', 'Correo electrónico *'].forEach((etiqueta) => cy.contains('label.field', etiqueta).find('input').should('exist'));
      cy.get('input[name="Input.BirthDate"]').should('have.attr', 'max').and('match', /^\d{4}-\d{2}-\d{2}$/);
      // Orden de Tab. (El selector de fecha de Chrome tiene sus propias paradas internas: día, mes y año; por eso se continúa desde el teléfono.)
      cy.get('input[name="Input.FullName"]').focus();
      cy.press(Cypress.Keyboard.Keys.TAB);
      cy.focused().should('have.attr', 'name', 'Input.IdentificationNumber');
      cy.press(Cypress.Keyboard.Keys.TAB);
      cy.focused().should('have.attr', 'name', 'Input.BirthDate');
      cy.get('input[name="Input.Phone"]').focus();
      cy.press(Cypress.Keyboard.Keys.TAB);
      cy.focused().should('have.attr', 'name', 'Input.Email');
      cy.press(Cypress.Keyboard.Keys.TAB);
      cy.focused().should('contain', 'Cancelar');
      cy.press(Cypress.Keyboard.Keys.TAB);
      cy.focused().should('contain', 'Guardar paciente');
    });

    it('6. un formulario vacío muestra los errores en español junto a cada campo y no guarda', () => {
      cy.visit('/Pacientes/Formulario');
      cy.contains('button', 'Guardar paciente').click();
      cy.location('pathname').should('eq', '/Pacientes/Formulario');
      cy.get('.field .err').filter(':visible').should('have.length.at.least', 4).each(($e) => expect($e.text()).to.match(/obligatorio|válid/i));
      snap('45-paciente-errores');
    });

    it('7. valida correo, teléfono y fecha de nacimiento, y conserva lo que ya se escribió', () => {
      cy.visit('/Pacientes/Formulario');
      llenar({ nombre: 'Prueba Validación', identificacion: 'CYP-E2E-W-VAL', nacimiento: '2999-01-01', telefono: 'abc', correo: 'sin-arroba' });
      cy.contains('button', 'Guardar paciente').click();
      cy.get('.err').filter(':visible').then(($e) => {
        const texto = [...$e].map((e) => e.textContent).join(' | ');
        expect(texto).to.match(/correo/i).and.to.match(/teléfono/i).and.to.match(/nacimiento/i);
      });
      cy.get('input[name="Input.FullName"]').should('have.value', 'Prueba Validación');
      cy.get('input[name="Input.Email"]').should('have.value', 'sin-arroba');
    });

    it('8. registra un paciente, lo avisa y aparece en la lista; repetir la identificación se rechaza con un mensaje claro', () => {
      cy.visit('/Pacientes/Formulario');
      llenar(BASE);
      cy.contains('button', 'Guardar paciente').click();
      cy.location('pathname').should('eq', '/Pacientes');
      cy.get('#toast, [role=status]').should('contain', 'Datos del paciente guardados');
      buscar(BASE.identificacion);
      cy.get('tbody tr').should('have.length', 1).and('contain', BASE.nombre).and('contain', BASE.telefono).and('contain', BASE.correo);

      cy.visit('/Pacientes/Formulario');
      llenar({ ...BASE, nombre: `${BASE.nombre} (repetido)`, identificacion: BASE.identificacion.toLowerCase() });
      cy.contains('button', 'Guardar paciente').click();
      cy.location('pathname').should('eq', '/Pacientes/Formulario');
      cy.contains('.err', 'Ya existe un paciente con esa identificación.');
      cy.get('input[name="Input.FullName"]').should('have.value', `${BASE.nombre} (repetido)`);
      snap('46-paciente-duplicado');
    });
  });

  describe('Edición (WEB-06)', () => {
    it('9. abre el paciente desde la lista, cambia el teléfono y lo ve reflejado; Cancelar no guarda', () => {
      cy.visit('/Pacientes');
      buscar(BASE.identificacion);
      cy.contains('tr', BASE.nombre).contains('a.link', 'Consultar / editar').click();
      cy.contains('h1', 'Datos del paciente');
      cy.get('input[name="Input.FullName"]').should('have.value', BASE.nombre);
      cy.get('input[name="Input.BirthDate"]').should('have.value', BASE.nacimiento);

      cy.get('input[name="Input.Phone"]').clear().type('5559 9999');
      cy.contains('a.btn', 'Cancelar').click();
      cy.location('pathname').should('eq', '/Pacientes');
      buscar(BASE.identificacion);
      cy.get('tbody tr').should('contain', BASE.telefono).and('not.contain', '5559 9999');

      cy.contains('tr', BASE.nombre).contains('a.link', 'Consultar / editar').click();
      cy.get('input[name="Input.Phone"]').clear().type('5559 9999');
      cy.contains('button', 'Guardar paciente').click();
      cy.get('#toast, [role=status]').should('contain', 'Datos del paciente guardados');
      buscar(BASE.identificacion);
      cy.get('tbody tr').should('contain', '5559 9999');
    });

    it('10. al editar, un dato inválido muestra el error y no cambia el registro', () => {
      cy.visit('/Pacientes');
      buscar(BASE.identificacion);
      cy.contains('tr', BASE.nombre).contains('a.link', 'Consultar / editar').click();
      cy.get('input[name="Input.FullName"]').clear();
      cy.get('input[name="Input.Email"]').clear().type('correo-malo');
      cy.contains('button', 'Guardar paciente').click();
      cy.location('pathname').should('contain', '/Pacientes/Formulario');
      cy.get('.err').filter(':visible').should('have.length.at.least', 2);
      cy.visit('/Pacientes');
      buscar(BASE.identificacion);
      cy.get('tbody tr').should('contain', BASE.nombre).and('contain', BASE.correo);
    });

    it('11. un paciente que no existe en la dirección se informa con la página de "no encontrado" sin romper la pantalla', () => {
      cy.visit('/Pacientes/Formulario/00000000-0000-0000-0000-000000000001', { failOnStatusCode: false });
      cy.contains('h2', 'No encontramos lo que buscas');
      cy.contains('a.btn', 'Volver al inicio');
    });
  });

  describe('Pantalla pequeña', () => {
    beforeEach(() => {
      cy.viewport(375, 812);
    });

    it('12. la lista y el formulario se adaptan a 375 px: sin desbordar la página y con el menú disponible', () => {
      ['/Pacientes', '/Pacientes/Formulario'].forEach((ruta) => {
        cy.visit(ruta);
        cy.document().then((doc) => expect(doc.documentElement.scrollWidth, `ancho de ${ruta}`).to.be.at.most(doc.documentElement.clientWidth + 1));
        cy.contains('nav a.nav', 'Pacientes').should('be.visible');
        cy.contains('nav a.nav', 'Agenda de citas').should('be.visible');
      });
      cy.visit('/Pacientes');
      cy.get('table').should('exist');
      snap('47-pacientes-movil');
    });
  });
});

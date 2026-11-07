import { assertControlsLabelled, assertNoHorizontalOverflow } from '../support/a11y';
import { addDays, clinicToday } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Laboratorio · listado, búsqueda y paginación (WEB-16), validaciones del registro (WEB-17), detalle y "Reintentar notificación" (WEB-18).
 * Complementa a laboratorio-resultados.cy.js (registro y notificación de extremo a extremo) sin repetirlo.
 *
 * Datos aislados: paciente CYP-E2E-W-LB-1 y exámenes "Cypress Lab NN" (la tarea db:cleanE2eWebPatients borra paciente, resultados y notificaciones
 * al empezar y al terminar). Los 22 resultados de la paginación se preparan por la API; el resultado "pendiente" se inserta sin evento.
 */
const LAB = 'daniela.reyes@example.com';
const RECEPCION = 'elena.vargas@example.com';
const PACIENTE = { nombre: 'Cypress Laboratorio Uno', id: 'CYP-E2E-W-LB-1' };
const ctx = {};
const ok = (r, que) => { expect(r.status, que).to.be.oneOf([200, 201, 204]); return r.body; };
const examen = (n) => `Cypress Lab ${String(n).padStart(2, '0')}`;

describe('Laboratorio · listado, registro y detalle', () => {
  before(() => {
    cy.task('db:cleanE2eWebPatients');
    cy.apiToken(RECEPCION).then((rec) => cy.api(rec, 'POST', '/api/patients',
      { fullName: PACIENTE.nombre, identificationNumber: PACIENTE.id, birthDate: '1980-02-02', phone: '5550 8888', email: 'lab.uno@example.com' })
      .then((c) => { ctx.patientId = ok(c, 'paciente').id; }));
    cy.apiToken(LAB).then((lab) => {
      for (let n = 1; n <= 22; n += 1) {
        cy.then(() => cy.api(lab, 'POST', '/api/labs/results', {
          patientId: ctx.patientId, examType: examen(n), performedOn: addDays(clinicToday(), -1), notes: null,
          parameters: [{ name: 'Glucosa', value: '90', unit: 'mg/dL', referenceRange: '70-100' }],
        }).its('status').should('eq', 201));
      }
    });
    cy.then(() => cy.task('db:createPendingLabResult', { patientId: ctx.patientId, examType: 'Cypress Lab Pendiente' })).then((id) => { ctx.pendingId = id; });
  });

  after(() => {
    cy.task('db:cleanE2eWebPatients');
  });

  beforeEach(() => cy.loginUi(LAB));

  describe('Listado', () => {
    it('1. busca por examen y por paciente, informa el total y avisa cuando no hay resultados', () => {
      cy.visit('/Laboratorio');
      cy.contains('h1', 'Resultados de laboratorio');
      assertControlsLabelled();
      cy.get('input[name=q]').type('Cypress Lab 07');
      cy.contains('button', 'Buscar').click();
      cy.get('tbody tr').should('have.length', 1).and('contain', PACIENTE.nombre).find('[data-notification]').invoke('text').should('match', /^(Enviada|Pendiente)$/); // la notificación es asíncrona: el estado depende del momento
      cy.contains('.sub', '1 resultado');
      cy.get('input[name=q]').clear().type('Laboratorio Uno');
      cy.contains('button', 'Buscar').click();
      cy.contains('.sub', '23 resultados');
      cy.get('input[name=q]').clear().type('zzzz-sin-resultados');
      cy.contains('button', 'Buscar').click();
      cy.contains('.emptymsg', 'No se encontraron resultados.');
      cy.get('.pager').should('not.exist');
    });

    it('2. pagina de 20 en 20 conservando la búsqueda (Anterior y Siguiente)', () => {
      cy.visit('/Laboratorio?q=Laboratorio%20Uno');
      cy.get('tbody tr').should('have.length', 20);
      cy.get('.pager').should('contain', 'Página 1');
      cy.contains('.pager a', 'Anterior').should('not.exist');
      cy.contains('.pager a', 'Siguiente').click();
      cy.location('search').should('include', 'pageNumber=2').and('include', 'q=');
      cy.get('tbody tr').should('have.length', 3);
      cy.get('.pager').should('contain', 'Página 2');
      cy.get('input[name=q]').should('have.value', 'Laboratorio Uno');
      cy.contains('.pager a', 'Siguiente').should('not.exist');
      cy.contains('.pager a', 'Anterior').click();
      cy.get('tbody tr').should('have.length', 20);
    });

    it('3. "Ver resultado" abre el detalle con los parámetros y "Volver" regresa al listado', () => {
      cy.visit('/Laboratorio?q=Cypress%20Lab%2003');
      cy.contains('tr', examen(3)).contains('a.link', 'Ver resultado').click();
      cy.location('pathname').should('match', /^\/Laboratorio\/Resultado\/[0-9a-f-]{36}$/);
      cy.contains('h2', examen(3));
      cy.contains('tr', 'Glucosa').should('contain', '90').and('contain', 'mg/dL').and('contain', '70-100');
      cy.contains('p', 'Sin observaciones.');
      cy.contains('a.btn', 'Volver').click();
      cy.location('pathname').should('eq', '/Laboratorio');
    });
  });

  describe('Registro', () => {
    it('4. valida paciente, examen, fecha y parámetro; una fecha futura se rechaza con un mensaje claro', () => {
      cy.visit('/Laboratorio/Registrar');
      assertControlsLabelled();
      cy.get('input[name="Input.PerformedOn"]').should('have.attr', 'max', clinicToday());
      cy.contains('button', 'Guardar resultado').click();
      cy.get('.field .err').filter(':visible').should('have.length.at.least', 4).and('contain', 'Este campo es obligatorio.');

      cy.get('input[name=q]').type(PACIENTE.id);
      cy.contains('button', 'Filtrar').click();
      cy.get('select[name="Input.PatientId"]').select(`${PACIENTE.nombre} · ${PACIENTE.id}`);
      cy.get('input[name="Input.ExamType"]').type('Cypress Lab Futuro');
      cy.get('input[name="Input.PerformedOn"]').type(addDays(clinicToday(), 3));
      cy.get('input[name="Parameters[0].Name"]').type('Glucosa');
      cy.get('input[name="Parameters[0].Value"]').type('95');
      cy.contains('button', 'Guardar resultado').click();
      cy.contains('.err', 'La fecha del examen no puede ser futura.');
      cy.get('input[name="Input.ExamType"]').should('have.value', 'Cypress Lab Futuro'); // lo escrito se conserva
      cy.location('pathname').should('eq', '/Laboratorio/Registrar');
    });

    it('5. el filtro de pacientes acota la lista; sin coincidencias solo queda "Selecciona un paciente…"', () => {
      cy.visit('/Laboratorio/Registrar');
      cy.get('input[name=q]').type('zzzz-ninguno');
      cy.contains('button', 'Filtrar').click();
      cy.get('select[name="Input.PatientId"] option').should('have.length', 1);
      cy.get('input[name=q]').clear().type(PACIENTE.id);
      cy.contains('button', 'Filtrar').click();
      cy.get('select[name="Input.PatientId"] option').should('have.length', 2);
    });

    it('6. permite hasta 10 parámetros y quitar los que sobran; con uno solo no se puede quitar', () => {
      cy.visit('/Laboratorio/Registrar');
      cy.get('fieldset.parametro').should('have.length', 1);
      cy.contains('button', 'Quitar este parámetro').should('not.exist');
      cy.get('input[name="Parameters[0].Name"]').type('Hemoglobina');
      for (let n = 2; n <= 10; n += 1) {
        cy.contains('button', 'Agregar parámetro').click();
        cy.get('fieldset.parametro').should('have.length', n);
      }
      cy.contains('button', 'Agregar parámetro').should('not.exist');
      cy.get('input[name="Parameters[0].Name"]').should('have.value', 'Hemoglobina');
      cy.get('fieldset.parametro').eq(4).contains('button', 'Quitar este parámetro').click();
      cy.get('fieldset.parametro').should('have.length', 9);
      cy.contains('a.btn', 'Cancelar').click();
      cy.location('pathname').should('eq', '/Laboratorio');
    });
  });

  describe('Notificación pendiente', () => {
    it('7. un resultado sin notificación se ve "Pendiente" y "Reintentar notificación" la reprograma', () => {
      cy.visit('/Laboratorio?q=Lab%20Pendiente');
      cy.get('tbody tr').should('have.length', 1).find('[data-notification=pendiente]').should('have.text', 'Pendiente');
      cy.visit(`/Laboratorio/Resultado/${ctx.pendingId}`);
      cy.get('#estadoNotificacion').should('contain', 'pendiente de envío');
      cy.get('#retryNotification').click();
      cy.get('#toast').should('contain', 'Notificación reprogramada');
      cy.location('pathname').should('eq', `/Laboratorio/Resultado/${ctx.pendingId}`);
    });
  });

  describe('Pantalla pequeña', () => {
    it('8. listado, registro y detalle no desbordan a 375 px', () => {
      cy.viewport(375, 812);
      ['/Laboratorio', '/Laboratorio/Registrar', `/Laboratorio/Resultado/${ctx.pendingId}`].forEach((ruta) => {
        cy.visit(ruta);
        assertNoHorizontalOverflow(ruta);
      });
      snap('55-laboratorio-movil');
    });
  });
});

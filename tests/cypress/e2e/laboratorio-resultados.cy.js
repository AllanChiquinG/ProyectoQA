import { snap } from '../support/screenshots';

/**
 * Recorrido de Laboratorio (WEB-16 a WEB-18) y lectura del Médico desde el expediente, con notificación asíncrona real
 * (outbox → RabbitMQ → consumidor → notificación interna del paciente). Requiere RabbitMQ en marcha (docker compose up -d).
 *
 * Aislamiento: usa el médico dedicado e2e.medico y el paciente "Cypress Paciente Laboratorio" (CYP-E2E-003); la tarea
 * db:cleanE2eClinical borra al empezar y al terminar sus resultados, notificaciones y eventos. No toca datos de uso normal.
 * Todas las acciones del usuario van por la interfaz; la API solo prepara datos y comprueba la autorización en el servidor.
 * El caso "RabbitMQ detenido" no se automatiza aquí (Cypress no controla Docker): se demuestra con scripts/evidencia-laboratorio.sh
 * y con las pruebas de integración (MessagingTests).
 */
const LAB = 'daniela.reyes@example.com';
const MEDICO = 'e2e.medico@example.com';
const COLEGA = 'e2e.colega@example.com';
const RECEPCION = 'elena.vargas@example.com';
const PATIENT = { name: 'Cypress Paciente Laboratorio', identification: 'CYP-E2E-003' };

const RUN = Date.now().toString(36);
const EXAM = `Hematología E2E ${RUN}`;
const NOTES = `Observaciones E2E ${RUN}`;
const PARAMS = [
  { name: 'Hemoglobina', value: '13.2', unit: 'g/dL', range: '12.0–16.0' },
  { name: 'Leucocitos', value: '6500', unit: '/µL', range: '4500–11000' },
];

const ctx = {};

const ok = (response, what) => {
  expect(response.status, what).to.be.oneOf([200, 201, 204]);
  return response.body;
};

/** Recarga la página hasta que aparezca el texto (la notificación se genera de forma asíncrona, normalmente en menos de un segundo). */
const reloadUntil = (selector, text, attempts = 20) => {
  const check = (left) => cy.get('body').then(($body) => {
    if ($body.find(selector).text().includes(text)) return;
    if (left === 0) throw new Error(`No apareció "${text}" en ${selector} tras ${attempts} intentos.`);
    cy.wait(1000);
    cy.reload();
    check(left - 1);
  });
  check(attempts);
};

describe('Laboratorio · registrar, consultar y notificar un resultado', () => {
  before(() => {
    cy.task('db:cleanE2eClinical');
    cy.apiToken(MEDICO).then((medico) => cy.api(medico, 'GET', '/api/doctors/me').then((me) => { ctx.doctorId = ok(me, 'médico de prueba').id; }));
    cy.apiToken(RECEPCION).then((recepcion) => cy.api(recepcion, 'GET', `/api/patients?q=${PATIENT.identification}`).then((search) => {
      const found = ok(search, 'buscar paciente').items.find((p) => p.identificationNumber === PATIENT.identification);
      if (found) { ctx.patientId = found.id; return; }
      cy.api(recepcion, 'POST', '/api/patients', {
        fullName: PATIENT.name, identificationNumber: PATIENT.identification, birthDate: '1990-01-01', phone: '5550 1234', email: 'cypress@example.com',
      }).then((created) => { ctx.patientId = ok(created, 'crear paciente').id; });
    }));
    // Relación de atención válida del médico con el paciente: una cita ya pasada (no depende de la hora del día).
    cy.then(() => cy.task('db:createPastAppointment', { doctorId: ctx.doctorId, patientId: ctx.patientId }));
  });

  after(() => {
    cy.task('db:cleanE2eClinical');
  });

  describe('Laboratorio', () => {
    beforeEach(() => {
      cy.loginUi(LAB);
    });

    it('1. inicia sesión como Laboratorio y abre "Resultados de laboratorio" desde el menú', () => {
      cy.visit('/');
      cy.contains('.user', 'Laboratorio');
      cy.contains('nav a.nav', 'Resultados de laboratorio').click();
      cy.location('pathname').should('eq', '/Laboratorio');
      cy.contains('h1', 'Resultados de laboratorio');
      cy.contains('th', 'Notificación');
      snap('30-laboratorio-resultados');
    });

    it('2. no guarda un resultado incompleto y muestra los errores', () => {
      cy.visit('/Laboratorio/Registrar');
      cy.contains('button', 'Guardar resultado').click();
      cy.get('.err').should('contain', 'Este campo es obligatorio.');
      cy.location('pathname').should('eq', '/Laboratorio/Registrar');
      snap('31-laboratorio-errores');
    });

    it('3. registra un resultado con dos parámetros escribiendo en el formulario', () => {
      cy.visit('/Laboratorio/Registrar');
      cy.get('input[name=q]').type(PATIENT.identification);
      cy.contains('button', 'Filtrar').click();
      cy.get('select[name="Input.PatientId"]').select(`${PATIENT.name} · ${PATIENT.identification}`);
      cy.get('input[name="Input.ExamType"]').type(EXAM);
      cy.get('input[name="Parameters[0].Name"]').type(PARAMS[0].name);
      cy.get('input[name="Parameters[0].Value"]').type(PARAMS[0].value);
      cy.get('input[name="Parameters[0].Unit"]').type(PARAMS[0].unit);
      cy.get('input[name="Parameters[0].ReferenceRange"]').type(PARAMS[0].range);

      cy.contains('button', 'Agregar parámetro').click(); // conserva lo escrito
      cy.get('input[name="Parameters[0].Name"]').should('have.value', PARAMS[0].name);
      cy.get('input[name="Input.ExamType"]').should('have.value', EXAM);
      cy.get('input[name="Parameters[1].Name"]').type(PARAMS[1].name);
      cy.get('input[name="Parameters[1].Value"]').type(PARAMS[1].value);
      cy.get('input[name="Parameters[1].Unit"]').type(PARAMS[1].unit);
      cy.get('input[name="Parameters[1].ReferenceRange"]').type(PARAMS[1].range);
      cy.get('textarea[name="Input.Notes"]').type(NOTES);
      cy.contains('.notice', 'El resultado quedará guardado aunque la notificación esté pendiente.');
      snap('32-laboratorio-formulario');

      cy.contains('button', 'Guardar resultado').click();

      // WEB-18: detalle del resultado recién guardado.
      cy.location('pathname').should('match', /^\/Laboratorio\/Resultado\/[0-9a-f-]{36}$/);
      cy.location('pathname').then((path) => { ctx.resultId = path.split('/').pop(); });
      cy.contains('h2', EXAM);
      cy.contains('.sub', PATIENT.name);
      PARAMS.forEach((p) => cy.contains('tr', p.name).should('contain', p.value).and('contain', p.unit).and('contain', p.range));
      cy.contains('p', NOTES);
      cy.get('#estadoNotificacion').should('contain', 'Resultado guardado');
    });

    it('4. la notificación al paciente se genera de forma asíncrona y pasa a "enviada"', () => {
      cy.visit(`/Laboratorio/Resultado/${ctx.resultId}`);
      reloadUntil('#estadoNotificacion', 'enviada al paciente');
      cy.get('#retryNotification').should('not.exist'); // ya enviada: no hay nada que reintentar
      snap('33-laboratorio-detalle-enviada');
    });

    it('5. el listado muestra el resultado con su notificación "Enviada" y permite buscarlo', () => {
      cy.visit('/Laboratorio');
      cy.get('input[name=q]').type(PATIENT.name);
      cy.contains('button', 'Buscar').click();
      cy.contains('tr.lab-row', EXAM).should('contain', PATIENT.name).find('[data-notification=enviada]').should('contain', 'Enviada');
      snap('34-laboratorio-listado');
      cy.contains('tr.lab-row', EXAM).contains('a', 'Ver resultado').click();
      cy.location('pathname').should('eq', `/Laboratorio/Resultado/${ctx.resultId}`);
    });
  });

  describe('Médico (expediente)', () => {
    it('6. el médico con relación de atención ve el resultado en la pestaña Laboratorio y abre su detalle', () => {
      cy.loginUi(MEDICO);
      cy.visit(`/Pacientes/Expediente/${ctx.patientId}?tab=Laboratorio`);
      cy.contains('.tabs a.on', 'Laboratorio');
      cy.contains('.mobcard', EXAM).contains('a', 'Ver resultado').click();
      cy.location('pathname').should('eq', `/Laboratorio/Resultado/${ctx.resultId}`);
      cy.contains('h2', EXAM);
      PARAMS.forEach((p) => cy.contains('tr', p.name).should('contain', p.value));
      cy.get('#retryNotification').should('not.exist'); // el médico no reintenta notificaciones
      snap('35-medico-detalle-resultado');
      cy.contains('a.btn', 'Volver').click();
      cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx.patientId}`);
      cy.contains('.tabs a.on', 'Laboratorio');
    });

    it('7. un médico sin relación de atención no ve el resultado (404, sin confirmar que existe)', () => {
      cy.loginUi(COLEGA);
      cy.visit(`/Laboratorio/Resultado/${ctx.resultId}`, { failOnStatusCode: false });
      cy.contains('h2', 'No encontramos lo que buscas');
      cy.contains(EXAM).should('not.exist');
      snap('36-medico-sin-relacion');
    });
  });

  describe('Otros roles', () => {
    it('8. Recepción no tiene acceso a los resultados de laboratorio', () => {
      cy.loginUi(RECEPCION);
      cy.visit('/Laboratorio', { failOnStatusCode: false });
      cy.contains('h2', 'No tienes acceso a esta página');
      cy.visit(`/Laboratorio/Resultado/${ctx.resultId}`, { failOnStatusCode: false });
      cy.contains('h2', 'No tienes acceso a esta página');
      cy.contains(EXAM).should('not.exist');
    });
  });
});

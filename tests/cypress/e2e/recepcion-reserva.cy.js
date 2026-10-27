import { addDays, clinicToday, firstOfMonth, monthLabel } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Recorrido de Recepción (WEB-01, 02, 03, 04): reservar, ver detalle, cancelar con y sin confirmación,
 * liberar el horario, conflicto de horario y calendario.
 *
 * Todas las acciones del usuario se hacen por la interfaz (escribir, elegir, pulsar). La API solo prepara y limpia datos
 * (médico y pacientes de prueba, citas del día) y provoca el conflicto con "otra recepcionista".
 * Los datos de prueba tienen nombres fijos para no acumular registros entre ejecuciones.
 */
const RECEPCION = 'elena.vargas@example.com';
const ADMIN = 'admin@example.com';
const DOCTOR = 'Dr. Cypress E2E';
const PATIENTS = [
  { name: 'Cypress Paciente Uno', identification: 'CYP-E2E-001' },
  { name: 'Cypress Paciente Dos', identification: 'CYP-E2E-002' },
];

const DATE = addDays(clinicToday(), 5); // fecha futura de atención (el médico de prueba atiende todos los días)
const ctx = {}; // datos compartidos entre las pruebas ordenadas del recorrido

const apiBody = (response, what) => {
  expect(response.status, what).to.be.oneOf([200, 201, 204]);
  return response.body;
};

/** Cancela, por la API, las citas activas del médico de prueba en la fecha (para poder repetir la ejecución). */
const cleanAgenda = () =>
  cy.apiToken(RECEPCION).then((token) =>
    cy.api(token, 'GET', `/api/appointments?doctorId=${ctx.doctorId}&date=${DATE}`).then((agenda) => {
      (agenda.body || []).forEach((a) => cy.api(token, 'POST', `/api/appointments/${a.id}/cancel`));
    }));

const fillAgendaFilters = () => {
  cy.visit('/Agenda');
  cy.get('select[name=doctorId]').select(DOCTOR); // cambio real: la página envía el filtro sola
  cy.location('search').should('include', `doctorId=${ctx.doctorId}`);
  cy.get('input[name=date]').type(DATE); // al completar la fecha, la página envía el filtro sola
  cy.location('search').should('include', `date=${DATE}`);
};

describe('Recepción · reservar, consultar y cancelar citas', () => {
  before(() => {
    // Preparación de datos por la API (no es lo que se prueba).
    cy.apiToken(ADMIN).then((admin) => {
      cy.api(admin, 'GET', `/api/doctors?q=${encodeURIComponent('Cypress E2E')}`).then((list) => {
        const found = apiBody(list, 'listar médicos').find((d) => d.fullName === DOCTOR);
        const ready = found
          ? cy.wrap(found.id)
          : cy.api(admin, 'POST', '/api/doctors', { fullName: DOCTOR, specialty: 'Medicina general', phone: '5550 0000', email: 'cypress.e2e@example.com' })
              .then((created) => apiBody(created, 'crear médico').id);
        ready.then((doctorId) => {
          ctx.doctorId = doctorId;
          cy.api(admin, 'PUT', `/api/doctors/${doctorId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' })
            .then((r) => apiBody(r, 'horario del médico'));
        });
      });
    });
    cy.apiToken(RECEPCION).then((token) => {
      PATIENTS.forEach((patient, index) => {
        cy.api(token, 'GET', `/api/patients?q=${patient.identification}`).then((search) => {
          const found = apiBody(search, 'buscar paciente').items.find((p) => p.identificationNumber === patient.identification);
          if (found) { ctx[`patient${index}`] = found.id; return; }
          cy.api(token, 'POST', '/api/patients', {
            fullName: patient.name, identificationNumber: patient.identification, birthDate: '1990-01-01', phone: '5550 1234', email: `cypress${index}@example.com`,
          }).then((created) => { ctx[`patient${index}`] = apiBody(created, 'crear paciente').id; });
        });
      });
    });
    cy.then(cleanAgenda);
  });

  beforeEach(() => {
    cy.loginUi(RECEPCION);
  });

  after(() => {
    cy.then(cleanAgenda);
  });

  it('1. inicia sesión como Recepción escribiendo en el formulario y pulsando el botón', () => {
    cy.visit('/');
    cy.contains('.user', 'Elena Vargas').should('contain', 'Recepción');
    cy.contains('nav a.nav', 'Agenda de citas').should('be.visible');
    snap('01-sesion-recepcion');
  });

  it('2 y 3. elige médico, fecha, paciente y horario, y confirma con el botón de la interfaz', () => {
    cy.visit('/');
    cy.contains('nav a.nav', 'Agenda de citas').click();
    cy.location('pathname').should('eq', '/Agenda');

    fillAgendaFilters();
    cy.contains('.daytitle', DOCTOR);
    snap('02-agenda-del-medico');

    cy.contains('a.btn', 'Reservar cita').click();
    cy.location('pathname').should('eq', '/Agenda/Reservar');
    cy.get('#DoctorId').find('option:selected').should('have.text', DOCTOR);
    cy.get('#Date').should('have.value', DATE);

    // La fecha también se puede cambiar en el formulario: los horarios se actualizan solos (se espera cada recarga).
    cy.intercept('POST', '**/Agenda/Reservar?handler=Actualizar').as('actualizarHorarios');
    cy.get('#Date').type(addDays(DATE, 1));
    cy.wait('@actualizarHorarios');
    cy.get('#Date').should('have.value', addDays(DATE, 1));
    cy.get('#Date').type(DATE);
    cy.wait('@actualizarHorarios');
    cy.get('#Date').should('have.value', DATE);

    cy.get('#PatientId').select(PATIENTS[0].name);
    cy.wait('@actualizarHorarios');
    cy.get('#PatientId').find('option:selected').should('have.text', PATIENTS[0].name);

    cy.contains('.times label', '09:00').click();
    cy.get('input[name=Time][value="09:00"]').should('be.checked');
    snap('03-formulario-de-reserva');

    cy.contains('button', 'Confirmar cita').click();

    cy.location('pathname').should('match', /^\/Agenda\/Cita\/[0-9a-f-]{36}$/);
    cy.location('pathname').then((path) => { ctx.detailPath = path; });
    cy.get('#toast').should('contain', 'Cita confirmada correctamente');
    cy.contains('.summaryline', 'Paciente').should('contain', PATIENTS[0].name);
    cy.contains('.summaryline', 'Médico').should('contain', DOCTOR);
    cy.contains('.summaryline', 'Hora').should('contain', '09:00');
    cy.contains('.summaryline', 'Estado').should('contain', 'Confirmada');
    snap('04-cita-confirmada');
  });

  it('4. abre "Ver detalle" desde la agenda', () => {
    fillAgendaFilters();
    cy.contains('.slot', '09:00').within(() => {
      cy.contains(PATIENTS[0].name);
      cy.contains('a', 'Ver detalle').click();
    });
    cy.location('pathname').should('eq', ctx.detailPath);
    cy.contains('h2', 'Reserva de atención médica');
  });

  it('5a. cancelar: si se RECHAZA la confirmación, la cita se conserva', () => {
    cy.intercept('POST', '**/Agenda/Cita/**').as('cancelRequest');
    const confirmMessages = [];
    cy.on('window:confirm', (message) => { confirmMessages.push(message); return false; }); // el usuario pulsa "Cancelar" en el cuadro

    cy.visit(ctx.detailPath);
    cy.contains('button', 'Cancelar cita').click();

    cy.then(() => {
      expect(confirmMessages, 'cuadros de confirmación mostrados').to.deep.equal(['¿Confirmas que deseas cancelar esta cita?']);
    });
    cy.get('@cancelRequest.all').should('have.length', 0); // no se envió nada al servidor
    cy.location('pathname').should('eq', ctx.detailPath);
    cy.contains('.summaryline', 'Estado').should('contain', 'Confirmada');
    cy.contains('button', 'Cancelar cita').should('be.visible');

    // La cita sigue en la agenda.
    fillAgendaFilters();
    cy.contains('.slot', '09:00').should('contain', PATIENTS[0].name).and('contain', 'Confirmada');
  });

  it('5b. cancelar: si se ACEPTA la confirmación, la cita se cancela', () => {
    cy.intercept('POST', '**/Agenda/Cita/**').as('cancelRequest');
    const confirmMessages = [];
    cy.on('window:confirm', (message) => { confirmMessages.push(message); return true; }); // el usuario pulsa "Aceptar"

    cy.visit(ctx.detailPath);
    cy.contains('button', 'Cancelar cita').click();

    cy.wait('@cancelRequest').its('response.statusCode').should('be.oneOf', [200, 302]);
    cy.then(() => { expect(confirmMessages).to.have.length(1); });
    cy.get('#toast').should('contain', 'La cita fue cancelada');
    cy.contains('.summaryline', 'Estado').should('contain', 'Cancelada');
    cy.contains('button', 'Cancelar cita').should('not.exist');
    snap('05-cita-cancelada');
  });

  it('6. el horario cancelado vuelve a estar disponible', () => {
    fillAgendaFilters();
    cy.contains('.slot', '09:00').should('contain', '＋ Horario disponible').and('not.contain', PATIENTS[0].name);
    snap('06-horario-liberado');

    // Y se puede volver a reservar desde ese mismo horario.
    cy.contains('.slot', '09:00').contains('a', 'Horario disponible').click();
    cy.location('pathname').should('eq', '/Agenda/Reservar');
    cy.get('input[name=Time][value="09:00"]').should('not.be.disabled').and('be.checked');
  });

  it('7. un conflicto de horario muestra el aviso y conserva lo ingresado', () => {
    const takenTime = '10:00';
    cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${DATE}&time=${takenTime}`);
    cy.get('#PatientId').select(PATIENTS[0].name);
    cy.get('#PatientId').find('option:selected').should('have.text', PATIENTS[0].name);
    cy.get('input[name=Time][value="10:00"]').should('be.checked').and('not.be.disabled');

    // Mientras la recepcionista llena el formulario, otra persona ocupa ese horario (por la API).
    cy.apiToken(RECEPCION).then((token) =>
      cy.api(token, 'POST', '/api/appointments', { patientId: ctx.patient1, doctorId: ctx.doctorId, date: DATE, time: takenTime })
        .then((r) => expect(r.status, 'reserva de la otra persona').to.eq(201)));

    cy.contains('button', 'Confirmar cita').click();

    cy.get('#formError').should('have.text', 'Este horario ya está ocupado. Selecciona otro para continuar.');
    // Se conserva lo ingresado...
    cy.get('#PatientId').find('option:selected').should('have.text', PATIENTS[0].name);
    cy.get('#DoctorId').find('option:selected').should('have.text', DOCTOR);
    cy.get('#Date').should('have.value', DATE);
    // ...y el horario ocupado ya no se puede elegir.
    cy.get(`input[name=Time][value="${takenTime}"]`).should('be.disabled').and('not.be.checked');
    snap('07-conflicto-de-horario');

    // Se puede corregir y reservar otro horario sin volver a empezar.
    cy.contains('.times label', '10:30').click();
    cy.contains('button', 'Confirmar cita').click();
    cy.location('pathname').should('match', /^\/Agenda\/Cita\/[0-9a-f-]{36}$/);
    cy.contains('.summaryline', 'Hora').should('contain', '10:30');
  });

  it('8. cambia de mes en el calendario', () => {
    fillAgendaFilters();
    const nextMonth = firstOfMonth(DATE, 1);
    const afterNext = firstOfMonth(DATE, 2);

    cy.get('.calhead strong').should('have.text', monthLabel(DATE));

    cy.get('a[aria-label="Mes siguiente"]').click();
    cy.get('.calhead strong').should('have.text', monthLabel(nextMonth));
    cy.location('search').should('include', `date=${nextMonth}`);
    cy.get('.calgrid a.chosen').should('have.text', '1');
    cy.contains('.toolbar-date strong', '1 ').should('be.visible');

    cy.get('a[aria-label="Mes siguiente"]').click();
    cy.get('.calhead strong').should('have.text', monthLabel(afterNext));

    cy.get('a[aria-label="Mes anterior"]').click();
    cy.get('a[aria-label="Mes anterior"]').click();
    cy.get('.calhead strong').should('have.text', monthLabel(DATE));

    // Elegir un día del calendario cambia la fecha de la agenda.
    cy.get('.calgrid a').contains(/^15$/).click();
    cy.location('search').should('match', /date=\d{4}-\d{2}-15/);
    cy.get('.calgrid a.chosen').should('have.text', '15');
    snap('08-calendario');
  });
});

import { clinicToday } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Recorrido del Médico (WEB-11, 12, 13, 14, 15): abrir el expediente desde la agenda, ver las secciones, registrar una consulta,
 * crear una receta con varios medicamentos, consultarla, y comprobar que no se accede a un paciente sin relación de atención.
 *
 * Todas las acciones del usuario van por la interfaz. La API solo prepara y limpia datos: amplía temporalmente el horario de
 * la Dra. Méndez (y lo restaura), reserva una cita de hoy para el paciente de prueba y la cancela al terminar.
 * Cada ejecución crea una consulta y una receta nuevas (la información clínica no se borra); usan un texto único por ejecución.
 */
const MEDICO = 'lucia.mendez@example.com';
const RECEPCION = 'elena.vargas@example.com';
const ADMIN = 'admin@example.com';
const PATIENT_WITH_RELATION = { name: 'Cypress Paciente Uno', identification: 'CYP-E2E-001' };
const PATIENT_WITHOUT_RELATION = { name: 'Cypress Paciente Dos', identification: 'CYP-E2E-002' };

const RUN = Date.now().toString(36);
const REASON = `Motivo E2E ${RUN}`;
const NOTES = `Notas clínicas E2E ${RUN}`;
const DIAGNOSIS = `Diagnóstico E2E ${RUN}`;
const MEDICATIONS = [
  { name: `Paracetamol ${RUN}`, dose: '500 mg', frequency: 'Cada 8 horas', duration: '3 días', instructions: `Tomar con alimentos ${RUN}` },
  { name: `Omeprazol ${RUN}`, dose: '20 mg', frequency: 'Una vez al día', duration: '14 días', instructions: `En ayunas ${RUN}` },
];

const ctx = {}; // datos compartidos entre las pruebas ordenadas

const ok = (response, what) => {
  expect(response.status, what).to.be.oneOf([200, 201, 204]);
  return response.body;
};

const ensurePatient = (token, patient) =>
  cy.api(token, 'GET', `/api/patients?q=${patient.identification}`).then((search) => {
    const found = ok(search, 'buscar paciente').items.find((p) => p.identificationNumber === patient.identification);
    if (found) return cy.wrap(found.id);
    return cy.api(token, 'POST', '/api/patients', {
      fullName: patient.name, identificationNumber: patient.identification, birthDate: '1990-01-01', phone: '5550 1234', email: 'cypress@example.com',
    }).then((created) => ok(created, 'crear paciente').id);
  });

describe('Médico · expediente, consulta y receta', () => {
  before(() => {
    const today = clinicToday();

    // La Dra. Méndez atiende todo el día durante la prueba (para tener un horario libre más tarde hoy); se restaura al terminar.
    cy.apiToken(MEDICO).then((medico) => cy.api(medico, 'GET', '/api/doctors/me').then((me) => { ctx.doctorId = ok(me, 'médico').id; }));
    cy.apiToken(ADMIN).then((admin) => {
      cy.then(() => cy.api(admin, 'GET', `/api/doctors/${ctx.doctorId}/schedule`)).then((current) => {
        ctx.originalSchedule = current.status === 200 ? current.body : null;
        return cy.api(admin, 'PUT', `/api/doctors/${ctx.doctorId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '00:00', endTime: '23:30' })
          .then((r) => ok(r, 'ampliar horario'));
      });
    });

    cy.apiToken(RECEPCION).then((recepcion) => {
      ensurePatient(recepcion, PATIENT_WITH_RELATION).then((id) => { ctx.patientId = id; });
      ensurePatient(recepcion, PATIENT_WITHOUT_RELATION).then((id) => { ctx.strangerId = id; });
      cy.then(() => cy.api(recepcion, 'GET', `/api/doctors/${ctx.doctorId}/availability?date=${today}`)).then((availability) => {
        const slot = ok(availability, 'disponibilidad de hoy').slots.find((s) => s.available);
        expect(slot, 'hay un horario libre más tarde hoy (la prueba necesita una cita de hoy)').to.exist;
        ctx.slot = slot.time;
        return cy.api(recepcion, 'POST', '/api/appointments', { patientId: ctx.patientId, doctorId: ctx.doctorId, date: today, time: slot.time })
          .then((r) => { ctx.appointmentId = ok(r, 'reservar la cita de hoy').id; });
      });
    });
  });

  beforeEach(() => {
    cy.loginUi(MEDICO);
  });

  after(() => {
    cy.apiToken(RECEPCION).then((recepcion) => cy.api(recepcion, 'POST', `/api/appointments/${ctx.appointmentId}/cancel`));
    cy.apiToken(ADMIN).then((admin) => {
      const schedule = ctx.originalSchedule ?? { days: [1, 2, 3, 4, 5], startTime: '08:00', endTime: '12:00' };
      cy.api(admin, 'PUT', `/api/doctors/${ctx.doctorId}/schedule`, { days: schedule.days, startTime: schedule.startTime, endTime: schedule.endTime });
    });
  });

  it('1. inicia sesión como Médico y ve su agenda con la cita del paciente', () => {
    cy.visit('/');
    cy.contains('.user', 'Lucía Méndez').should('contain', 'Médico');
    cy.contains('nav a.nav', 'Mi agenda').click();
    cy.location('pathname').should('eq', '/MiAgenda');
    cy.contains('.slot', ctx.slot).should('contain', PATIENT_WITH_RELATION.name).and('contain', 'Confirmada');
    snap('10-medico-mi-agenda');
  });

  it('2. abre el expediente desde la agenda y recorre las secciones (laboratorio: pendiente)', () => {
    cy.visit('/MiAgenda');
    cy.contains('.slot', PATIENT_WITH_RELATION.name).contains('a', 'Abrir expediente').click();

    cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx.patientId}`);
    cy.contains('h2', PATIENT_WITH_RELATION.name);
    cy.contains('.sub', 'Información clínica autorizada');
    cy.contains('.tabs a.on', 'Consultas');
    snap('11-expediente-consultas');

    cy.contains('.tabs a', 'Recetas').click();
    cy.contains('.tabs a.on', 'Recetas');

    cy.contains('.tabs a', 'Laboratorio').click();
    cy.contains('.tabs a.on', 'Laboratorio');
    cy.get('#laboratorioPendiente').should('contain', 'pendiente').and('contain', 'todavía no está disponible');
    cy.get('.mobcard').should('not.exist'); // no se inventa ningún resultado
    snap('12-expediente-laboratorio-pendiente');
  });

  it('3. no guarda una consulta incompleta y muestra los errores', () => {
    cy.visit(`/Pacientes/Expediente/${ctx.patientId}?cita=${ctx.appointmentId}`);
    cy.contains('a.btn', 'Registrar consulta').click();
    cy.location('pathname').should('eq', `/Pacientes/RegistrarConsulta/${ctx.patientId}`);
    cy.contains('.notice', PATIENT_WITH_RELATION.name).and('contain', 'Dra. Lucía Méndez');
    cy.get('#Input_AppointmentId').find('option:selected').should('have.value', ctx.appointmentId);

    cy.contains('button', 'Guardar consulta').click();

    cy.location('pathname').should('eq', `/Pacientes/RegistrarConsulta/${ctx.patientId}`);
    cy.get('.field .err').should('have.length.at.least', 3).and('contain', 'Este campo es obligatorio.');
    snap('13-consulta-con-errores');
  });

  it('4 y 5. registra la consulta, crea una receta con dos medicamentos y la consulta', () => {
    cy.visit(`/Pacientes/RegistrarConsulta/${ctx.patientId}?cita=${ctx.appointmentId}`);
    cy.get('#Input_Reason').type(REASON);
    cy.get('#Input_Notes').type(NOTES);
    cy.get('#Input_Diagnosis').type(DIAGNOSIS);
    snap('14-consulta-llena');
    cy.contains('button', 'Guardar y crear receta').click();

    // WEB-14: receta de la consulta recién guardada.
    cy.location('pathname').should('match', /^\/Pacientes\/CrearReceta\/[0-9a-f-]{36}$/);
    cy.get('#toast').should('contain', 'Consulta guardada');
    cy.contains('.notice', PATIENT_WITH_RELATION.name);
    cy.get('fieldset.medicamento').should('have.length', 1);

    const fillMedication = (index, m) => {
      cy.get(`#Items_${index}__Medication`).type(m.name);
      cy.get(`#Items_${index}__Dose`).type(m.dose);
      cy.get(`#Items_${index}__Frequency`).type(m.frequency);
      cy.get(`#Items_${index}__Duration`).type(m.duration);
      cy.get(`#Items_${index}__Instructions`).type(m.instructions);
    };

    fillMedication(0, MEDICATIONS[0]);
    cy.contains('button', 'Agregar medicamento').click();
    cy.get('fieldset.medicamento').should('have.length', 2);
    cy.get('#Items_0__Medication').should('have.value', MEDICATIONS[0].name); // lo escrito se conserva

    // Se agrega un tercero y se quita: la receta queda con dos.
    cy.contains('button', 'Agregar medicamento').click();
    cy.get('fieldset.medicamento').should('have.length', 3);
    cy.get('fieldset.medicamento').eq(2).contains('button', 'Quitar este medicamento').click();
    cy.get('fieldset.medicamento').should('have.length', 2);

    fillMedication(1, MEDICATIONS[1]);
    snap('15-receta-dos-medicamentos');
    cy.contains('button', 'Guardar receta').click();

    // WEB-15: detalle de la receta.
    cy.location('pathname').should('match', /^\/Pacientes\/Receta\/[0-9a-f-]{36}$/);
    cy.location('pathname').then((path) => { ctx.prescriptionPath = path; });
    cy.get('#toast').should('contain', 'Receta guardada');
    cy.contains('.eyebrow', 'RECETA MÉDICA');
    cy.contains('h2', PATIENT_WITH_RELATION.name);
    cy.get('.medication-card').should('have.length', 2);
    MEDICATIONS.forEach((m, index) => {
      cy.get('.medication-card').eq(index).within(() => {
        cy.contains('h2', m.name);
        cy.contains('.summaryline', 'Dosis').should('contain', m.dose);
        cy.contains('.summaryline', 'Frecuencia').should('contain', m.frequency);
        cy.contains('.summaryline', 'Duración').should('contain', m.duration);
        cy.contains('h3', 'Indicaciones');
        cy.contains('p', m.instructions);
      });
    });
    snap('16-detalle-de-receta');
  });

  it('6. el expediente muestra la consulta y la receta, y la receta se abre desde su tarjeta', () => {
    cy.visit(ctx.prescriptionPath);
    cy.contains('a.btn', 'Volver al expediente').click();
    cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx.patientId}`);
    cy.contains('.tabs a.on', 'Recetas');
    cy.contains('.mobcard', `${MEDICATIONS[0].name} y 1 más`).contains('a', 'Ver receta').click();
    cy.location('pathname').should('eq', ctx.prescriptionPath);

    cy.visit(`/Pacientes/Expediente/${ctx.patientId}`);
    cy.contains('.mobcard', REASON).should('contain', DIAGNOSIS).and('contain', NOTES);
    snap('17-expediente-con-consulta');
  });

  it('7. la cita ya tiene consulta: no se puede registrar otra', () => {
    cy.visit(`/Pacientes/RegistrarConsulta/${ctx.patientId}`);
    cy.get('#sinCitas').should('contain', 'no tiene citas pendientes de consulta');
    cy.contains('button', 'Guardar consulta').should('not.exist');
  });

  it('8. un paciente sin cita ni consulta con este médico no se puede abrir', () => {
    cy.request({ url: `/Pacientes/Expediente/${ctx.strangerId}`, failOnStatusCode: false }).its('status').should('eq', 404);
    cy.visit(`/Pacientes/Expediente/${ctx.strangerId}`, { failOnStatusCode: false });
    cy.contains('No encontramos lo que buscas');
    cy.contains(PATIENT_WITHOUT_RELATION.name).should('not.exist');
    snap('18-paciente-sin-relacion');
  });
});

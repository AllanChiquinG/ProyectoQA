import { addDays, clinicToday } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Recorrido de los Médicos (WEB-11 a WEB-15): abrir el expediente desde la agenda, ver las secciones, registrar una consulta,
 * crear una receta con varios medicamentos y consultarla; y el expediente COMPARTIDO: un colega con relación de atención lee lo
 * que escribió otro médico pero no puede crear recetas sobre su consulta, y sin relación válida no accede.
 *
 * Aislamiento y horario: la prueba usa dos médicos dedicados a Cypress (e2e.medico y e2e.colega, sembrados solo en Development)
 * y pacientes "Cypress…". No toca la agenda de ningún médico de uso normal. La cita del médico es una cita ya pasada creada
 * directamente en la base de datos LOCAL (la API no permite reservar en el pasado), así que NO depende de la hora del día ni
 * de que quede un horario libre hoy. Al empezar y al terminar se borran las consultas, recetas y citas de los pacientes de prueba.
 * Todas las acciones del usuario van por la interfaz; la API solo prepara datos y comprueba la autorización en el servidor.
 */
const MEDICO = 'e2e.medico@example.com';
const COLEGA = 'e2e.colega@example.com';
const RECEPCION = 'elena.vargas@example.com';
const ADMIN = 'admin@example.com';
const MEDICO_NOMBRE = 'Dr. Cypress E2E';
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

describe('Médicos · expediente compartido, consulta y receta', () => {
  before(() => {
    cy.task('db:cleanE2eClinical'); // punto de partida limpio: sin consultas, recetas ni citas de los pacientes de prueba

    cy.apiToken(MEDICO).then((medico) => cy.api(medico, 'GET', '/api/doctors/me').then((me) => { ctx.doctorId = ok(me, 'médico de prueba').id; }));
    cy.apiToken(COLEGA).then((colega) => cy.api(colega, 'GET', '/api/doctors/me').then((me) => { ctx.colegaDoctorId = ok(me, 'colega de prueba').id; }));

    cy.apiToken(RECEPCION).then((recepcion) => {
      ensurePatient(recepcion, PATIENT_WITH_RELATION).then((id) => { ctx.patientId = id; });
      ensurePatient(recepcion, PATIENT_WITHOUT_RELATION).then((id) => { ctx.strangerId = id; });
    });

    // Cita ya pasada del médico con el paciente (independiente de la hora del día).
    cy.then(() => cy.task('db:createPastAppointment', { doctorId: ctx.doctorId, patientId: ctx.patientId })).then((appointment) => {
      ctx.appointmentId = appointment.id;
      ctx.pastDate = appointment.date;
    });

    // El colega (médico dedicado a la prueba) atiende todos los días de 08:00 a 12:00 y tiene una cita FUTURA con el paciente:
    // esa es su relación de atención válida. Una fecha futura de otro día no depende de la hora actual.
    cy.apiToken(ADMIN).then((admin) => cy.then(() => cy.api(admin, 'PUT', `/api/doctors/${ctx.colegaDoctorId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' }))
      .then((r) => ok(r, 'horario del colega de prueba')));
    cy.apiToken(RECEPCION).then((recepcion) => cy.then(() => cy.api(recepcion, 'POST', '/api/appointments',
      { patientId: ctx.patientId, doctorId: ctx.colegaDoctorId, date: addDays(clinicToday(), 10), time: '09:00' }))
      .then((r) => { ctx.colegaAppointmentId = ok(r, 'cita del colega').id; }));
  });

  after(() => {
    cy.apiToken(RECEPCION).then((recepcion) => cy.api(recepcion, 'POST', `/api/appointments/${ctx.colegaAppointmentId}/cancel`));
    cy.task('db:cleanE2eClinical');
  });

  describe('Médico autor', () => {
    beforeEach(() => {
      cy.loginUi(MEDICO);
    });

    it('1. inicia sesión como Médico y ve en su agenda la cita del paciente', () => {
      cy.visit('/');
      cy.contains('.user', MEDICO_NOMBRE).should('contain', 'Médico');
      cy.contains('nav a.nav', 'Mi agenda').click();
      cy.location('pathname').should('eq', '/MiAgenda');
      cy.get('input[name=date]').type(ctx.pastDate); // el día de la cita (pasada), por el calendario de la pantalla
      cy.location('search').should('include', `date=${ctx.pastDate}`);
      cy.contains('.slot', PATIENT_WITH_RELATION.name).should('contain', 'Confirmada');
      snap('10-medico-mi-agenda');
    });

    it('2. abre el expediente desde la agenda y recorre las secciones vacías (consultas, recetas y laboratorio)', () => {
      cy.visit(`/MiAgenda?date=${ctx.pastDate}`);
      cy.contains('.slot', PATIENT_WITH_RELATION.name).contains('a', 'Abrir expediente').click();

      cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx.patientId}`);
      cy.contains('h2', PATIENT_WITH_RELATION.name);
      cy.contains('.sub', 'Información clínica autorizada');
      cy.get('#expedienteCompartido').should('contain', 'Expediente compartido');
      cy.contains('.tabs a.on', 'Consultas');
      cy.contains('.emptymsg', 'Este paciente aún no tiene consultas registradas.');
      snap('11-expediente-consultas');

      cy.contains('.tabs a', 'Recetas').click();
      cy.contains('.tabs a.on', 'Recetas');
      cy.contains('.emptymsg', 'Sin recetas registradas.');

      cy.contains('.tabs a', 'Laboratorio').click();
      cy.contains('.tabs a.on', 'Laboratorio');
      cy.get('#sinResultados').should('contain', 'Sin resultados registrados.');
      cy.get('.mobcard').should('not.exist'); // no se inventa ningún resultado
      snap('12-expediente-laboratorio-vacio');
    });

    it('3. no guarda una consulta incompleta y muestra los errores', () => {
      cy.visit(`/Pacientes/Expediente/${ctx.patientId}?cita=${ctx.appointmentId}`);
      cy.contains('a.btn', 'Registrar consulta').click();
      cy.location('pathname').should('eq', `/Pacientes/RegistrarConsulta/${ctx.patientId}`);
      cy.contains('.notice', PATIENT_WITH_RELATION.name).and('contain', MEDICO_NOMBRE);
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
      cy.location('pathname').then((path) => { ctx.consultationId = path.split('/').pop(); });
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
      cy.contains('.mobcard', `${MEDICATIONS[0].name} y 1 más`).should('contain', 'Escrita por ti').contains('a', 'Ver receta').click();
      cy.location('pathname').should('eq', ctx.prescriptionPath);

      cy.visit(`/Pacientes/Expediente/${ctx.patientId}`);
      cy.contains('.mobcard', REASON).should('contain', DIAGNOSIS).and('contain', NOTES).and('contain', 'Registrada por ti');
      cy.contains('.mobcard', REASON).contains('a', 'Crear receta'); // en lo propio sí puede crear recetas
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

  describe('Médico colega (expediente compartido)', () => {
    beforeEach(() => {
      cy.loginUi(COLEGA);
    });

    it('9. el colega con una cita con el paciente lee la consulta y la receta de otro médico, marcadas como ajenas', () => {
      cy.visit('/Pacientes');
      cy.contains('tr', PATIENT_WITH_RELATION.name).contains('a', 'Abrir expediente').click();
      cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx.patientId}`);
      cy.get('#expedienteCompartido');

      cy.contains('.mobcard', REASON).within(() => {
        cy.contains(DIAGNOSIS);
        cy.contains(NOTES);
        cy.contains('.author', `Registrada por ${MEDICO_NOMBRE}`);
        cy.contains('a', 'Crear receta').should('not.exist'); // no es suya
      });
      snap('19-colega-lee-consulta-ajena');

      cy.contains('.tabs a', 'Recetas').click();
      cy.contains('.mobcard', `${MEDICATIONS[0].name} y 1 más`).should('contain', `Escrita por ${MEDICO_NOMBRE}`).contains('a', 'Ver receta').click();
      cy.location('pathname').should('eq', ctx.prescriptionPath);
      cy.get('.medication-card').should('have.length', 2);
      cy.contains('.sub', MEDICO_NOMBRE);
      snap('20-colega-lee-receta-ajena');
    });

    it('10. el colega no puede crear una receta sobre la consulta de otro: ni por la pantalla ni por la API', () => {
      cy.visit(`/Pacientes/CrearReceta/${ctx.consultationId}`, { failOnStatusCode: false });
      cy.contains('No tienes acceso a esta página');
      cy.get('fieldset.medicamento').should('not.exist');

      // También se rechaza en el servidor, no solo se oculta en la interfaz.
      cy.apiToken(COLEGA).then((token) => cy.api(token, 'POST', '/api/prescriptions', {
        consultationId: ctx.consultationId,
        items: [{ medication: 'Intruso', dose: '1', frequency: '1', duration: '1', instructions: 'No debe guardarse' }],
      }).its('status').should('eq', 404));
      cy.apiToken(COLEGA).then((token) => cy.api(token, 'PUT', `/api/consultations/${ctx.consultationId}`, { reason: 'x' }).its('status').should('eq', 405));
    });

    it('11. sin una relación válida el colega ya no accede al expediente', () => {
      // Recepción cancela la única cita del colega con el paciente.
      cy.apiToken(RECEPCION).then((recepcion) => cy.api(recepcion, 'POST', `/api/appointments/${ctx.colegaAppointmentId}/cancel`).its('status').should('eq', 204));

      cy.request({ url: `/Pacientes/Expediente/${ctx.patientId}`, failOnStatusCode: false }).its('status').should('eq', 404);
      cy.visit(`/Pacientes/Expediente/${ctx.patientId}`, { failOnStatusCode: false });
      cy.contains('No encontramos lo que buscas');
      cy.contains(REASON).should('not.exist');
      cy.request({ url: ctx.prescriptionPath, failOnStatusCode: false }).its('status').should('eq', 404);
      snap('21-colega-sin-relacion');
    });
  });
});

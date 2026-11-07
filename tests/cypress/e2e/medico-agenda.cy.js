import { assertControlsLabelled, assertNoHorizontalOverflow } from '../support/a11y';
import { addDays, clinicToday } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Médico · Mi agenda (WEB-11), pacientes autorizados (WEB-05 como Médico), pestañas del expediente y validaciones de la receta (WEB-14).
 * Complementa a medico-expediente.cy.js (consulta, receta y expediente compartido) sin repetirlo.
 *
 * Datos aislados: el médico dedicado "e2e.colega" ("Dra. Cypress Colega") con pacientes CYP-E2E-W-MA-* (la tarea db:cleanE2eWebPatients los borra,
 * con sus citas, consultas y recetas, al empezar y al terminar) y citas futuras a +25 días. Las acciones del usuario van por la interfaz.
 */
const MEDICO = 'e2e.colega@example.com';
const MEDICO_NOMBRE = 'Dra. Cypress Colega';
const RECEPCION = 'elena.vargas@example.com';
const ADMIN = 'admin@example.com';
const FECHA = addDays(clinicToday(), 25);
const FECHA_VACIA = addDays(clinicToday(), 26);
const PACIENTES = [
  { nombre: 'Cypress Agenda Uno', id: 'CYP-E2E-W-MA-1' },
  { nombre: 'Cypress Agenda Dos', id: 'CYP-E2E-W-MA-2' },
];
const AJENO = { nombre: 'Cypress Agenda Ajeno', id: 'CYP-E2E-W-MA-3' }; // sin relación con el médico
const ctx = {};
const ok = (r, que) => { expect(r.status, que).to.be.oneOf([200, 201, 204]); return r.body; };

describe('Médico · agenda, pacientes autorizados y receta', () => {
  before(() => {
    cy.task('db:cleanE2eWebPatients');
    cy.apiToken(MEDICO).then((m) => cy.api(m, 'GET', '/api/doctors/me').then((me) => { ctx.doctorId = ok(me, 'médico').id; }));
    cy.apiToken(ADMIN).then((a) => cy.then(() => cy.api(a, 'PUT', `/api/doctors/${ctx.doctorId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' })).then((r) => ok(r, 'horario')));
    cy.apiToken(RECEPCION).then((rec) => {
      [...PACIENTES, AJENO].forEach((p, i) => cy.api(rec, 'POST', '/api/patients',
        { fullName: p.nombre, identificationNumber: p.id, birthDate: '1985-05-05', phone: '5550 7777', email: `ma${i}@example.com` })
        .then((c) => { ctx[p.id] = ok(c, 'paciente').id; }));
      cy.then(() => PACIENTES.forEach((p, i) => cy.api(rec, 'POST', '/api/appointments', { patientId: ctx[p.id], doctorId: ctx.doctorId, date: FECHA, time: i === 0 ? '09:00' : '09:30' }).then((r) => ok(r, 'cita'))));
    });
    cy.then(() => cy.task('db:createPastAppointment', { doctorId: ctx.doctorId, patientId: ctx[PACIENTES[0].id] })).then((a) => { ctx.pastAppointment = a; });
  });

  after(() => {
    cy.task('db:cleanE2eWebPatients');
  });

  beforeEach(() => cy.loginUi(MEDICO));

  describe('Mi agenda', () => {
    it('1. muestra solo su agenda del día con las citas, el contador, el calendario y sin opciones de reserva', () => {
      cy.visit(`/MiAgenda?date=${FECHA}`);
      cy.contains('h1', 'Mi agenda');
      cy.contains('.daytitle', MEDICO_NOMBRE).and('contain', '2 citas programadas');
      cy.contains('.slot', '09:00').should('contain', PACIENTES[0].nombre).and('contain', 'Confirmada');
      cy.contains('.slot', '09:30').should('contain', PACIENTES[1].nombre);
      cy.contains('.toolbar', 'Vista del día');
      cy.get('select[name=doctorId]').should('not.exist'); // un médico no ve la agenda de otros
      cy.contains('a.btn', 'Reservar cita').should('not.exist');
      cy.contains('a', '＋ Horario disponible').should('not.exist');
      cy.contains('.slot', '10:00').should('contain', 'Horario disponible');
      assertControlsLabelled();
      snap('53-mi-agenda');
    });

    it('2. cambia de día con la fecha y con el calendario; un día sin citas y un día pasado lo dicen', () => {
      cy.visit(`/MiAgenda?date=${FECHA}`);
      cy.get('input[name=date]').type(FECHA_VACIA);
      cy.location('search').should('include', `date=${FECHA_VACIA}`);
      cy.contains('.daytitle', '0 citas programadas');
      cy.get('.slot').should('have.length', 8);
      cy.get('.calhead strong').invoke('text').then((mes) => {
        cy.get('a[aria-label="Mes siguiente"]').click();
        cy.get('.calhead strong').should('not.have.text', mes);
        cy.get('a[aria-label="Mes anterior"]').click();
        cy.get('.calhead strong').should('have.text', mes);
      });
      cy.visit(`/MiAgenda?date=${ctx.pastAppointment.date}`);
      cy.contains('.slot', ctx.pastAppointment.time).should('contain', PACIENTES[0].nombre);
      cy.visit(`/MiAgenda?date=${addDays(clinicToday(), -2)}`);
      cy.contains('.emptymsg', 'No hubo citas este día.');
    });

    it('3. "Abrir expediente" desde la cita lleva al expediente del paciente correcto', () => {
      cy.visit(`/MiAgenda?date=${FECHA}`);
      cy.contains('.slot', PACIENTES[1].nombre).contains('a', 'Abrir expediente').click();
      cy.location('pathname').should('eq', `/Pacientes/Expediente/${ctx[PACIENTES[1].id]}`);
      cy.contains('h2', PACIENTES[1].nombre);
    });
  });

  describe('Pacientes autorizados', () => {
    it('4. lista solo a los pacientes con relación de atención, sin "Registrar paciente", y busca', () => {
      cy.visit('/Pacientes');
      cy.contains('h1', 'Gestión de pacientes');
      cy.contains('a.btn', 'Registrar paciente').should('not.exist');
      cy.contains('tbody tr', PACIENTES[0].nombre).contains('a.link', 'Abrir expediente');
      cy.contains('tbody tr', PACIENTES[1].nombre);
      cy.contains('tbody', AJENO.nombre).should('not.exist');
      cy.get('input[name=q]').type('Agenda Dos');
      cy.contains('button', 'Buscar').click();
      cy.get('tbody tr').should('have.length', 1).and('contain', PACIENTES[1].nombre);
      cy.get('input[name=q]').clear().type(AJENO.id);
      cy.contains('button', 'Buscar').click();
      cy.contains('.emptymsg', 'Aún no tienes pacientes autorizados.');
      assertControlsLabelled();
    });

    it('5. un paciente sin relación no se abre (404) y el médico no entra al alta ni a la edición de pacientes', () => {
      cy.visit(`/Pacientes/Expediente/${ctx[AJENO.id]}`, { failOnStatusCode: false });
      cy.contains('h2', 'No encontramos lo que buscas');
      cy.contains('El registro no existe o no tienes acceso a él.');
      cy.visit('/Pacientes/Formulario', { failOnStatusCode: false });
      cy.location('pathname').should('not.eq', '/Pacientes/Formulario');
    });
  });

  describe('Expediente y receta', () => {
    it('6. las pestañas se recorren con el teclado y la pestaña activa queda marcada', () => {
      cy.visit(`/Pacientes/Expediente/${ctx[PACIENTES[0].id]}`);
      cy.get('[role=tablist]').should('have.attr', 'aria-label', 'Secciones del expediente');
      cy.get('[role=tab][aria-selected=true]').should('have.text', 'Consultas');
      cy.get('[role=tab]').should('have.length', 3);
      cy.contains('[role=tab]', 'Laboratorio').click();
      cy.location('search').should('include', 'tab=Laboratorio');
      cy.get('[role=tab][aria-selected=true]').should('have.text', 'Laboratorio');
    });

    it('7. la receta valida los campos obligatorios, permite hasta 10 medicamentos y conserva lo escrito', () => {
      cy.visit(`/Pacientes/RegistrarConsulta/${ctx[PACIENTES[0].id]}?cita=${ctx.pastAppointment.id}`);
      cy.get('#Input_Reason').type('Motivo agenda');
      cy.get('#Input_Notes').type('Notas agenda');
      cy.get('#Input_Diagnosis').type('Diagnóstico agenda');
      cy.contains('button', 'Guardar y crear receta').click();
      cy.location('pathname').should('match', /^\/Pacientes\/CrearReceta\//);
      assertControlsLabelled();

      cy.contains('button', 'Guardar receta').click();
      cy.get('fieldset.medicamento').should('have.length', 1);
      cy.get('.field .err').filter(':visible').should('have.length.at.least', 5).and('contain', 'Este campo es obligatorio.');
      cy.contains('button', 'Quitar este medicamento').should('not.exist'); // con uno solo no se puede quitar

      cy.get('#Items_0__Medication').type('Ibuprofeno agenda');
      for (let n = 2; n <= 10; n += 1) {
        cy.contains('button', 'Agregar medicamento').click();
        cy.get('fieldset.medicamento').should('have.length', n);
      }
      cy.contains('button', 'Agregar medicamento').should('not.exist'); // máximo 10
      cy.get('#Items_0__Medication').should('have.value', 'Ibuprofeno agenda');
      for (let n = 10; n >= 2; n -= 1) {
        cy.get('fieldset.medicamento').last().contains('button', 'Quitar este medicamento').click();
        cy.get('fieldset.medicamento').should('have.length', n - 1);
      }

      cy.get('#Items_0__Dose').type('400 mg');
      cy.get('#Items_0__Frequency').type('Cada 8 horas');
      cy.get('#Items_0__Duration').type('5 días');
      cy.get('#Items_0__Instructions').type('Con alimentos');
      cy.contains('button', 'Guardar receta').click();
      cy.location('pathname').should('match', /^\/Pacientes\/Receta\//);
      cy.get('#toast').should('contain', 'Receta guardada');
      cy.get('.medication-card').should('have.length', 1);
    });
  });

  describe('Pantalla pequeña', () => {
    it('8. mi agenda, pacientes y expediente no desbordan a 375 px', () => {
      cy.viewport(375, 812);
      [`/MiAgenda?date=${FECHA}`, '/Pacientes', `/Pacientes/Expediente/${ctx[PACIENTES[0].id]}`].forEach((ruta) => {
        cy.visit(ruta);
        assertNoHorizontalOverflow(ruta);
      });
      snap('54-mi-agenda-movil');
    });
  });
});

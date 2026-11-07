import { addDays, clinicToday, firstOfMonth, monthLabel } from '../support/dates';
import { assertControlsLabelled, assertNoHorizontalOverflow } from '../support/a11y';
import { snap } from '../support/screenshots';

/**
 * Recepción · agenda y reserva (WEB-02, 03, 04): filtros, calendario, horarios, validaciones, reserva, detalle, cancelación y prevención de doble
 * reserva. Complementa a recepcion-reserva.cy.js (recorrido principal) sin repetirlo.
 *
 * Datos aislados: médica de pruebas "Dra. Cypress Colega" (todos los días 08:00-12:00) y pacientes "Cypress…"; fechas a 20 días en adelante, que
 * se limpian (citas canceladas por la API) al empezar y al terminar. No toca la agenda de ningún médico de uso normal.
 */
const RECEPCION = 'elena.vargas@example.com';
const ADMIN = 'admin@example.com';
const DOCTOR = 'Dra. Cypress Colega';
const RIOS = 'Dr. Alejandro Ríos'; // atiende de lunes a viernes: sirve para comprobar un fin de semana
const FECHA = addDays(clinicToday(), 20);
const FECHA_2 = addDays(clinicToday(), 21);
const ctx = {};

const ok = (r, que) => { expect(r.status, que).to.be.oneOf([200, 201, 204]); return r.body; };

const limpiar = () =>
  cy.apiToken(RECEPCION).then((token) => [FECHA, FECHA_2].forEach((fecha) =>
    cy.api(token, 'GET', `/api/appointments?doctorId=${ctx.doctorId}&date=${fecha}`).then((r) => (r.body || []).forEach((a) => cy.api(token, 'POST', `/api/appointments/${a.id}/cancel`)))));

/** Primer sábado a partir de mañana (hora de la clínica). */
const proximoSabado = () => {
  let f = addDays(clinicToday(), 1);
  while (new Date(`${f}T12:00:00Z`).getUTCDay() !== 6) f = addDays(f, 1);
  return f;
};

describe('Recepción · agenda y reserva', () => {
  before(() => {
    cy.apiToken(ADMIN).then((admin) => {
      cy.api(admin, 'GET', '/api/doctors').then((r) => {
        ctx.doctorId = ok(r, 'médicos').find((d) => d.fullName === DOCTOR).id;
        ctx.riosId = r.body.find((d) => d.fullName === RIOS).id;
        cy.api(admin, 'PUT', `/api/doctors/${ctx.doctorId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' }).then((s) => ok(s, 'horario'));
      });
    });
    cy.apiToken(RECEPCION).then((token) => {
      ['CYP-E2E-001', 'CYP-E2E-002'].forEach((id, i) => cy.api(token, 'GET', `/api/patients?q=${id}`).then((r) => {
        const found = ok(r, 'paciente').items.find((p) => p.identificationNumber === id);
        if (found) { ctx[`p${i}`] = found.id; return; }
        cy.api(token, 'POST', '/api/patients', { fullName: `Cypress Paciente ${i === 0 ? 'Uno' : 'Dos'}`, identificationNumber: id, birthDate: '1990-01-01', phone: '5550 1234', email: `c${i}@example.com` })
          .then((c) => { ctx[`p${i}`] = ok(c, 'crear').id; });
      }));
    });
    cy.then(limpiar);
  });

  after(() => {
    cy.then(limpiar);
  });

  beforeEach(() => {
    cy.loginUi(RECEPCION);
  });

  describe('Agenda (WEB-02)', () => {
    it('1. muestra la fecha, el filtro de médico, el calendario y el aviso, y "Reservar cita" lleva al formulario con lo elegido', () => {
      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.contains('h1', 'Agenda de citas');
      cy.get('select[name=doctorId] option:selected').should('have.text', DOCTOR);
      cy.get('select[name=doctorId] option').should('have.length.at.least', 3);
      cy.get('input[name=date]').should('have.value', FECHA);
      cy.contains('.daytitle', DOCTOR).and('contain', '0 citas programadas');
      cy.get('.slot').should('have.length', 8);
      cy.contains('.calendar', monthLabel(FECHA));
      cy.contains('.note h3', 'Todo listo para atender');
      cy.contains('a.btn', 'Reservar cita').should('have.attr', 'href').and('include', `doctorId=${ctx.doctorId}`).and('include', `date=${FECHA}`);
      cy.contains('a.btn', 'Reservar cita').click();
      cy.location('pathname').should('eq', '/Agenda/Reservar');
      cy.get('select[name=DoctorId] option:selected').should('have.text', DOCTOR);
      cy.get('input[name=Date]').should('have.value', FECHA);
      snap('48-agenda');
    });

    it('2. los filtros funcionan: cambiar de médico, de fecha, un día del calendario y los meses anterior y siguiente', () => {
      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.get('select[name=doctorId]').select(RIOS);
      cy.location('search').should('include', `doctorId=${ctx.riosId}`);
      cy.contains('.daytitle', RIOS);
      cy.get('input[name=date]').type(FECHA_2);
      cy.location('search').should('include', `date=${FECHA_2}`);
      cy.get('input[name=date]').should('have.value', FECHA_2);

      // Calendario: un día del mes, el día elegido queda marcado.
      const dia = Number(FECHA_2.slice(8));
      cy.get(`.calgrid a[aria-label]`).contains(new RegExp(`^${dia === 15 ? 14 : 15}$`)).click();
      cy.location('search').should('match', /date=\d{4}-\d{2}-(14|15)/);
      cy.get('.calgrid a.chosen').should('have.attr', 'aria-current', 'date');

      cy.get('a[aria-label="Mes siguiente"]').click();
      cy.get('.calhead strong').invoke('text').should('not.eq', monthLabel(FECHA_2));
      cy.get('a[aria-label="Mes anterior"]').click();
      cy.get('a[aria-label="Mes anterior"]').click();
      cy.get('.calhead strong').should('not.have.text', monthLabel(FECHA_2));
    });

    it('3. un fin de semana sin atención lo dice, y un día pasado no ofrece horarios', () => {
      cy.visit(`/Agenda?doctorId=${ctx.riosId}&date=${proximoSabado()}`);
      cy.contains('.emptymsg', 'El médico no atiende este día.');
      cy.contains('a.btn', 'Reservar cita').should('exist');

      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${addDays(clinicToday(), -1)}`);
      cy.contains('.emptymsg', 'No hubo citas este día.');
      cy.get('.slot').should('not.exist');
      cy.contains('a.appointment.empty', 'Horario disponible').should('not.exist');
    });

    it('4. un horario libre abre la reserva con médico, fecha y hora ya elegidos', () => {
      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.contains('.slot', '09:00').find('a.appointment.empty').should('contain', 'Horario disponible').click();
      cy.location('pathname').should('eq', '/Agenda/Reservar');
      cy.get('input[name=Time][value="09:00"]').should('be.checked');
      cy.get('select[name=DoctorId] option:selected').should('have.text', DOCTOR);
    });
  });

  describe('Reserva (WEB-03)', () => {
    it('5. valida: sin paciente y sin horario pide elegirlos, y conserva lo ya elegido', () => {
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.contains('button', 'Confirmar cita').click();
      cy.get('#formError').should('contain', 'Selecciona el paciente.');
      cy.get('select[name=PatientId]').select('Cypress Paciente Uno');
      cy.contains('button', 'Confirmar cita').click();
      cy.get('#formError').should('contain', 'Selecciona un horario disponible.');
      cy.get('select[name=PatientId] option:selected').should('have.text', 'Cypress Paciente Uno');
      cy.get('select[name=DoctorId] option:selected').should('have.text', DOCTOR);
      cy.get('input[name=Date]').should('have.value', FECHA);
      snap('49-reserva-validacion');
    });

    it('6. tiene etiquetas en todos los campos, el grupo de horarios con nombre y la duración informada', () => {
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`);
      assertControlsLabelled();
      cy.get('[role=radiogroup]').should('have.attr', 'aria-labelledby', 'timesTitle');
      cy.get('input[aria-label="Duración"]').should('be.disabled').and('have.value', '30 minutos');
      cy.get('input[name=Time]').should('have.length', 8);
      cy.contains('.notice', 'Si otro paciente ocupa el horario, podrás elegir otro sin perder los datos.');
    });

    it('7. cambiar de médico o de fecha actualiza los horarios sin perder el paciente; un día sin atención no ofrece horarios', () => {
      cy.intercept('POST', '**/Agenda/Reservar?handler=Actualizar').as('actualizar');
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.get('select[name=PatientId]').select('Cypress Paciente Dos');
      cy.wait('@actualizar');
      cy.get('select[name=DoctorId]').select(RIOS);
      cy.wait('@actualizar');
      cy.get('select[name=PatientId] option:selected').should('have.text', 'Cypress Paciente Dos');
      cy.get('select[name=DoctorId] option:selected').should('have.text', RIOS);
      cy.get('input[name=Date]').type(proximoSabado());
      cy.wait('@actualizar');
      cy.get('input[name=Time]').should('not.exist');
      cy.contains('.sub', 'El médico no atiende este día.');
      cy.get('select[name=PatientId] option:selected').should('have.text', 'Cypress Paciente Dos');
    });

    it('8. reserva y abre el detalle; Volver a agenda regresa al día; cancelar libera el horario', () => {
      cy.intercept('POST', '**/Agenda/Reservar?handler=Actualizar').as('actualizar');
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA_2}`);
      cy.get('select[name=PatientId]').select('Cypress Paciente Uno');
      cy.wait('@actualizar');
      cy.contains('.times label', '10:30').click();
      cy.get('input[name=Time][value="10:30"]').should('be.checked');
      cy.contains('button', 'Confirmar cita').click();

      cy.location('pathname').should('match', /^\/Agenda\/Cita\/[0-9a-f-]{36}$/);
      cy.get('#toast').should('contain', 'Cita confirmada correctamente');
      cy.contains('.summaryline', 'Paciente').should('contain', 'Cypress Paciente Uno');
      cy.contains('.summaryline', 'Médico').should('contain', DOCTOR);
      cy.contains('.summaryline', 'Hora').should('contain', '10:30');
      cy.location('pathname').then((path) => {
        cy.contains('a.btn', 'Volver a agenda').click();
        cy.location('search').should('include', `date=${FECHA_2}`);
        cy.contains('.daytitle', '1 cita programada');
        cy.contains('.slot', '10:30').should('contain', 'Cypress Paciente Uno').and('contain', 'Confirmada');

        cy.on('window:confirm', () => true);
        cy.visit(path);
        cy.contains('button', 'Cancelar cita').click();
        cy.get('#toast').should('contain', 'La cita fue cancelada');
        cy.contains('.summaryline', 'Estado').should('contain', 'Cancelada');
        cy.contains('button', 'Cancelar cita').should('not.exist');
      });
      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA_2}`);
      cy.contains('.slot', '10:30').find('a.appointment.empty').should('exist');
    });
  });

  describe('Prevención de doble reserva', () => {
    it('9. dos reservas al mismo horario (una tras otra; la concurrencia real la cubren las pruebas xUnit): solo una queda y la otra recibe 409', () => {
      cy.apiToken(RECEPCION).then((token) => {
        const reservar = (paciente, clave) => cy.api(token, 'POST', '/api/appointments', { patientId: paciente, doctorId: ctx.doctorId, date: FECHA, time: '11:00' });
        reservar(ctx.p0).then((a) => reservar(ctx.p1).then((b) => {
          expect([a.status, b.status].sort()).to.deep.equal([201, 409]);
        }));
        cy.api(token, 'GET', `/api/appointments?doctorId=${ctx.doctorId}&date=${FECHA}`).its('body').then((citas) => {
          expect(citas.filter((c) => c.time === '11:00' && c.status === 'Confirmed')).to.have.length(1);
        });
      });
    });

    it('10. pulsar dos veces "Confirmar cita" crea una sola cita (la reserva lleva una clave de idempotencia)', () => {
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.get('input[name=IdempotencyKey]').should('have.attr', 'value').and('have.length.at.least', 8);
      cy.intercept('POST', '**/Agenda/Reservar?handler=Actualizar').as('actualizar');
      cy.get('select[name=PatientId]').select('Cypress Paciente Dos');
      cy.wait('@actualizar');
      cy.contains('.times label', '11:30').click();
      cy.contains('button', 'Confirmar cita').dblclick();
      cy.location('pathname').should('match', /^\/Agenda\/Cita\//);
      cy.apiToken(RECEPCION).then((token) => cy.api(token, 'GET', `/api/appointments?doctorId=${ctx.doctorId}&date=${FECHA}`).its('body').then((citas) => {
        expect(citas.filter((c) => c.time === '11:30' && c.status === 'Confirmed')).to.have.length(1);
      }));
    });

    it('11. un horario ocupado aparece sin opción de elegirlo (deshabilitado) en la reserva', () => {
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.get('input[name=Time][value="11:00"]').should('be.disabled');
      cy.get('input[name=Time][value="11:30"]').should('be.disabled');
      cy.get('input[name=Time][value="08:00"]').should('not.be.disabled');
    });
  });

  describe('Pantalla pequeña', () => {
    it('12. la agenda, la reserva y el detalle se adaptan a 375 px sin desbordar; el menú sigue disponible', () => {
      cy.viewport(375, 812);
      [`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA}`, `/Agenda/Reservar?doctorId=${ctx.doctorId}&date=${FECHA}`].forEach((ruta) => {
        cy.visit(ruta);
        assertNoHorizontalOverflow(ruta);
        cy.contains('nav a.nav', 'Agenda de citas').should('be.visible');
      });
      cy.visit(`/Agenda?doctorId=${ctx.doctorId}&date=${FECHA}`);
      cy.contains('.slot', '08:00').should('be.visible');
      snap('50-agenda-movil');
    });
  });
});

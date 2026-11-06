import { assertControlsLabelled, assertNoHorizontalOverflow } from '../support/a11y';
import { addDays, clinicToday } from '../support/dates';
import { snap } from '../support/screenshots';

/**
 * Administrador · médicos (WEB-07, 08), disponibilidad (WEB-09) y usuarios y roles (WEB-10).
 * Datos aislados: médicos "Dr. Cypress Web…" (la tarea db:cleanE2eWebDoctors los borra al empezar y al terminar), la médica "Dra. Cypress Colega"
 * para la disponibilidad (se restaura) y la cuenta de personal "e2e.acceso@example.com" para el cambio de rol (se restaura a Recepción).
 */
const ADMIN = 'admin@example.com';
const RECEPCION = 'elena.vargas@example.com';
const ACCESO = 'e2e.acceso@example.com';
const COLEGA = 'Dra. Cypress Colega';
const NUEVO = `Dr. Cypress Web ${Date.now().toString(36)}`;
const EDITADO = `${NUEVO} Editado`;
const ctx = {};

const llenarMedico = (d) => {
  const set = (name, v) => cy.get(`input[name="Input.${name}"]`).clear().type(v || '{backspace}', { parseSpecialCharSequences: false });
  if (d.nombre !== undefined) set('FullName', d.nombre);
  if (d.especialidad !== undefined) set('Specialty', d.especialidad);
  if (d.telefono !== undefined) set('Phone', d.telefono);
  if (d.correo !== undefined) set('Email', d.correo);
};
const VALIDO = { nombre: NUEVO, especialidad: 'Medicina interna', telefono: '5550 4321', correo: 'cypress.web@example.com' };

describe('Administrador', () => {
  before(() => {
    cy.task('db:cleanE2eWebDoctors');
    cy.apiToken(ADMIN).then((t) => cy.api(t, 'GET', '/api/doctors').its('body').then((docs) => { ctx.colegaId = docs.find((d) => d.fullName === COLEGA).id; }));
    // Estado conocido: la prueba 5 no debe depender de lo que dejó una corrida anterior.
    cy.apiToken(ADMIN).then((t) => cy.api(t, 'PUT', `/api/doctors/${ctx.colegaId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' }));
  });

  after(() => {
    cy.apiToken(ADMIN).then((t) => {
      cy.api(t, 'PUT', `/api/doctors/${ctx.colegaId}/schedule`, { days: [0, 1, 2, 3, 4, 5, 6], startTime: '08:00', endTime: '12:00' });
      cy.api(t, 'GET', '/api/users').its('body').then((us) => {
        const u = us.find((x) => x.email === ACCESO);
        if (u && u.role !== 'Recepcion') cy.api(t, 'PUT', `/api/users/${u.id}/role`, { role: 'Recepcion' });
      });
    });
    cy.task('db:cleanE2eWebDoctors');
  });

  beforeEach(() => cy.loginUi(ADMIN));

  describe('Médicos', () => {
    it('1. lista, busca (por nombre), muestra "sin resultados" y conserva la búsqueda', () => {
      cy.visit('/Medicos');
      cy.contains('h1', 'Gestión de médicos');
      cy.get('tbody tr').should('have.length.at.least', 3);
      cy.get('input[name=q]').type('Cypress Colega');
      cy.contains('button', 'Buscar').click();
      cy.get('tbody tr').should('have.length', 1).and('contain', COLEGA);
      cy.get('input[name=q]').should('have.value', 'Cypress Colega');
      cy.contains('.sub', '1 médico');
      cy.get('input[name=q]').clear().type('zzzz-no-existe');
      cy.contains('button', 'Buscar').click();
      cy.contains('.emptymsg', 'No se encontraron médicos.');
      assertControlsLabelled();
    });

    it('2. valida el registro: campos vacíos, correo y teléfono inválidos, sin guardar', () => {
      cy.visit('/Medicos');
      cy.contains('a.btn', 'Registrar médico').click();
      cy.contains('h1', 'Registrar médico');
      assertControlsLabelled();
      cy.contains('button', 'Guardar médico').click();
      cy.get('span.err').filter(':visible').should('have.length.at.least', 4);
      llenarMedico({ ...VALIDO, correo: 'sin-arroba', telefono: 'abc' });
      cy.contains('button', 'Guardar médico').click();
      cy.get('span.err').filter(':visible').should('have.length.at.least', 1);
      cy.location('pathname').should('eq', '/Medicos/Formulario');
      snap('51-medico-validacion');
    });

    it('3. registra un médico, aparece en la lista y se conserva al recargar', () => {
      cy.visit('/Medicos/Formulario');
      llenarMedico(VALIDO);
      cy.contains('button', 'Guardar médico').click();
      cy.location('pathname').should('eq', '/Medicos');
      cy.get('#toast').should('contain', 'Datos del médico guardados');
      cy.get('input[name=q]').type(NUEVO);
      cy.contains('button', 'Buscar').click();
      cy.get('tbody tr').should('have.length', 1).and('contain', 'Medicina interna').and('contain', 'cypress.web@example.com');
      cy.reload();
      cy.get('tbody tr').should('have.length', 1);
    });

    it('4. edita al médico (datos precargados), Cancelar no guarda y Guardar persiste el cambio', () => {
      cy.visit(`/Medicos?q=${encodeURIComponent(NUEVO)}`);
      cy.contains('tr', NUEVO).contains('a.link', 'Editar').click();
      cy.contains('h1', 'Editar médico');
      cy.get('input[name="Input.FullName"]').should('have.value', NUEVO);
      cy.get('input[name="Input.Specialty"]').should('have.value', 'Medicina interna');
      llenarMedico({ nombre: 'Nombre que no se guarda' });
      cy.contains('a.btn', 'Cancelar').click();
      cy.location('pathname').should('eq', '/Medicos');
      cy.visit(`/Medicos?q=${encodeURIComponent(NUEVO)}`);
      cy.get('tbody tr').should('have.length', 1);
      cy.contains('tr', NUEVO).contains('a.link', 'Editar').click();
      llenarMedico({ nombre: EDITADO, especialidad: 'Cardiología' });
      cy.contains('button', 'Guardar médico').click();
      cy.get('#toast').should('contain', 'Datos del médico guardados');
      cy.visit(`/Medicos?q=${encodeURIComponent(EDITADO)}`);
      cy.get('tbody tr').should('have.length', 1).and('contain', 'Cardiología');
    });
  });

  describe('Disponibilidad', () => {
    it('5. muestra el horario actual y valida horas', () => {
      cy.visit(`/Medicos/Disponibilidad/${ctx.colegaId}`);
      cy.contains('h2', COLEGA);
      cy.get('select[name=Preset]').find('option:selected').should('contain', 'Personalizado');
      assertControlsLabelled();
      cy.get('input[name=StartTime]').should('have.value', '08:00');

      cy.get('input[name=EndTime]').clear().type('07:00');
      cy.contains('button', 'Guardar disponibilidad').click();
      cy.get('#formError').should('contain', 'La hora final debe ser posterior a la inicial.');

      cy.get('input[name=StartTime]').clear();
      cy.contains('button', 'Guardar disponibilidad').click();
      cy.get('#formError').should('contain', 'Indica la hora de inicio y de finalización.');
    });

    it('6. cambiar días y horas se refleja en la reserva (martes y jueves 09:00 a 10:30)', () => {
      cy.visit(`/Medicos/Disponibilidad/${ctx.colegaId}`);
      cy.get('select[name=Preset]').select('Martes y jueves');
      cy.get('input[name=StartTime]').clear().type('09:00');
      cy.get('input[name=EndTime]').clear().type('10:30');
      cy.contains('button', 'Guardar disponibilidad').click();
      cy.get('#toast').should('contain', 'Disponibilidad guardada');

      cy.visit(`/Medicos/Disponibilidad/${ctx.colegaId}`);
      cy.get('select[name=Preset]').find('option:selected').should('have.text', 'Martes y jueves');
      cy.get('input[name=StartTime]').should('have.value', '09:00');

      // Efecto: la reserva ofrece 3 horarios un martes y ninguno el día siguiente.
      let f = addDays(clinicToday(), 1);
      while (new Date(`${f}T12:00:00Z`).getUTCDay() !== 2) f = addDays(f, 1);
      const otro = addDays(f, 1);
      cy.loginUi(RECEPCION);
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.colegaId}&date=${f}`);
      cy.get('input[name=Time]').should('have.length', 3);
      cy.visit(`/Agenda/Reservar?doctorId=${ctx.colegaId}&date=${otro}`);
      cy.get('input[name=Time]').should('not.exist');
      cy.contains('.sub', 'El médico no atiende este día.');
    });
  });

  describe('Usuarios y roles', () => {
    it('7. lista a las personas con su rol; la cuenta propia no se puede cambiar', () => {
      cy.visit('/Usuarios');
      cy.contains('h1', 'Usuarios y roles');
      cy.get('tbody tr').should('have.length.at.least', 5);
      cy.contains('tr', ADMIN).should('contain', 'Tu cuenta').find('select').should('be.disabled');
      assertControlsLabelled();
    });

    it('8. cambia el rol de una cuenta, avisa del cierre de sus sesiones y el nuevo rol se aplica; se restaura', () => {
      cy.visit('/Usuarios');
      cy.contains('tr', ACCESO).find('select').select('Laboratorio');
      cy.contains('tr', ACCESO).contains('button', 'Guardar').click();
      cy.get('#toast').should('contain', 'Rol actualizado. Se cerraron las sesiones activas de esa persona.');
      cy.contains('tr', ACCESO).find('select option:selected').should('have.text', 'Laboratorio');

      cy.loginUi(ACCESO);
      cy.visit('/Laboratorio');
      cy.location('pathname').should('eq', '/Laboratorio');
      cy.visit('/Agenda', { failOnStatusCode: false });
      cy.location('pathname').should('not.eq', '/Agenda');

      cy.loginUi(ADMIN);
      cy.visit('/Usuarios');
      cy.contains('tr', ACCESO).find('select').select('Recepción');
      cy.contains('tr', ACCESO).contains('button', 'Guardar').click();
      cy.get('#toast').should('contain', 'Rol actualizado');
    });

    it('9. Recepción no entra a Médicos ni a Usuarios', () => {
      cy.loginUi(RECEPCION);
      ['/Medicos', '/Usuarios', '/Medicos/Formulario'].forEach((ruta) => {
        cy.visit(ruta, { failOnStatusCode: false });
        cy.location('pathname').should('not.eq', ruta);
      });
    });
  });

  describe('Pantalla pequeña', () => {
    it('10. médicos, formulario, disponibilidad y usuarios no desbordan a 375 px', () => {
      cy.viewport(375, 812);
      ['/Medicos', '/Medicos/Formulario', `/Medicos/Disponibilidad/${ctx.colegaId}`, '/Usuarios'].forEach((ruta) => {
        cy.visit(ruta);
        assertNoHorizontalOverflow(ruta);
      });
      snap('52-usuarios-movil');
    });
  });
});

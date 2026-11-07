import { snap } from '../support/screenshots';

/**
 * Acceso y permisos de la web del personal (WEB-01): inicio y cierre de sesión, sesión vencida o revocada, ruta protegida con regreso, menú por rol
 * y restricciones por rol en la web y en la API. Datos aislados: usa la cuenta dedicada e2e.acceso@example.com (Recepción) para los cambios de rol.
 * Todas las acciones del usuario van por la interfaz; la API solo prepara datos y comprueba la autorización en el servidor.
 */
const ROLES = {
  Recepcion: { email: 'elena.vargas@example.com', nombre: 'Elena Vargas', texto: 'Recepción', menu: ['Agenda de citas', 'Pacientes'], inicio: ['Agenda de citas', 'Pacientes'] },
  Medico: { email: 'lucia.mendez@example.com', nombre: 'Lucía Méndez', texto: 'Médico', menu: ['Mi agenda', 'Pacientes autorizados'], inicio: ['Mi agenda', 'Pacientes autorizados'] },
  Laboratorio: { email: 'daniela.reyes@example.com', nombre: 'Daniela Reyes', texto: 'Laboratorio', menu: ['Resultados de laboratorio'], inicio: ['Resultados de laboratorio', 'Registrar resultado'] },
  Administrador: { email: 'admin@example.com', nombre: 'Administrador MediCore', texto: 'Administrador', menu: ['Pacientes', 'Médicos', 'Usuarios y roles'], inicio: ['Pacientes', 'Médicos', 'Usuarios y roles'] },
};
const ACCESO = 'e2e.acceso@example.com';
const ADMIN = ROLES.Administrador.email;

// Páginas de la web: cuáles puede abrir cada rol y cuáles no (la web redirige a "Acceso restringido").
const PERMITIDAS = {
  Recepcion: ['/Agenda', '/Pacientes', '/Pacientes/Formulario'],
  Medico: ['/MiAgenda', '/Pacientes'],
  Laboratorio: ['/Laboratorio', '/Laboratorio/Registrar'],
  Administrador: ['/Pacientes', '/Pacientes/Formulario', '/Medicos', '/Medicos/Formulario', '/Usuarios'],
};
const PROHIBIDAS = {
  Recepcion: ['/Medicos', '/Medicos/Formulario', '/Usuarios', '/MiAgenda', '/Laboratorio', '/Laboratorio/Registrar'],
  Medico: ['/Agenda', '/Agenda/Reservar', '/Pacientes/Formulario', '/Medicos', '/Usuarios', '/Laboratorio', '/Laboratorio/Registrar'],
  Laboratorio: ['/Agenda', '/Agenda/Reservar', '/Pacientes', '/Pacientes/Formulario', '/Medicos', '/Usuarios', '/MiAgenda'],
  Administrador: ['/Agenda', '/Agenda/Reservar', '/MiAgenda', '/Laboratorio', '/Laboratorio/Registrar'],
};

const devPassword = () => cy.env(['MEDICORE_PASSWORD']).then(({ MEDICORE_PASSWORD }) => MEDICORE_PASSWORD);

/** Inicio de sesión escribiendo en el formulario (sin cy.session, para probar el flujo completo). */
const loginDirecto = (email, password) => {
  cy.visit('/Login');
  cy.get('input[type=email]').clear().type(email);
  cy.get('input[type=password]').clear().type(password, { log: false });
  cy.contains('button', 'Iniciar sesión').click();
};

describe('Acceso · inicio y cierre de sesión', () => {
  beforeEach(() => {
    cy.clearAllCookies();
  });

  it('1. la pantalla de inicio de sesión tiene campos con etiqueta, está en español y no muestra datos ficticios', () => {
    cy.visit('/Login');
    cy.contains('h1', 'Inicia tu jornada');
    cy.contains('label', 'Correo electrónico').find('input[type=email]').should('exist');
    cy.contains('label', 'Contraseña').find('input[type=password]').should('exist');
    cy.get('input[type=email]').should('have.value', '');
    cy.get('input[type=password]').should('have.value', '');
    cy.get('body').should('not.contain', 'Prototipo').and('not.contain', 'Acceso simulado');
    snap('40-login');
  });

  it('2. teclado: se escribe, se pasa con Tab del correo a la contraseña y al botón, y Enter envía el formulario', () => {
    cy.visit('/Login');
    cy.get('input[type=email]').focus().type(ROLES.Recepcion.email);
    cy.press(Cypress.Keyboard.Keys.TAB);
    cy.focused().should('have.attr', 'type', 'password');
    devPassword().then((password) => cy.focused().type(password, { log: false }));
    cy.press(Cypress.Keyboard.Keys.TAB);
    cy.focused().should('contain', 'Iniciar sesión');
    // Enter dentro del campo de contraseña envía el formulario (cy.press no activa botones: es una limitación de Cypress, no de la web).
    cy.get('input[type=password]').type('{enter}');
    cy.location('pathname').should('eq', '/');
    cy.contains('.user', ROLES.Recepcion.nombre);
  });

  it('3. un formulario vacío o con correo inválido muestra un mensaje comprensible y no entra', () => {
    cy.visit('/Login');
    cy.contains('button', 'Iniciar sesión').click();
    cy.get('[role=alert]').should('contain', 'Ingresa un correo electrónico válido y tu contraseña.');
    cy.location('pathname').should('eq', '/Login');
    cy.get('input[type=email]').type('no-es-un-correo');
    cy.get('input[type=password]').type('cualquiera');
    cy.contains('button', 'Iniciar sesión').click();
    cy.get('[role=alert]').should('contain', 'Ingresa un correo electrónico válido y tu contraseña.');
  });

  it('4. credenciales incorrectas: el mismo mensaje con contraseña errónea o con un correo que no existe (no revela cuál falló)', () => {
    loginDirecto(ROLES.Recepcion.email, 'Contraseña-Incorrecta-1!');
    cy.get('[role=alert]').should('contain', 'Correo o contraseña incorrectos');
    cy.location('pathname').should('eq', '/Login');
    cy.get('[role=alert]').invoke('text').then((conContrasenaMala) => {
      loginDirecto('no.existe@example.com', 'Contraseña-Incorrecta-1!');
      cy.get('[role=alert]').invoke('text').should('eq', conContrasenaMala);
    });
    snap('41-login-incorrecto');
  });

  Object.entries(ROLES).forEach(([rol, datos]) => {
    it(`5. ${rol}: entra, ve su nombre y rol, su menú y sus accesos directos funcionan`, () => {
      devPassword().then((password) => loginDirecto(datos.email, password));
      cy.location('pathname').should('eq', '/');
      cy.contains('.user', datos.nombre).should('contain', datos.texto);
      cy.get('nav[aria-label="Navegación principal"] a.nav').then(($items) => {
        expect([...$items].map((a) => a.textContent.replace(/[^\p{L}\s]/gu, '').trim())).to.deep.equal(datos.menu);
      });
      cy.get('body').should('not.contain', 'próximas entregas');
      datos.inicio.forEach((titulo) => cy.contains('a.shortcut', titulo).should('have.attr', 'href'));
      // El primer acceso directo lleva a una pantalla del módulo (no a un error).
      cy.get('a.shortcut').first().click();
      cy.get('h1').should('exist');
      cy.get('body').should('not.contain', 'No tienes acceso a esta página');
      snap(`42-inicio-${rol}`);
    });
  });

  it('6. cerrar sesión vuelve al inicio de sesión y la sesión ya no sirve al regresar a una página protegida', () => {
    devPassword().then((password) => loginDirecto(ROLES.Recepcion.email, password));
    cy.contains('.user', ROLES.Recepcion.nombre);
    cy.contains('button', 'Cerrar sesión').click();
    cy.location('pathname').should('eq', '/Login');
    cy.visit('/Pacientes');
    cy.location('pathname').should('eq', '/Login');
    cy.location('search').should('contain', 'ReturnUrl=%2FPacientes');
    cy.go('back');
    cy.location('pathname').should('eq', '/Login');
  });

  it('7. una página protegida sin sesión pide iniciar sesión y, al entrar, regresa a esa página', () => {
    cy.visit('/Agenda');
    cy.location('pathname').should('eq', '/Login');
    cy.location('search').should('contain', 'ReturnUrl=%2FAgenda');
    devPassword().then((password) => {
      cy.get('input[type=email]').type(ROLES.Recepcion.email);
      cy.get('input[type=password]').type(password, { log: false });
    });
    cy.contains('button', 'Iniciar sesión').click();
    cy.location('pathname').should('eq', '/Agenda');
    cy.contains('h1', 'Agenda de citas');
  });

  it('8. un ReturnUrl externo no se sigue (no hay redirección abierta)', () => {
    cy.visit('/Login?ReturnUrl=https%3A%2F%2Fexample.org%2Frobo');
    devPassword().then((password) => {
      cy.get('input[type=email]').type(ROLES.Recepcion.email);
      cy.get('input[type=password]').type(password, { log: false });
    });
    cy.contains('button', 'Iniciar sesión').click();
    cy.location('origin').should('eq', Cypress.config('baseUrl'));
    cy.location('pathname').should('eq', '/');
  });
});

describe('Acceso · sesión vencida o revocada', () => {
  let accesoId;

  before(() => {
    cy.apiToken(ADMIN).then((admin) => cy.api(admin, 'GET', '/api/users').then((r) => {
      accesoId = r.body.find((u) => u.email === ACCESO).id;
      cy.api(admin, 'PUT', `/api/users/${accesoId}/role`, { role: 'Recepcion' });
    }));
  });

  after(() => {
    cy.apiToken(ADMIN).then((admin) => cy.api(admin, 'PUT', `/api/users/${accesoId}/role`, { role: 'Recepcion' }));
  });

  const entrar = () => {
    cy.clearAllCookies();
    devPassword().then((password) => loginDirecto(ACCESO, password));
    cy.contains('.user', 'Cypress Acceso');
  };

  it('9. si desaparece la cookie de sesión, la siguiente pantalla pide iniciar sesión de nuevo', () => {
    entrar();
    cy.clearCookie('medicore.auth');
    cy.visit('/Pacientes');
    cy.location('pathname').should('eq', '/Login');
  });

  it('10. si el administrador cambia el rol de la persona, su sesión se revoca y la web le pide volver a entrar', () => {
    entrar();
    cy.visit('/Pacientes');
    cy.contains('h1', 'Gestión de pacientes');
    cy.apiToken(ADMIN).then((admin) => cy.api(admin, 'PUT', `/api/users/${accesoId}/role`, { role: 'Laboratorio' }).its('status').should('be.oneOf', [200, 204]));
    cy.visit('/Pacientes');
    cy.location('pathname').should('eq', '/Login');
    // Con el rol nuevo, la persona entra y ve el menú de Laboratorio.
    devPassword().then((password) => loginDirecto(ACCESO, password));
    cy.contains('.user', 'Laboratorio');
    cy.contains('nav a.nav', 'Resultados de laboratorio');
    cy.contains('nav a.nav', 'Agenda de citas').should('not.exist');
  });
});

describe('Permisos por rol (web y API)', () => {
  Object.keys(ROLES).forEach((rol) => {
    it(`11. ${rol}: abre sus pantallas y las que no le corresponden muestran "Acceso restringido"`, () => {
      cy.loginUi(ROLES[rol].email);
      PERMITIDAS[rol].forEach((ruta) => {
        cy.visit(ruta);
        cy.location('pathname').should('eq', ruta);
        cy.get('body').should('not.contain', 'No tienes acceso a esta página');
      });
      PROHIBIDAS[rol].forEach((ruta) => {
        cy.visit(ruta, { failOnStatusCode: false });
        cy.contains('h2', 'No tienes acceso a esta página');
        cy.contains('.sub', 'comunícate con el administrador');
      });
    });
  });

  it('12. la API rechaza por rol aunque se conozca la dirección: 401 sin sesión y 403 al rol equivocado', () => {
    const api = Cypress.expose('apiUrl');
    cy.request({ url: `${api}/api/patients`, failOnStatusCode: false }).its('status').should('eq', 401);
    cy.request({ url: `${api}/api/labs/results`, failOnStatusCode: false }).its('status').should('eq', 401);

    const casos = [
      [ROLES.Recepcion.email, 'GET', '/api/labs/results', 403],
      [ROLES.Recepcion.email, 'GET', '/api/users', 403],
      [ROLES.Laboratorio.email, 'GET', '/api/patients', 403],
      [ROLES.Laboratorio.email, 'GET', '/api/appointments', 403],
      [ROLES.Medico.email, 'POST', '/api/patients', 403],
      [ROLES.Medico.email, 'GET', '/api/users', 403],
      [ADMIN, 'GET', '/api/appointments', 403],
      [ADMIN, 'GET', '/api/labs/results', 403],
      ['e2e.paciente@example.com', 'GET', '/api/users', 403],
      ['e2e.paciente@example.com', 'GET', '/api/patients', 403],
    ];
    casos.forEach(([email, metodo, ruta, esperado]) => {
      cy.apiToken(email).then((token) => cy.api(token, metodo, ruta, metodo === 'POST' ? {} : undefined).its('status').should('eq', esperado));
    });
  });

  it('13. restricción por recurso: un médico sin relación de atención no abre el expediente de un paciente ajeno (404, sin confirmar que existe)', () => {
    cy.apiToken(ROLES.Recepcion.email).then((recepcion) => cy.api(recepcion, 'GET', '/api/patients?q=PAC-002').then((r) => {
      const ajeno = r.body.items.find((p) => p.identificationNumber === 'PAC-002').id;
      cy.apiToken('e2e.colega@example.com').then((colega) => cy.api(colega, 'GET', `/api/patients/${ajeno}`).its('status').should('eq', 404));
    }));
    cy.loginUi('e2e.colega@example.com');
    cy.apiToken(ROLES.Recepcion.email).then((recepcion) => cy.api(recepcion, 'GET', '/api/patients?q=PAC-002').then((r) => {
      cy.visit(`/Pacientes/Expediente/${r.body.items[0].id}`, { failOnStatusCode: false });
      cy.contains('h2', 'No encontramos lo que buscas');
    }));
  });
});

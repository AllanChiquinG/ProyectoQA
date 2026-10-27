const fs = require('fs');
const path = require('path');
const { defineConfig } = require('cypress');
const { Client } = require('pg');

// Las pruebas necesitan la web, la API, PostgreSQL y Redis en marcha (ver README.md de esta carpeta).

/**
 * Cadena de conexión a la base de datos de DESARROLLO, solo para preparar y limpiar datos de prueba que la API no permite crear
 * (citas ya pasadas) ni borrar (información clínica). Sale de MEDICORE_E2E_DB o del archivo .env de la raíz (ignorado por Git).
 * Se niega a conectar con cualquier servidor que no sea local: nunca debe apuntar a QA ni a producción.
 */
function connectionConfig() {
  const url = process.env.MEDICORE_E2E_DB;
  let config;
  if (url) {
    const u = new URL(url);
    config = { host: u.hostname, port: Number(u.port || 5432), user: decodeURIComponent(u.username), password: decodeURIComponent(u.password), database: u.pathname.slice(1) };
  } else {
    const envFile = path.resolve(__dirname, '../../.env');
    if (!fs.existsSync(envFile)) throw new Error('Define MEDICORE_E2E_DB (postgres://usuario:clave@127.0.0.1:5433/medicore) o crea el archivo .env de la raíz.');
    const env = Object.fromEntries(fs.readFileSync(envFile, 'utf8').split(/\r?\n/).filter((l) => l.includes('=') && !l.startsWith('#')).map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]));
    config = { host: '127.0.0.1', port: Number(env.POSTGRES_PORT || 5433), user: env.POSTGRES_USER || 'medicore', password: env.POSTGRES_PASSWORD, database: env.POSTGRES_DB || 'medicore' };
  }
  if (!['127.0.0.1', 'localhost', '::1'].includes(config.host)) throw new Error(`Las pruebas solo pueden preparar datos en una base de datos local (se recibió "${config.host}").`);
  return config;
}

async function withDb(action) {
  const client = new Client(connectionConfig());
  await client.connect();
  try {
    return await action(client);
  } finally {
    await client.end();
  }
}

const pad = (n) => String(n).padStart(2, '0');
const formatDate = (d) => `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
const formatTime = (d) => `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;

const tasks = {
  /**
   * Crea una cita CONFIRMADA YA PASADA entre un médico y un paciente (la API no permite reservar en el pasado, y una consulta solo
   * se registra sobre citas de hoy o anteriores). Elige un horario alejado y aleatorio; así la prueba no depende de la hora del día.
   */
  async 'db:createPastAppointment'({ doctorId, patientId }) {
    return withDb(async (db) => {
      const createdBy = (await db.query('select "Id" from users order by "NormalizedEmail" limit 1')).rows[0].Id;
      for (let attempt = 0; attempt < 30; attempt++) {
        // Entre 2000 y 2023, en múltiplos de 30 minutos.
        const slots = Math.floor(Math.random() * (24 * 365 * 48));
        const start = new Date(Date.UTC(2000, 0, 1) + slots * 30 * 60 * 1000);
        const startText = `${formatDate(start)} ${formatTime(start)}:00`;
        const end = new Date(start.getTime() + 30 * 60 * 1000);
        const endText = `${formatDate(end)} ${formatTime(end)}:00`;
        try {
          const result = await db.query(
            `insert into appointments ("Id","PatientId","DoctorId","StartsAtLocal","EndsAtLocal","Status","Reason","CreatedByUserId","CreatedAtUtc")
             values (gen_random_uuid(), $1, $2, $3, $4, 'Confirmed', 'Cita de prueba E2E', $5, now()) returning "Id"`,
            [patientId, doctorId, startText, endText, createdBy]);
          return { id: result.rows[0].Id, date: formatDate(start), time: formatTime(start) };
        } catch (error) {
          if (error.code !== '23P01' && error.code !== '23505') throw error; // horario ocupado: se prueba otro
        }
      }
      throw new Error('No se encontró un horario libre para la cita de prueba.');
    });
  },

  /** Borra la información clínica y las citas de los pacientes de prueba (identificación CYP-E2E-*). No toca a nadie más. */
  async 'db:cleanE2eClinical'() {
    return withDb(async (db) => {
      const ids = `select "Id" from patients where "IdentificationNumber" like 'CYP-E2E-%'`;
      await db.query(`delete from prescriptions where "PatientId" in (${ids})`); // las líneas de la receta se borran en cascada
      await db.query(`delete from consultations where "PatientId" in (${ids})`);
      await db.query(`delete from appointments where "PatientId" in (${ids})`);
      return null;
    });
  },
};

module.exports = defineConfig({
  e2e: {
    baseUrl: process.env.MEDICORE_WEB_URL || 'http://localhost:5298',
    specPattern: 'e2e/**/*.cy.js',
    supportFile: 'support/e2e.js',
    video: false,
    screenshotsFolder: '../../reports/cypress/screenshots',
    trashAssetsBeforeRuns: true,
    defaultCommandTimeout: 8000,
    viewportWidth: 1280,
    viewportHeight: 800,
    setupNodeEvents(on) {
      on('task', tasks);
    },
  },
  expose: {
    apiUrl: process.env.MEDICORE_API_URL || 'http://localhost:5011',
  },
});

// Escenario crítico de MediCore: pacientes reservan citas mientras médicos consultan agenda y expediente.
// Perfiles (PERFIL): smoke, load, stress, spike. Los umbrales se fijaron antes de ejecutar: ver documentacion/07-rendimiento.md.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://proxy';
const PASSWORD = __ENV.MEDICORE_PASSWORD;
const PERFIL = __ENV.PERFIL || 'smoke';

const reservasCreadas = new Counter('reservas_creadas');
const reservasRechazadas = new Counter('reservas_rechazadas_por_conflicto');
const errorInesperado = new Rate('error_inesperado');

const perfiles = {
  smoke: [{ duration: '30s', target: 5 }],
  load: [{ duration: '1m', target: 30 }, { duration: '3m', target: 30 }, { duration: '30s', target: 0 }],
  stress: [{ duration: '1m', target: 40 }, { duration: '1m', target: 80 }, { duration: '1m', target: 150 }, { duration: '30s', target: 0 }],
  spike: [{ duration: '20s', target: 10 }, { duration: '10s', target: 200 }, { duration: '1m', target: 200 }, { duration: '10s', target: 10 }, { duration: '30s', target: 10 }],
};

// Umbrales por perfil. Stress busca el límite: sus umbrales informan, no definen la salida.
const umbrales = {
  smoke: { 'http_req_duration{tipo:lectura}': ['p(95)<800'], 'http_req_duration{tipo:reserva}': ['p(95)<1000'], error_inesperado: ['rate<0.01'] },
  load: { 'http_req_duration{tipo:lectura}': ['p(95)<800'], 'http_req_duration{tipo:reserva}': ['p(95)<1000'], error_inesperado: ['rate<0.01'] },
  stress: { error_inesperado: ['rate<0.05'] },
  spike: { 'http_req_duration{tipo:lectura}': ['p(95)<2000'], 'http_req_duration{tipo:reserva}': ['p(95)<3000'], error_inesperado: ['rate<0.02'] },
};

export const options = {
  scenarios: { demanda: { executor: 'ramping-vus', startVUs: 1, stages: perfiles[PERFIL], gracefulRampDown: '20s', gracefulStop: '20s' } },
  thresholds: umbrales[PERFIL],
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function login(email) {
  const r = http.post(`${BASE}/api/auth/login`, JSON.stringify({ email, password: PASSWORD }), { headers: { 'Content-Type': 'application/json' } });
  if (r.status !== 200) throw new Error(`No se pudo iniciar sesión con ${email}: ${r.status}`);
  return { Authorization: `Bearer ${r.json('accessToken')}`, 'Content-Type': 'application/json' };
}

export function setup() {
  const recepcion = login('elena.vargas@example.com');
  const medico = login('lucia.mendez@example.com');
  const doctors = http.get(`${BASE}/api/doctors`, { headers: recepcion }).json();
  const patients = [];
  const corrida = Date.now();
  for (let i = 0; i < 40; i++) {
    const r = http.post(`${BASE}/api/patients`, JSON.stringify({
      fullName: `Paciente Carga ${i}`, identificationNumber: `K6-${corrida}-${i}`, birthDate: '1990-05-20', phone: '5555-1234', email: `carga${i}@example.com`,
    }), { headers: recepcion });
    if (r.status === 201) patients.push(r.json('id'));
  }
  const dates = [];
  for (let d = 3; dates.length < 40; d++) {
    const date = new Date(Date.now() + d * 86400000);
    if (![0, 6].includes(date.getUTCDay())) dates.push(date.toISOString().slice(0, 10));
  }
  const medicoId = http.get(`${BASE}/api/doctors/me`, { headers: medico }).json('id');
  return { recepcion, medico, doctorIds: doctors.map((d) => d.id), patients, dates, medicoId };
}

const SLOTS = ['08:00', '08:30', '09:00', '09:30', '10:00', '10:30', '11:00', '11:30'];
const elegir = (a) => a[Math.floor(Math.random() * a.length)];

function reservar(data) {
  const doctorId = elegir(data.doctorIds);
  const date = elegir(data.dates);
  const a = http.get(`${BASE}/api/doctors/${doctorId}/availability?date=${date}`, { headers: data.recepcion, tags: { tipo: 'lectura', name: 'disponibilidad' } });
  const slot = a.status === 200 ? (a.json('slots') || []).filter((s) => s.available) : [];
  errorInesperado.add(a.status !== 200);
  if (!slot.length) return;
  const r = http.post(`${BASE}/api/appointments`, JSON.stringify({
    patientId: elegir(data.patients), doctorId, date, time: elegir(slot).time, reason: 'Carga',
  }), { headers: data.recepcion, tags: { tipo: 'reserva', name: 'reserva' }, responseCallback: http.expectedStatuses(200, 201, 409) });
  if (r.status === 201) reservasCreadas.add(1);
  if (r.status === 409) reservasRechazadas.add(1);
  errorInesperado.add(![200, 201, 409].includes(r.status));
  check(r, { 'reserva sin error inesperado': (x) => [200, 201, 409].includes(x.status) });
}

function consultar(data) {
  const date = elegir(data.dates);
  const agenda = http.get(`${BASE}/api/appointments?doctorId=${data.medicoId}&date=${date}`, { headers: data.medico, tags: { tipo: 'lectura', name: 'agenda' } });
  const pacientes = http.get(`${BASE}/api/patients?page=1&pageSize=20`, { headers: data.medico, tags: { tipo: 'lectura', name: 'pacientes' } });
  const catalogo = http.get(`${BASE}/api/doctors`, { headers: data.recepcion, tags: { tipo: 'lectura', name: 'catalogo' } });
  [agenda, pacientes, catalogo].forEach((r) => errorInesperado.add(r.status !== 200));
  const items = pacientes.status === 200 ? pacientes.json('items') || [] : [];
  if (items.length) {
    const e = http.get(`${BASE}/api/patients/${elegir(items).id}/consultations`, { headers: data.medico, tags: { tipo: 'lectura', name: 'expediente' } });
    errorInesperado.add(e.status !== 200);
  }
}

export default function (data) {
  if (__VU % 2 === 0) reservar(data); else consultar(data);
  sleep(0.5 + Math.random());
}

export function handleSummary(data) {
  return {
    [`/reportes/${PERFIL}.json`]: JSON.stringify(data, null, 2),
    stdout: `\n${PERFIL}: ${data.metrics.http_reqs.values.count} peticiones, ${data.metrics.http_reqs.values.rate.toFixed(1)} req/s\n`,
  };
}

// Dispara CITAS reservas a la vez, una por usuario virtual, cada una con un paciente distinto.
// MODO=distintas: todas en horarios libres; se crean todas.
// MODO=mismo: todas al mismo médico, día y hora; solo una puede ganar y las demás reciben 409.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://proxy';
const PASSWORD = __ENV.MEDICORE_PASSWORD;
const CITAS = Number(__ENV.CITAS || 100);
const MODO = __ENV.MODO === 'mismo' ? 'mismo' : 'distintas';
const ESPERADAS = MODO === 'mismo' ? 1 : CITAS;
const SLOTS = ['08:00', '08:30', '09:00', '09:30', '10:00', '10:30', '11:00', '11:30'];

const creadas = new Counter('citas_creadas');
const conflictos = new Counter('citas_rechazadas_409');
const errores = new Counter('citas_con_error');

export const options = {
  scenarios: { citas: { executor: 'per-vu-iterations', vus: CITAS, iterations: 1, maxDuration: '2m' } },
  thresholds: {
    citas_creadas: [`count==${ESPERADAS}`],
    citas_rechazadas_409: [`count==${CITAS - ESPERADAS}`],
    citas_con_error: ['count==0'],
  },
  setupTimeout: '3m',
};

function login(email) {
  const r = http.post(`${BASE}/api/auth/login`, JSON.stringify({ email, password: PASSWORD }), { headers: { 'Content-Type': 'application/json' } });
  if (r.status !== 200) throw new Error(`No se pudo iniciar sesión con ${email}: ${r.status}`);
  return { Authorization: `Bearer ${r.json('accessToken')}`, 'Content-Type': 'application/json' };
}

export function setup() {
  const recepcion = login('elena.vargas@example.com');
  const admin = login('admin@example.com');
  const corrida = Date.now();
  const medico = http.post(`${BASE}/api/doctors`, JSON.stringify({
    fullName: `Dr. Citas ${corrida}`, specialty: 'Carga', phone: '5555-1234', email: `citas.${corrida}@example.com`,
  }), { headers: admin });
  if (medico.status !== 201) throw new Error(`No se pudo crear el médico: ${medico.status}`);
  const doctorId = medico.json('id');
  const horario = http.put(`${BASE}/api/doctors/${doctorId}/schedule`, JSON.stringify({ days: [1, 2, 3, 4, 5], startTime: '08:00', endTime: '12:00' }), { headers: admin });
  if (horario.status !== 200 && horario.status !== 204) throw new Error(`No se pudo definir el horario: ${horario.status}`);
  const patients = [];
  for (let i = 0; i < CITAS; i++) {
    const r = http.post(`${BASE}/api/patients`, JSON.stringify({
      fullName: `Paciente Citas ${i}`, identificationNumber: `K6C-${corrida}-${i}`, birthDate: '1990-05-20', phone: '5555-1234', email: `citas${i}@example.com`,
    }), { headers: recepcion });
    if (r.status !== 201) throw new Error(`No se pudo crear el paciente: ${r.status}`);
    patients.push(r.json('id'));
  }
  const dates = [];
  for (let d = 3; dates.length * SLOTS.length < CITAS; d++) {
    const date = new Date(Date.now() + d * 86400000);
    if (![0, 6].includes(date.getUTCDay())) dates.push(date.toISOString().slice(0, 10));
  }
  return { recepcion, doctorId, patients, dates };
}

export default function (data) {
  const i = MODO === 'mismo' ? 0 : __VU - 1;
  const r = http.post(`${BASE}/api/appointments`, JSON.stringify({
    patientId: data.patients[__VU - 1], doctorId: data.doctorId,
    date: data.dates[Math.floor(i / SLOTS.length)], time: SLOTS[i % SLOTS.length], reason: 'Prueba de citas',
  }), { headers: data.recepcion, responseCallback: http.expectedStatuses(201, 409) });
  if (r.status === 201) creadas.add(1);
  else if (r.status === 409) conflictos.add(1);
  else errores.add(1);
  check(r, { 'creada o rechazada con 409': (x) => [201, 409].includes(x.status) });
}

export function handleSummary(data) {
  const n = (m) => (data.metrics[m] ? data.metrics[m].values.count : 0);
  const d = data.metrics.http_req_duration.values;
  return {
    [`/reportes/citas-${MODO}.json`]: JSON.stringify(data, null, 2),
    stdout: `\n${CITAS} reservas (${MODO}): ${n('citas_creadas')} creadas, ${n('citas_rechazadas_409')} rechazadas con 409, ${n('citas_con_error')} con error, p95 ${d['p(95)'].toFixed(1)} ms\n`,
  };
}

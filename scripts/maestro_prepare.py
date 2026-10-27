"""Prepara los datos aislados de cada flujo Maestro (paciente de pruebas e2e.paciente@example.com, identificación CYP-E2E-004).

Usa la API real (con los usuarios ficticios de desarrollo) y, solo para lo que la API no permite crear (una cita ya pasada para la receta), la base
de PostgreSQL de docker compose. Borra y vuelve a crear únicamente datos del paciente CYP-E2E-004; no toca a nadie más.

Requiere: docker compose up -d, la API en http://localhost:5011 en Development y MEDICORE_PASSWORD (= Seed:DevPassword).

Escenarios: base | cita | cancelar | resultados | recetas | notificaciones
Uso:  python scripts/maestro_prepare.py <escenario>      (imprime las variables para "maestro test -e ...", una por línea CLAVE=valor)
"""
import datetime
import json
import os
import pathlib
import random
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding='utf-8')
ROOT = pathlib.Path(__file__).resolve().parent.parent
API = os.environ.get('MEDICORE_API_URL', 'http://localhost:5011')
PASSWORD = os.environ.get('MEDICORE_PASSWORD')

PACIENTE_EMAIL = 'e2e.paciente@example.com'
PACIENTE_ID_NUMBER = 'CYP-E2E-004'
ADMIN, LAB, MEDICO, COLEGA = 'admin@example.com', 'daniela.reyes@example.com', 'e2e.medico@example.com', 'e2e.colega@example.com'
COLEGA_NOMBRE, MEDICO_NOMBRE = 'Dra. Cypress Colega', 'Dr. Cypress E2E'
EXAMEN = 'Hematología Maestro'
MEDICAMENTO = 'Paracetamol Maestro'
CLINIC_TZ = datetime.timezone(datetime.timedelta(hours=-6))  # America/Guatemala no tiene horario de verano


def clinic_today():
    return datetime.datetime.now(CLINIC_TZ).date()


def call(method, path, token=None, body=None, key=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = f'Bearer {token}'
    if key:
        headers['Idempotency-Key'] = key
    request = urllib.request.Request(API + path, method=method, data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        raw = error.read()
        try:
            return error.code, json.loads(raw) if raw else None
        except ValueError:
            return error.code, None


def login(email):
    status, body = call('POST', '/api/auth/login', body={'email': email, 'password': PASSWORD})
    if status != 200:
        raise SystemExit(f'No se pudo iniciar sesión como {email} ({status}). ¿Está la API en Development con los usuarios sembrados?')
    return body['accessToken']


def psql(sql):
    # Por defecto el compose de desarrollo del repositorio; para el ambiente QA: MEDICORE_COMPOSE_ARGS="-p medicore-qa --env-file .env.qa -f docker-compose.yml -f docker-compose.qa.yml"
    compose_args = os.environ.get('MEDICORE_COMPOSE_ARGS', '').split()
    result = subprocess.run(['docker', 'compose', *compose_args, 'exec', '-T', 'postgres', 'sh', '-c', 'psql -t -A -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$0"', sql],
                            cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f'psql falló: {result.stderr.strip()}')
    return result.stdout.strip()


def clean_patient():
    """Deja al paciente de pruebas sin citas, consultas, recetas, resultados, notificaciones ni eventos."""
    pid = f"select \"Id\" from patients where \"IdentificationNumber\" = '{PACIENTE_ID_NUMBER}'"
    psql(f'delete from notifications where "PatientId" in ({pid})')
    psql("delete from outbox_messages where \"Type\"='LabResultReady' and \"Payload\"->>'patientId' in (select \"Id\"::text from patients where \"IdentificationNumber\" = '" + PACIENTE_ID_NUMBER + "')")
    psql(f'delete from lab_results where "PatientId" in ({pid})')
    psql(f'delete from prescriptions where "PatientId" in ({pid})')
    psql(f'delete from consultations where "PatientId" in ({pid})')
    psql(f'delete from appointments where "PatientId" in ({pid})')


def doctor_id(admin_token, name):
    status, doctors = call('GET', '/api/doctors', admin_token)
    found = [d for d in doctors if d['fullName'] == name]
    if not found:
        raise SystemExit(f'No existe el médico de pruebas "{name}": arranca la API en Development para sembrarlo.')
    return found[0]['id']


def wait_for(condition, seconds=40):
    deadline = time.time() + seconds
    while time.time() < deadline:
        if condition():
            return True
        time.sleep(1)
    return condition()


def prepare(scenario):
    if not PASSWORD:
        raise SystemExit('Define MEDICORE_PASSWORD con el valor de Seed:DevPassword.')
    admin, lab, paciente = login(ADMIN), login(LAB), login(PACIENTE_EMAIL)
    status, patients = call('GET', f'/api/labs/patients?q={PACIENTE_ID_NUMBER}', lab)
    if status != 200 or not patients:
        raise SystemExit('No existe el paciente de pruebas CYP-E2E-004: arranca la API en Development para sembrarlo.')
    patient_id = patients[0]['id']
    colega_id = doctor_id(admin, COLEGA_NOMBRE)

    clean_patient()
    # La colega de pruebas atiende todos los días de 08:00 a 12:00 (no se toca la agenda de ningún médico de uso normal).
    status, _ = call('PUT', f'/api/doctors/{colega_id}/schedule', admin, {'days': [0, 1, 2, 3, 4, 5, 6], 'startTime': '08:00', 'endTime': '12:00'})
    assert status == 200, f'horario de la colega: {status}'

    env = {'EMAIL': PACIENTE_EMAIL, 'PASSWORD': PASSWORD, 'NOMBRE': 'Cypress', 'MEDICO': COLEGA_NOMBRE}

    if scenario == 'cita':
        target = clinic_today() + datetime.timedelta(days=3)
        env.update({'DIA': str(target.day), 'MES_SIGUIENTE': str(target.month != clinic_today().month).lower(), 'HORA': '09:00', 'FECHA': target.isoformat()})

    elif scenario == 'cancelar':
        target = clinic_today() + datetime.timedelta(days=5)
        status, body = call('POST', '/api/appointments', paciente, {'doctorId': colega_id, 'date': target.isoformat(), 'time': '10:00'}, key=f'maestro-{int(time.time())}')
        assert status in (200, 201), f'cita del paciente: {status} {body}'
        env['FECHA'] = target.isoformat()

    elif scenario in ('resultados', 'notificaciones'):
        yesterday = clinic_today() - datetime.timedelta(days=1)
        status, created = call('POST', '/api/labs/results', lab, {
            'patientId': patient_id, 'examType': EXAMEN, 'performedOn': yesterday.isoformat(), 'notes': 'Resultado de prueba Maestro',
            'parameters': [{'name': 'Hemoglobina', 'value': '13.2', 'unit': 'g/dL', 'referenceRange': '12.0–16.0'}]})
        assert status == 201, f'resultado: {status}'
        # La notificación se genera de forma asíncrona (outbox -> RabbitMQ -> consumidor): se espera a que exista.
        ready = wait_for(lambda: call('GET', '/api/notifications/unread-count', paciente)[1]['count'] >= 1)
        assert ready, 'La notificación no se generó: ¿está RabbitMQ en marcha?'
        env['EXAMEN'] = EXAMEN

    elif scenario == 'recetas':
        medico = login(MEDICO)
        status, me = call('GET', '/api/doctors/me', medico)
        assert status == 200
        created_by = psql('select "Id" from users order by "NormalizedEmail" limit 1')
        for _ in range(30):  # cita confirmada ya pasada (la API no permite reservar en el pasado)
            start = datetime.datetime(2000, 1, 1) + datetime.timedelta(minutes=30 * random.randrange(24 * 365 * 48))
            end = start + datetime.timedelta(minutes=30)
            row = psql(f"""insert into appointments ("Id","PatientId","DoctorId","StartsAtLocal","EndsAtLocal","Status","Reason","CreatedByUserId","CreatedAtUtc")
                           select gen_random_uuid(), p."Id", '{me['id']}', '{start:%Y-%m-%d %H:%M:00}', '{end:%Y-%m-%d %H:%M:00}', 'Confirmed', 'Cita de prueba Maestro', '{created_by}', now()
                           from patients p where p."IdentificationNumber" = '{PACIENTE_ID_NUMBER}' on conflict do nothing returning "Id" """)
            if row:
                appointment_id = row.splitlines()[0]
                break
        else:
            raise SystemExit('No se encontró un horario libre para la cita de prueba.')
        status, consultation = call('POST', '/api/consultations', medico, {'appointmentId': appointment_id, 'reason': 'Motivo Maestro', 'notes': 'Notas Maestro', 'diagnosis': 'Diagnóstico Maestro'})
        assert status == 201, f'consulta: {status} {consultation}'
        status, prescription = call('POST', '/api/prescriptions', medico, {'consultationId': consultation['id'], 'items': [
            {'medication': MEDICAMENTO, 'dose': '500 mg', 'frequency': 'Cada 8 horas', 'duration': '3 días', 'instructions': 'Tomar con alimentos'}]})
        assert status == 201, f'receta: {status} {prescription}'
        env['MEDICAMENTO'] = MEDICAMENTO

    elif scenario != 'base':
        raise SystemExit(f'Escenario desconocido: {scenario}')

    return env


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    for key, value in prepare(sys.argv[1]).items():
        print(f'{key}={"***" if key == "PASSWORD" else value}')   # la contraseña no se imprime

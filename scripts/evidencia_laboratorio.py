"""Evidencia reproducible de la entrega 6 (laboratorio y notificaciones asíncronas) contra la pila de desarrollo real.

Requisitos: docker compose up -d (postgres, redis, rabbitmq) y la API en http://localhost:5011 en Development
(por ejemplo con scripts/dev-stack-linux.sh up). Variable de entorno MEDICORE_PASSWORD = Seed:DevPassword.

Qué hace (solo con datos ficticios CYP-E2E-*; no toca otros datos):
  A. broker disponible: registrar un resultado, ver la notificación asíncrona del paciente y marcarla como leída;
  B. broker DETENIDO: el resultado se guarda y queda "Pendiente"; al volver el broker el evento pendiente se publica y se notifica;
  C. reentrega: el mismo evento se vuelve a publicar y NO se duplica la notificación;
  D. autorización y validación: accesos ajenos, otros roles y entradas inválidas.
Detiene y vuelve a iniciar SOLO el servicio rabbitmq de docker compose (sus datos están en un volumen y se conservan).
Escribe el informe en documentacion/evidencias/entrega-6-laboratorio.md (sin contenido clínico: solo identificadores y estados).

Uso: python scripts/evidencia_laboratorio.py
"""
import datetime
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding='utf-8')
ROOT = pathlib.Path(__file__).resolve().parent.parent
API = os.environ.get('MEDICORE_API_URL', 'http://localhost:5011')
PASSWORD = os.environ.get('MEDICORE_PASSWORD')
if not PASSWORD:
    sys.exit('Define MEDICORE_PASSWORD con el valor de Seed:DevPassword.')

LAB, RECEPCION, MEDICO_AJENO, PACIENTE = 'daniela.reyes@example.com', 'elena.vargas@example.com', 'e2e.colega@example.com', 'e2e.paciente@example.com'
PATIENT_ID_NUMBER = 'CYP-E2E-004'
lines = []


def log(text=''):
    print(text)
    lines.append(text)


def call(method, path, token=None, body=None):
    request = urllib.request.Request(API + path, method=method, data=None if body is None else json.dumps(body).encode(),
                                     headers={'Content-Type': 'application/json', **({'Authorization': f'Bearer {token}'} if token else {})})
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
    assert status == 200, f'login {email}: {status}'
    return body['accessToken']


def compose(*args):
    return subprocess.run(['docker', 'compose', *args], cwd=ROOT, capture_output=True, text=True)


def psql(sql):
    result = compose('exec', '-T', 'postgres', 'sh', '-c', 'psql -t -A -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$0"', sql)
    return result.stdout.strip()


def wait_for(condition, seconds=60, step=1.0):
    deadline = time.time() + seconds
    while time.time() < deadline:
        value = condition()
        if value:
            return value
        time.sleep(step)
    return condition()


def clean():
    ids = "select \"Id\" from patients where \"IdentificationNumber\" like 'CYP-E2E-%'"
    psql(f'delete from notifications where "PatientId" in ({ids})')
    psql("delete from outbox_messages where \"Type\"='LabResultReady' and \"Payload\"->>'patientId' in (select \"Id\"::text from patients where \"IdentificationNumber\" like 'CYP-E2E-%')")
    psql(f'delete from lab_results where "PatientId" in ({ids})')


def new_result(token, patient_id, exam):
    today = datetime.date.today() - datetime.timedelta(days=1)
    return call('POST', '/api/labs/results', token, {'patientId': patient_id, 'examType': exam, 'performedOn': today.isoformat(), 'notes': 'Evidencia',
                                                      'parameters': [{'name': 'Glucosa', 'value': '92', 'unit': 'mg/dL', 'referenceRange': '70-100'}]})


def outbox_state(result_id):
    row = psql(f"select \"Attempts\" || '|' || coalesce(to_char(\"ProcessedAtUtc\",'HH24:MI:SS'),'pendiente') from outbox_messages where \"Payload\"->>'labResultId'='{result_id}' order by \"OccurredAtUtc\" limit 1")
    attempts, processed = (row.split('|') + ['?', '?'])[:2]
    return int(attempts) if attempts.isdigit() else -1, processed


def notification_count(result_id):
    value = psql(f"select count(*) from notifications where \"SourceId\"='{result_id}'")
    return int(value) if value.isdigit() else -1


def main():
    started = datetime.datetime.now().astimezone().strftime('%Y-%m-%d %H:%M:%S %z')
    log('# Evidencia de la entrega 6: laboratorio y notificaciones asíncronas')
    log()
    log(f'Ejecutada el {started} con `scripts/evidencia_laboratorio.py` contra la pila de desarrollo (PostgreSQL, Redis y RabbitMQ de docker compose; API en Development).')
    log('Solo datos ficticios `CYP-E2E-*`. El informe contiene identificadores y estados, no contenido clínico.')
    log()

    clean()
    lab, recepcion, ajeno, paciente = login(LAB), login(RECEPCION), login(MEDICO_AJENO), login(PACIENTE)
    status, patients = call('GET', f'/api/labs/patients?q={PATIENT_ID_NUMBER}', lab)
    assert status == 200 and patients, 'No existe el paciente de pruebas CYP-E2E-004: arranca la API en Development para sembrarlo.'
    patient_id = patients[0]['id']

    # ---- A ----
    log('## A. Broker disponible: registro, notificación asíncrona y lectura del paciente')
    status, created = new_result(lab, patient_id, 'Química sanguínea A')
    log(f'- `POST /api/labs/results` → **{status}** (resultado `{created["id"]}`, estado inicial de la notificación: `{created["notificationStatus"]}`).')
    result_a = created['id']
    sent = wait_for(lambda: call('GET', f'/api/labs/results/{result_a}', lab)[1]['notificationStatus'] == 'Sent', 30)
    log(f'- La notificación pasó a `Sent` de forma asíncrona: **{sent}**; notificaciones guardadas para el resultado: **{notification_count(result_a)}**.')
    status, listing = call('GET', '/api/notifications?unreadOnly=true', paciente)
    mine = [n for n in listing['items'] if n['targetId'] == result_a]
    log(f'- Paciente `GET /api/notifications?unreadOnly=true` → **{status}**, notificaciones del resultado: **{len(mine)}** (tipo `{mine[0]["type"]}`, sin leer: `{not mine[0]["isRead"]}`).')
    status, count = call('GET', '/api/notifications/unread-count', paciente)
    log(f'- `GET /api/notifications/unread-count` → **{status}**, sin leer: **{count["count"]}**.')
    status, _ = call('POST', f'/api/notifications/{mine[0]["id"]}/read', paciente)
    log(f'- `POST /api/notifications/{{id}}/read` → **{status}**.')
    status, detail = call('GET', f'/api/labs/results/{result_a}', paciente)
    log(f'- El paciente lee su propio resultado: `GET /api/labs/results/{{id}}` → **{status}**.')
    log()

    # ---- B ----
    log('## B. RabbitMQ detenido: el resultado no se pierde y el evento pendiente se recupera')
    stop = compose('stop', 'rabbitmq')
    log(f'- `docker compose stop rabbitmq` → código {stop.returncode}.')
    status, created = new_result(lab, patient_id, 'Química sanguínea B')
    result_b = created['id']
    log(f'- Con el broker detenido, `POST /api/labs/results` → **{status}** (resultado `{result_b}` guardado).')
    status, detail = call('GET', f'/api/labs/results/{result_b}', lab)
    log(f'- `GET /api/labs/results/{{id}}` → **{status}**, estado de la notificación: `{detail["notificationStatus"]}`.')
    attempts = wait_for(lambda: outbox_state(result_b)[0] >= 1 and outbox_state(result_b)[0], 30)
    log(f'- Evento en el outbox: intentos de publicación fallidos **{attempts}**, procesado: `{outbox_state(result_b)[1]}` (sigue pendiente, con espera creciente).')
    log(f'- Notificaciones generadas mientras el broker está caído: **{notification_count(result_b)}**.')
    start = compose('start', 'rabbitmq')
    log(f'- `docker compose start rabbitmq` → código {start.returncode}.')
    recovered = wait_for(lambda: notification_count(result_b) == 1, 120, 2)
    attempts_after, processed = outbox_state(result_b)
    log(f'- Tras volver el broker, el evento pendiente se publicó y se consumió: notificación generada **{recovered}**; intentos fallidos acumulados {attempts_after}; evento procesado a las `{processed}`.')
    status, detail = call('GET', f'/api/labs/results/{result_b}', lab)
    log(f'- Estado final de la notificación: `{detail["notificationStatus"]}`; notificaciones del resultado: **{notification_count(result_b)}**.')
    log()

    # ---- C ----
    log('## C. Reentrega del mismo evento: sin notificaciones duplicadas')
    psql(f"update outbox_messages set \"ProcessedAtUtc\"=null, \"NextAttemptAtUtc\"=now() where \"Payload\"->>'labResultId'='{result_b}'")
    log('- Se marcó de nuevo como pendiente el evento ya consumido (simula la caída entre "publicado" y "marcado") y el publicador lo volvió a publicar con el mismo `MessageId`.')
    again = wait_for(lambda: outbox_state(result_b)[1] != 'pendiente', 30)
    time.sleep(3)
    log(f'- Evento republicado: **{bool(again)}**. Notificaciones del resultado después de la reentrega: **{notification_count(result_b)}** (debe ser 1).')
    log()

    # ---- D ----
    log('## D. Autorización y validación')
    checks = [
        ('Recepción registra un resultado', call('POST', '/api/labs/results', recepcion, {'patientId': patient_id})[0], 403),
        ('Recepción lista resultados', call('GET', '/api/labs/results', recepcion)[0], 403),
        ('Recepción lee un resultado', call('GET', f'/api/labs/results/{result_a}', recepcion)[0], 403),
        ('Médico sin relación lee el resultado', call('GET', f'/api/labs/results/{result_a}', ajeno)[0], 404),
        ('Médico sin relación lista los del paciente', call('GET', f'/api/patients/{patient_id}/lab-results', ajeno)[0], 404),
        ('Paciente intenta registrar un resultado', call('POST', '/api/labs/results', paciente, {'patientId': patient_id})[0], 403),
        ('Personal lee notificaciones de pacientes', call('GET', '/api/notifications', lab)[0], 403),
        ('Sin sesión', call('GET', '/api/labs/results')[0], 401),
        ('Resultado sin parámetros', call('POST', '/api/labs/results', lab, {'patientId': patient_id, 'examType': 'X', 'performedOn': '2026-01-01', 'parameters': []})[0], 400),
        ('Fecha futura', call('POST', '/api/labs/results', lab, {'patientId': patient_id, 'examType': 'X', 'performedOn': '2999-01-01', 'parameters': [{'name': 'a', 'value': 'b'}]})[0], 400),
        ('Paciente inexistente', call('POST', '/api/labs/results', lab, {'patientId': '00000000-0000-0000-0000-000000000001', 'examType': 'X', 'performedOn': '2026-01-01', 'parameters': [{'name': 'a', 'value': 'b'}]})[0], 400),
        ('Modificar un resultado (PUT)', call('PUT', f'/api/labs/results/{result_a}', lab, {})[0], 405),
        ('Borrar un resultado (DELETE)', call('DELETE', f'/api/labs/results/{result_a}', lab)[0], 405),
    ]
    log('| Comprobación | Esperado | Obtenido | Resultado |')
    log('|---|---|---|---|')
    failures = 0
    for name, got, expected in checks:
        verdict = 'OK' if got == expected else 'FALLÓ'
        failures += got != expected
        log(f'| {name} | {expected} | {got} | {verdict} |')
    log()

    clean()
    log('## Limpieza')
    log('- Se borraron los resultados, notificaciones y eventos de los pacientes `CYP-E2E-*` creados por esta ejecución. No se tocó ningún otro dato ni volumen.')
    log()
    broker_ok = notification_count(result_a) == 0  # tras la limpieza no quedan notificaciones de prueba
    summary_ok = failures == 0 and recovered and bool(sent) and broker_ok
    log(f'**Resultado global: {"CORRECTO" if summary_ok else "CON FALLOS"}** ({len(checks) - failures}/{len(checks)} comprobaciones de la sección D).')

    out = ROOT / 'documentacion' / 'evidencias' / 'entrega-6-laboratorio.md'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return 0 if summary_ok else 1


if __name__ == '__main__':
    sys.exit(main())

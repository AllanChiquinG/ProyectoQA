"""Evidencia del ambiente QA: detiene Redis, el Worker y RabbitMQ, y comprueba el comportamiento real.

Requiere QA en marcha y sembrado. Escribe documentacion/evidencias/resiliencia-qa.md, solo con estados e identificadores.
Si algo falla, vuelve a iniciar los servicios antes de terminar.
"""
import datetime
import json
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8080"
PASSWORD = next(line.split("=", 1)[1].strip() for line in pathlib.Path(".env.qa").read_text(encoding="utf-8").splitlines() if line.startswith("SEED_DEV_PASSWORD="))
REDIS, WORKER, BROKER = "medicore-qa-redis-1", "medicore-qa-worker-1", "medicore-qa-rabbitmq-1"
lines = []


def log(text=""):
    lines.append(text)
    print(text)


def call(method, path, token=None, body=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        return error.code, None


def login(email):
    code, body = call("POST", "/api/auth/login", body={"email": email, "password": PASSWORD})
    return code, (body or {}).get("accessToken")


def docker(action, name):
    subprocess.run(["docker", action, name], check=True, capture_output=True)


def wait_healthy(name, seconds=90):
    limit = time.time() + seconds
    while time.time() < limit:
        state = subprocess.run(["docker", "inspect", "-f", "{{.State.Health.Status}}", name], capture_output=True, text=True).stdout.strip()
        if state == "healthy":
            return True
        time.sleep(1)
    return False


def status_of(token, result_id):
    code, body = call("GET", f"/api/labs/results/{result_id}", token)
    return code, (body or {}).get("notificationStatus")


def new_result(token, patient_id, exam):
    today = datetime.date.today().isoformat()
    body = {"patientId": patient_id, "examType": exam, "performedOn": today, "notes": "Prueba de resiliencia",
            "parameters": [{"name": "Glucosa", "value": "92", "unit": "mg/dL", "referenceRange": "70-100"}]}
    code, created = call("POST", "/api/labs/results", token, body)
    return code, (created or {}).get("id")


def wait_sent(token, result_id, seconds=180):
    start = time.time()
    while time.time() - start < seconds:
        if status_of(token, result_id)[1] == "Sent":
            return round(time.time() - start, 1)
        time.sleep(1)
    return None


def notifications_for(token, result_id):
    _, body = call("GET", "/api/notifications?pageSize=100", token)
    items = (body or {}).get("items", [])
    return sum(1 for item in items if str(item.get("targetId", "")).lower() == result_id.lower())


def redis():
    log("## 1. Redis detenido")
    log()
    _, token = login("elena.vargas@example.com")
    log(f"- Antes, `GET /api/doctors` con sesión: **{call('GET', '/api/doctors', token)[0]}**")
    docker("stop", REDIS)
    try:
        log(f"- `docker stop` de Redis. Login: **{login('elena.vargas@example.com')[0]}**")
        log(f"- El mismo token: **{call('GET', '/api/doctors', token)[0]}**")
    finally:
        docker("start", REDIS)
    wait_healthy(REDIS)
    time.sleep(3)
    code, token = login("elena.vargas@example.com")
    log(f"- Redis iniciado de nuevo. Login: **{code}**, `GET /api/doctors`: **{call('GET', '/api/doctors', token)[0]}**")
    log()


def outage(title, service, lab, patient, patient_id, exam):
    log(f"## {title}")
    log()
    docker("stop", service)
    try:
        code, result_id = new_result(lab, patient_id, exam)
        time.sleep(5)
        log(f"- `docker stop` de `{service}`. Registrar resultado: **{code}**")
        log(f"- 5 segundos después, lectura: **{status_of(lab, result_id)[0]}**, notificación: **{status_of(lab, result_id)[1]}**")
    finally:
        docker("start", service)
    wait_healthy(service)
    log(f"- Servicio iniciado de nuevo. Notificación enviada en **{wait_sent(lab, result_id)} s**, estado: **{status_of(lab, result_id)[1]}**")
    time.sleep(2)
    log(f"- Notificaciones del paciente para ese resultado: **{notifications_for(patient, result_id)}**")
    log()


def main():
    code, lab = login("daniela.reyes@example.com")
    if code != 200:
        sys.exit(f"No se pudo iniciar sesión en QA: {code}")
    _, patient = login("e2e.paciente@example.com")
    _, me = call("GET", "/api/patients/me", patient)
    log("# Evidencia de resiliencia en QA")
    log()
    log(f"Ejecutada el {datetime.datetime.now():%Y-%m-%d %H:%M} con `scripts/evidencia_resiliencia.py` sobre el ambiente QA en contenedores. Solo datos ficticios.")
    log()
    redis()
    _, lab = login("daniela.reyes@example.com")
    _, patient = login("e2e.paciente@example.com")
    outage("2. Worker detenido", WORKER, lab, patient, me["id"], "Resiliencia worker")
    outage("3. RabbitMQ detenido", BROKER, lab, patient, me["id"], "Resiliencia broker")
    out = pathlib.Path("documentacion/evidencias/resiliencia-qa.md")
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

"""Prepara el contrato OpenAPI para escanear la API con un rol: inicia sesión, busca identificadores reales de QA y los pone en las rutas.

Sin identificadores reales, ZAP solo recibe 404 y no llega al código clínico. Se quitan las rutas que cerrarían la sesión o cambiarían permisos.
Imprime el token para sec-zap.sh; la contraseña llega por la variable ZAP_PASSWORD.

Uso: zap-openapi.py --url http://proxy --email correo --spec openapi-v1.json --out salida.json [--auth]
"""
import argparse
import datetime
import json
import os
import sys
import urllib.error
import urllib.request

DOCTORS = "/api/doctors"
LAB_RESULTS = "/api/labs/results"
FICTITIOUS = "Dato ficticio"
REASON = "Control ficticio"
ALWAYS_EXCLUDED = ("/api/auth/logout", "/api/auth/refresh", "/api/users/{id}/role")
# Plantilla de ruta -> tipo de identificador que necesita.
KINDS = {
    "/api/appointments/{id}": "appointment",
    "/api/appointments/{id}/cancel": "appointment",
    "/api/consultations/{id}": "consultation",
    DOCTORS + "/{id}": "doctor",
    DOCTORS + "/{doctorId}/availability": "doctor",
    DOCTORS + "/{doctorId}/schedule": "doctor",
    LAB_RESULTS + "/{id}": "lab",
    LAB_RESULTS + "/{id}/notification/retry": "lab",
    "/api/notifications/{id}/read": "notification",
    "/api/patients/{id}": "patient",
    "/api/patients/{patientId}/consultations": "patient",
    "/api/patients/{patientId}/lab-results": "patient",
    "/api/patients/{patientId}/prescriptions": "patient",
    "/api/prescriptions/{id}": "prescription",
}
# De dónde sale cada tipo; se usa la primera fuente que devuelva datos. {patient} recorre los pacientes visibles para el rol.
SOURCES = {
    "patient": ["/api/patients/me", "/api/patients", "/api/labs/patients?q=Ana"],
    "doctor": [DOCTORS + "/me", DOCTORS],
    "appointment": ["/api/appointments/me", "/api/appointments/consultable", "/api/appointments"],
    "consultation": ["/api/patients/{patient}/consultations"],
    "prescription": ["/api/prescriptions/me", "/api/patients/{patient}/prescriptions"],
    "lab": [LAB_RESULTS + "/me", LAB_RESULTS, "/api/patients/{patient}/lab-results"],
    "notification": ["/api/notifications"],
}


def call(url, token=None, body=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(url, data=json.dumps(body).encode() if body else None, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return json.load(response)
    except (urllib.error.HTTPError, ValueError):
        return None


def ids(value):
    """Identificadores del primer nivel de una respuesta: un objeto, una lista o una página con "items"."""
    if isinstance(value, dict):
        if "id" in value:
            return [str(value["id"])]
        pages = [value[key] for key in ("items", "data", "results") if isinstance(value.get(key), list)]
        return ids(pages[0]) if pages else []
    if isinstance(value, list):
        return [str(item["id"]) for item in value if isinstance(item, dict) and "id" in item]
    return []


def first_ids(base, token, urls):
    """Identificadores de la primera dirección que devuelva datos."""
    for url in urls:
        values = ids(call(base + url, token))
        if values:
            return values
    return []


def expand(sources, patients):
    """Cambia {patient} por cada paciente visible para el rol."""
    urls = []
    for source in sources:
        urls += [source.replace("{patient}", patient) for patient in patients[:25]] if "{patient}" in source else [source]
    return urls


def resolve(base, token):
    patients = first_ids(base, token, SOURCES["patient"])
    found = {"patient": patients[0]} if patients else {}
    today = datetime.date.today()
    agenda = [f"/api/appointments?doctorId={doctor}&date={today + datetime.timedelta(days=offset)}"
              for doctor in ids(call(base + DOCTORS, token))[:5] for offset in range(-3, 15)]
    for kind, sources in SOURCES.items():
        extra = agenda if kind == "appointment" else []
        values = first_ids(base, token, expand(sources, patients) + extra) if kind != "patient" else []
        if values:
            found[kind] = values[0]
    for patient in patients[:25]:
        values = ids(call(f"{base}/api/appointments/consultable?patientId={patient}", token))
        if values:
            found["consultable"], found["consultable_patient"] = values[0], patient
            break
    return found


def free_slot(base, token):
    """Primer horario libre de los próximos días: un cuerpo de reserva válido necesita uno."""
    today = datetime.date.today()
    days = [str(today + datetime.timedelta(days=offset)) for offset in range(1, 15)]
    doctors = ids(call(base + DOCTORS + "/me", token)) + ids(call(base + DOCTORS, token))[:5]
    for doctor, day in ((doctor, day) for doctor in doctors for day in days):
        slots = (call(f"{base}{DOCTORS}/{doctor}/availability?date={day}", token) or {}).get("slots", [])
        free = [slot["time"] for slot in slots if slot.get("available")]
        if free:
            return doctor, day, free[-1]
    return None, None, None


def samples(base, token, found):
    """Cuerpos válidos para que ZAP pase la validación y analice la lógica de cada operación clínica; devuelve {(ruta, método): cuerpo} y los parámetros."""
    doctor, day, time = free_slot(base, token)
    booking = {"doctorId": doctor, "date": day, "time": time, "reason": REASON}
    if not ids(call(base + "/api/patients/me", token)):
        booking["patientId"] = found.get("patient")
    bodies = {
        ("/api/appointments", "post"): booking,
        (LAB_RESULTS, "post"): {"patientId": found.get("patient"), "examType": "Glucosa", "performedOn": str(datetime.date.today()), "notes": FICTITIOUS,
                                "parameters": [{"name": "Glucosa", "value": "90", "unit": "mg/dL", "referenceRange": "70-100"}]},
        ("/api/consultations", "post"): {"appointmentId": found.get("consultable"), "reason": REASON, "notes": FICTITIOUS, "diagnosis": "Sin hallazgos"},
        ("/api/prescriptions", "post"): {"consultationId": found.get("consultation"), "items": [
            {"medication": "Acetaminofén", "dose": "500 mg", "frequency": "Cada 8 horas", "duration": "3 días", "instructions": FICTITIOUS}]},
    }
    schedule = call(f"{base}{DOCTORS}/{found.get('doctor')}/schedule", token)
    if schedule:
        bodies[(DOCTORS + "/{doctorId}/schedule", "put")] = {key: schedule[key] for key in ("days", "startTime", "endTime")}
    query = {"date": day, "patientId": found.get("consultable_patient") or found.get("patient"), "doctorId": found.get("doctor")}
    return bodies, query


def sample_parameters(operation, query):
    # La cabecera es opcional y ZAP genera un valor que no cumple su largo: sin ella, la reserva llega a la lógica de negocio.
    parameters = [p for p in operation.get("parameters", []) if p.get("name") != "Idempotency-Key"]
    for parameter in parameters:
        value = query.get(parameter["name"]) if parameter.get("in") == "query" else None
        if value:
            parameter["example"] = value
            parameter.setdefault("schema", {})["example"] = value
    if "parameters" in operation:
        operation["parameters"] = parameters


def with_samples(path, value, bodies, query):
    """Pone ejemplos en el contrato; ZAP los usa en lugar de inventar valores que la API rechazaría."""
    for method, operation in value.items():
        if not isinstance(operation, dict):
            continue
        sample_parameters(operation, query)
        body = bodies.get((path, method))
        media = operation.get("requestBody", {}).get("content", {}).get("application/json")
        if body and media:
            media["example"] = body
            media["schema"] = {"type": "object", "example": body}


def lab_fixture(base, token, name):
    """Da un resultado (y su notificación) a la paciente, para que Médico y Paciente tengan datos que analizar."""
    patient = ids(call(base + "/api/labs/patients?q=" + name, token))
    result = call(base + LAB_RESULTS, token, {
        "patientId": patient[0] if patient else None, "examType": "Hemograma", "performedOn": str(datetime.date.today()),
        "notes": FICTITIOUS, "parameters": [{"name": "Hemoglobina", "value": "14"}]})
    if not result:
        print("No se pudo crear el resultado de laboratorio ficticio", file=sys.stderr)


def concrete(path, value, kind, identifier):
    """Ruta con el identificador real; ya no necesita su parámetro de ruta."""
    for operation in value.values():
        if isinstance(operation, dict) and "parameters" in operation:
            operation["parameters"] = [p for p in operation["parameters"] if p.get("in") != "path"]
    return path.replace("{id}", identifier).replace("{doctorId}", identifier).replace("{patientId}", identifier)


def build_paths(spec, found, bodies, query, include_auth):
    paths = {}
    for path, value in spec["paths"].items():
        if path in ALWAYS_EXCLUDED or (path.startswith("/api/auth/") and not include_auth):
            continue
        with_samples(path, value, bodies, query)
        kind = KINDS.get(path)
        if kind is None:
            paths[path] = value
        elif kind in found:
            paths[concrete(path, value, kind, found[kind])] = value
    return paths


def main():
    parser = argparse.ArgumentParser()
    for name in ("url", "email", "spec", "out"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--auth", action="store_true", help="incluye /api/auth/login; solo en un perfil para no repetirlo")
    parser.add_argument("--lab-fixture", help="nombre de la paciente a la que se registra un resultado ficticio")
    args = parser.parse_args()

    login = call(args.url + "/api/auth/login", body={"email": args.email, "password": os.environ["ZAP_PASSWORD"]})
    if not login or "accessToken" not in login:
        sys.exit("No se pudo iniciar sesión con " + args.email)
    token = login["accessToken"]
    if args.lab_fixture:
        lab_fixture(args.url, token, args.lab_fixture)
    found = resolve(args.url, token)
    bodies, query = samples(args.url, token, found)

    spec = json.load(open(args.spec, encoding="utf-8"))
    spec["paths"] = build_paths(spec, found, bodies, query, args.auth)
    json.dump(spec, open(args.out, "w", encoding="utf-8"))
    print(f"{args.email}: {len(spec['paths'])} rutas; identificadores: {', '.join(sorted(found)) or 'ninguno'}", file=sys.stderr)
    print(token)


if __name__ == "__main__":
    main()

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

ALWAYS_EXCLUDED = ("/api/auth/logout", "/api/auth/refresh", "/api/users/{id}/role")
# Plantilla de ruta -> tipo de identificador que necesita.
KINDS = {
    "/api/appointments/{id}": "appointment",
    "/api/appointments/{id}/cancel": "appointment",
    "/api/consultations/{id}": "consultation",
    "/api/doctors/{id}": "doctor",
    "/api/doctors/{doctorId}/availability": "doctor",
    "/api/doctors/{doctorId}/schedule": "doctor",
    "/api/labs/results/{id}": "lab",
    "/api/labs/results/{id}/notification/retry": "lab",
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
    "doctor": ["/api/doctors/me", "/api/doctors"],
    "appointment": ["/api/appointments/me", "/api/appointments/consultable", "/api/appointments"],
    "consultation": ["/api/patients/{patient}/consultations"],
    "prescription": ["/api/prescriptions/me", "/api/patients/{patient}/prescriptions"],
    "lab": ["/api/labs/results/me", "/api/labs/results", "/api/patients/{patient}/lab-results"],
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
        for key in ("items", "data", "results"):
            if isinstance(value.get(key), list):
                return ids(value[key])
        return []
    if isinstance(value, list):
        return [str(item["id"]) for item in value if isinstance(item, dict) and "id" in item]
    return []


def resolve(base, token):
    found = {}
    patients = []
    for source in SOURCES["patient"]:
        patients = ids(call(base + source, token))
        if patients:
            found["patient"] = patients[0]
            break
    today = datetime.date.today()
    agenda = [f"/api/appointments?doctorId={doctor}&date={today + datetime.timedelta(days=offset)}"
              for doctor in ids(call(base + "/api/doctors", token))[:5] for offset in range(-3, 15)]
    for kind, sources in SOURCES.items():
        if kind == "patient":
            continue
        for source in sources + (agenda if kind == "appointment" else []):
            for patient in patients[:25] if "{patient}" in source else [None]:
                values = ids(call(base + (source.replace("{patient}", patient) if patient else source), token))
                if values:
                    found[kind] = values[0]
                    break
            if kind in found:
                break
    for patient in patients[:25]:
        values = ids(call(f"{base}/api/appointments/consultable?patientId={patient}", token))
        if values:
            found["consultable"], found["consultable_patient"] = values[0], patient
            break
    return found


def free_slot(base, token):
    """Primer horario libre de los próximos días: un cuerpo de reserva válido necesita uno."""
    today = datetime.date.today()
    for doctor in ids(call(base + "/api/doctors/me", token)) + ids(call(base + "/api/doctors", token))[:5]:
        for offset in range(1, 15):
            day = str(today + datetime.timedelta(days=offset))
            slots = (call(f"{base}/api/doctors/{doctor}/availability?date={day}", token) or {}).get("slots", [])
            free = [slot["time"] for slot in slots if slot.get("available")]
            if free:
                return doctor, day, free[-1]
    return None, None, None


def samples(base, token, found):
    """Cuerpos válidos para que ZAP pase la validación y analice la lógica de cada operación clínica; devuelve {(ruta, método): cuerpo} y los parámetros."""
    doctor, day, time = free_slot(base, token)
    today = str(datetime.date.today())
    is_patient = bool(ids(call(base + "/api/patients/me", token)))
    booking = {"doctorId": doctor, "date": day, "time": time, "reason": "Control ficticio"}
    if not is_patient:
        booking["patientId"] = found.get("patient")
    bodies = {
        ("/api/appointments", "post"): booking,
        ("/api/labs/results", "post"): {"patientId": found.get("patient"), "examType": "Glucosa", "performedOn": today, "notes": "Dato ficticio",
                                        "parameters": [{"name": "Glucosa", "value": "90", "unit": "mg/dL", "referenceRange": "70-100"}]},
        ("/api/consultations", "post"): {"appointmentId": found.get("consultable"), "reason": "Control ficticio", "notes": "Dato ficticio", "diagnosis": "Sin hallazgos"},
        ("/api/prescriptions", "post"): {"consultationId": found.get("consultation"), "items": [
            {"medication": "Acetaminofén", "dose": "500 mg", "frequency": "Cada 8 horas", "duration": "3 días", "instructions": "Dato ficticio"}]},
    }
    schedule = call(f"{base}/api/doctors/{found.get('doctor')}/schedule", token)
    if schedule:
        bodies[("/api/doctors/{doctorId}/schedule", "put")] = {key: schedule[key] for key in ("days", "startTime", "endTime")}
    query = {"date": day, "patientId": found.get("consultable_patient") or found.get("patient"), "doctorId": found.get("doctor")}
    return bodies, query


def with_samples(path, value, bodies, query):
    """Pone ejemplos en el contrato; ZAP los usa en lugar de inventar valores que la API rechazaría."""
    for method, operation in value.items():
        if not isinstance(operation, dict):
            continue
        # La cabecera es opcional y ZAP genera un valor que no cumple su largo: sin ella, la reserva llega a la lógica de negocio.
        if "parameters" in operation:
            operation["parameters"] = [p for p in operation["parameters"] if p.get("name") != "Idempotency-Key"]
        for parameter in operation.get("parameters", []):
            if parameter.get("in") == "query" and query.get(parameter["name"]):
                parameter["example"] = query[parameter["name"]]
                parameter.setdefault("schema", {})["example"] = query[parameter["name"]]
        body = bodies.get((path, method))
        media = operation.get("requestBody", {}).get("content", {}).get("application/json")
        if body and media:
            media["example"] = body
            media["schema"] = {"type": "object", "example": body}


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
    if args.lab_fixture:  # Da un resultado (y su notificación) a la paciente, para que Médico y Paciente tengan datos que analizar.
        patient = ids(call(args.url + "/api/labs/patients?q=" + args.lab_fixture, token))
        result = call(args.url + "/api/labs/results", token, {
            "patientId": patient[0] if patient else None, "examType": "Hemograma", "performedOn": str(datetime.date.today()),
            "notes": "Dato ficticio para el análisis de ZAP", "parameters": [{"name": "Hemoglobina", "value": "14"}]})
        if not result:
            print("No se pudo crear el resultado de laboratorio ficticio", file=sys.stderr)
    found = resolve(args.url, token)
    bodies, query = samples(args.url, token, found)

    spec = json.load(open(args.spec, encoding="utf-8"))
    paths = {}
    for path, value in spec["paths"].items():
        if path in ALWAYS_EXCLUDED or (path.startswith("/api/auth/") and not args.auth):
            continue
        with_samples(path, value, bodies, query)
        kind = KINDS.get(path)
        if kind and kind in found:
            concrete = path.replace("{id}", found[kind]).replace("{doctorId}", found[kind]).replace("{patientId}", found[kind])
            for operation in value.values():
                if isinstance(operation, dict) and "parameters" in operation:
                    operation["parameters"] = [p for p in operation["parameters"] if p.get("in") != "path"]
            paths[concrete] = value
        elif kind is None:
            paths[path] = value
    spec["paths"] = paths
    json.dump(spec, open(args.out, "w", encoding="utf-8"))
    print(f"{args.email}: {len(paths)} rutas; identificadores: {', '.join(sorted(found)) or 'ninguno'}", file=sys.stderr)
    print(token)


if __name__ == "__main__":
    main()

"""Genera medicore.insomnia.json (exportación de Insomnia, importable desde la app y ejecutable con inso).

Uso: python tests/insomnia/generar_coleccion.py
"""
import json
import pathlib

WORKSPACE = "wrk_medicore"
ENV = "env_medicore"
TIME = 1_762_400_000_000
ROLES = {
    "recepcion": "elena.vargas@example.com",
    "medico": "lucia.mendez@example.com",
    "laboratorio": "daniela.reyes@example.com",
    "admin": "admin@example.com",
    "paciente": "ana.garcia@example.com",
}
EVIL_TOKEN = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwicm9sZSI6IkFkbWluaXN0cmFkb3IifQ.firma-invalida"
NO_ID = "00000000-0000-0000-0000-000000000001"

resources = [
    {"_id": WORKSPACE, "_type": "workspace", "parentId": None, "name": "MediCore API", "scope": "collection",
     "description": "Positivas, negativas, autenticación, autorización y errores.", "modified": TIME, "created": TIME},
    {"_id": "env_base", "_type": "environment", "parentId": WORKSPACE, "name": "Base", "data": {}, "modified": TIME, "created": TIME},
    {"_id": ENV, "_type": "environment", "parentId": "env_base", "name": "QA local", "isPrivate": False, "modified": TIME, "created": TIME,
     "data": {"base_url": "http://127.0.0.1:8080", "password": "defina-SEED_DEV_PASSWORD"}},
]
order = 0


def folder(name):
    folder_id = "fld_" + "".join(c for c in name.lower() if c.isalnum())
    resources.append({"_id": folder_id, "_type": "request_group", "parentId": WORKSPACE, "name": name,
                      "metaSortKey": len(resources), "modified": TIME, "created": TIME})
    return folder_id


def req(parent, name, method, path, status, token=None, body=None, after="", before="", headers=None):
    """Agrega una petición que comprueba el código HTTP esperado."""
    global order
    order += 1
    head = list(headers or [])
    if token:
        value = EVIL_TOKEN if token == "falso" else "{{ _.token_%s }}" % token
        head.append({"name": "Authorization", "value": "Bearer " + value})
    item = {
        "_id": f"req_{order:03d}", "_type": "request", "parentId": parent, "name": f"{order:02d} {name}", "method": method,
        "url": "{{ _.base_url }}" + path, "headers": head, "authentication": {}, "parameters": [],
        "metaSortKey": order, "modified": TIME, "created": TIME, "isPrivate": False, "settingFollowRedirects": "global",
        "preRequestScript": before,
        "afterResponseScript": "insomnia.test('%s responde %d', () => { insomnia.expect(insomnia.response.code).to.eql(%d); });\n%s" % (name.replace("'", ""), status, status, after),
    }
    if body is not None:
        item["body"] = {"mimeType": "application/json", "text": json.dumps(body, ensure_ascii=False, indent=2)}
        head.append({"name": "Content-Type", "value": "application/json"})
    resources.append(item)


def save(key, expression):
    return "insomnia.environment.set('%s', %s);\n" % (key, expression)


NEXT_WEEKDAY = """const d = new Date(Date.now() + (7 + Math.floor(Math.random() * 75)) * 86400000);
while ([0, 6].includes(d.getUTCDay())) d.setUTCDate(d.getUTCDate() + 1);
insomnia.environment.set('booking_date', d.toISOString().slice(0, 10));
"""
UNIQUE = "insomnia.environment.set('unique', String(Date.now()));\n"

auth = folder("1 Autenticación")
req(auth, "Salud de la API", "GET", "/api/health", 200)
req(auth, "Sin token", "GET", "/api/auth/me", 401)
req(auth, "Token manipulado", "GET", "/api/auth/me", 401, token="falso")
req(auth, "Credenciales inválidas", "POST", "/api/auth/login", 401, body={"email": "nadie@example.com", "password": "incorrecta"})
req(auth, "Datos de acceso incompletos", "POST", "/api/auth/login", 400, body={"email": "no-es-correo", "password": ""})
for role, email in ROLES.items():
    req(auth, f"Inicio de sesión {role}", "POST", "/api/auth/login", 200, body={"email": email, "password": "{{ _.password }}"},
        after=save(f"token_{role}", "insomnia.response.json().accessToken"))
req(auth, "Usuario autenticado", "GET", "/api/auth/me", 200, token="recepcion",
    after="insomnia.test('No expone la contraseña', () => { insomnia.expect(insomnia.response.text()).to.not.include('asswordHash'); });\n")

doctors = folder("2 Médicos y disponibilidad")
req(doctors, "Catálogo de médicos", "GET", "/api/doctors", 200, token="recepcion",
    after=save("doctor_id", "insomnia.response.json().find(d => d.fullName.includes('Méndez')).id"))
req(doctors, "Médico sin token", "GET", "/api/doctors", 401)
req(doctors, "Paciente no ve contacto del médico", "GET", "/api/doctors", 200, token="paciente",
    after="insomnia.test('Sin teléfono ni correo', () => { insomnia.expect(insomnia.response.json().every(d => !d.phone && !d.email)).to.eql(true); });\n")
req(doctors, "Recepción no crea médicos", "POST", "/api/doctors", 403, token="recepcion",
    body={"fullName": "Dr. No Autorizado", "specialty": "General", "phone": "5555-1234", "email": "no@example.com"})
req(doctors, "Disponibilidad", "GET", "/api/doctors/{{ _.doctor_id }}/availability?date={{ _.booking_date }}", 200, token="recepcion", before=NEXT_WEEKDAY,
    after=save("slot", "insomnia.response.json().slots.find(s => s.available).time"))
req(doctors, "Disponibilidad con fecha inválida", "GET", "/api/doctors/{{ _.doctor_id }}/availability?date=no-es-fecha", 400, token="recepcion")
req(doctors, "Disponibilidad de médico inexistente", "GET", f"/api/doctors/{NO_ID}/availability?date=" + "{{ _.booking_date }}", 404, token="recepcion")

patients = folder("3 Pacientes")
patient_body = {"fullName": "Paciente Insomnia", "identificationNumber": "INS-{{ _.unique }}", "birthDate": "1990-05-20", "phone": "5555-1234", "email": "insomnia@example.com"}
req(patients, "Alta de paciente", "POST", "/api/patients", 201, token="recepcion", body=patient_body, before=UNIQUE,
    after=save("patient_id", "insomnia.response.json().id"))
req(patients, "Identificación duplicada", "POST", "/api/patients", 409, token="recepcion", body=patient_body)
req(patients, "Datos inválidos", "POST", "/api/patients", 400, token="recepcion", body={"fullName": "", "identificationNumber": "", "birthDate": "2999-01-01", "phone": "x", "email": "x"})
req(patients, "Lectura por recepción", "GET", "/api/patients/{{ _.patient_id }}", 200, token="recepcion")
req(patients, "Lectura sin token", "GET", "/api/patients/{{ _.patient_id }}", 401)
req(patients, "Laboratorio sin acceso a pacientes", "GET", "/api/patients/{{ _.patient_id }}", 403, token="laboratorio")
req(patients, "Médico sin relación de atención", "GET", "/api/patients/{{ _.patient_id }}", 404, token="medico")
req(patients, "Otro paciente no lo puede leer", "GET", "/api/patients/{{ _.patient_id }}", 404, token="paciente")
req(patients, "Paciente inexistente", "GET", f"/api/patients/{NO_ID}", 404, token="recepcion")

appointments = folder("4 Citas")
booking = {"patientId": "{{ _.patient_id }}", "doctorId": "{{ _.doctor_id }}", "date": "{{ _.booking_date }}", "time": "{{ _.slot }}", "reason": "Control"}
idempotency = [{"name": "Idempotency-Key", "value": "ins-{{ _.unique }}"}]
req(appointments, "Reserva", "POST", "/api/appointments", 201, token="recepcion", body=booking, headers=idempotency,
    after=save("appointment_id", "insomnia.response.json().id"))
req(appointments, "Reintento con la misma clave", "POST", "/api/appointments", 200, token="recepcion", body=booking, headers=idempotency,
    after="insomnia.test('Devuelve la misma cita', () => { insomnia.expect(insomnia.response.json().id).to.eql(insomnia.environment.get('appointment_id')); });\n")
req(appointments, "Otro paciente en el mismo horario", "POST", "/api/appointments", 409, token="recepcion",
    body={"patientId": "{{ _.other_patient_id }}", "doctorId": "{{ _.doctor_id }}", "date": "{{ _.booking_date }}", "time": "{{ _.slot }}"},
    before="", after="")
resources.insert(len(resources) - 1, None)  # se reemplaza abajo por el alta del segundo paciente
order += 1
second = {
    "_id": f"req_{order:03d}", "_type": "request", "parentId": appointments, "name": f"{order:02d} Segundo paciente", "method": "POST",
    "url": "{{ _.base_url }}/api/patients", "authentication": {}, "parameters": [], "modified": TIME, "created": TIME, "isPrivate": False,
    "metaSortKey": order - 1.5, "settingFollowRedirects": "global", "preRequestScript": "",
    "headers": [{"name": "Authorization", "value": "Bearer {{ _.token_recepcion }}"}, {"name": "Content-Type", "value": "application/json"}],
    "body": {"mimeType": "application/json", "text": json.dumps({**patient_body, "identificationNumber": "INS2-{{ _.unique }}"})},
    "afterResponseScript": "insomnia.test('Segundo paciente responde 201', () => { insomnia.expect(insomnia.response.code).to.eql(201); });\n" + save("other_patient_id", "insomnia.response.json().id"),
}
resources[resources.index(None)] = second
req(appointments, "Horario fuera de la atención", "POST", "/api/appointments", 400, token="recepcion",
    body={**booking, "time": "22:00"})
req(appointments, "Fecha pasada", "POST", "/api/appointments", 400, token="recepcion", body={**booking, "date": "2020-01-06"})
req(appointments, "Clave de idempotencia inválida", "POST", "/api/appointments", 400, token="recepcion", body=booking, headers=[{"name": "Idempotency-Key", "value": "x"}])
req(appointments, "Paciente reserva para otro", "POST", "/api/appointments", 404, token="paciente", body=booking)
req(appointments, "Laboratorio no reserva", "POST", "/api/appointments", 403, token="laboratorio", body=booking)
req(appointments, "Detalle de la cita", "GET", "/api/appointments/{{ _.appointment_id }}", 200, token="recepcion")
req(appointments, "Paciente ajeno no ve la cita", "GET", "/api/appointments/{{ _.appointment_id }}", 404, token="paciente")
req(appointments, "Cancelación", "POST", "/api/appointments/{{ _.appointment_id }}/cancel", 204, token="recepcion")
req(appointments, "Cancelación repetida", "POST", "/api/appointments/{{ _.appointment_id }}/cancel", 204, token="recepcion")

labs = folder("5 Laboratorio")
lab_body = {"patientId": "{{ _.patient_id }}", "examType": "Química sanguínea", "performedOn": "2026-01-15", "notes": "Prueba de API",
            "parameters": [{"name": "Glucosa", "value": "92", "unit": "mg/dL", "referenceRange": "70-100"}]}
req(labs, "Registro de resultado", "POST", "/api/labs/results", 201, token="laboratorio", body=lab_body, after=save("lab_id", "insomnia.response.json().id"))
req(labs, "Sin parámetros", "POST", "/api/labs/results", 400, token="laboratorio", body={**lab_body, "parameters": []})
req(labs, "Fecha futura", "POST", "/api/labs/results", 400, token="laboratorio", body={**lab_body, "performedOn": "2999-01-01"})
req(labs, "Paciente inexistente", "POST", "/api/labs/results", 400, token="laboratorio", body={**lab_body, "patientId": NO_ID})
req(labs, "Recepción no registra", "POST", "/api/labs/results", 403, token="recepcion", body=lab_body)
req(labs, "Médico no registra", "POST", "/api/labs/results", 403, token="medico", body=lab_body)
req(labs, "Detalle por laboratorio", "GET", "/api/labs/results/{{ _.lab_id }}", 200, token="laboratorio")
req(labs, "Administrador sin acceso clínico", "GET", "/api/labs/results/{{ _.lab_id }}", 403, token="admin")
req(labs, "Paciente ajeno", "GET", "/api/labs/results/{{ _.lab_id }}", 404, token="paciente")
req(labs, "Los resultados no se editan", "PUT", "/api/labs/results/{{ _.lab_id }}", 405, token="laboratorio", body=lab_body)
req(labs, "Los resultados no se borran", "DELETE", "/api/labs/results/{{ _.lab_id }}", 405, token="laboratorio")

prescriptions = folder("6 Recetas")
req(prescriptions, "Mis recetas", "GET", "/api/prescriptions/me", 200, token="paciente")
req(prescriptions, "Receta inexistente", "GET", f"/api/prescriptions/{NO_ID}", 404, token="medico")
req(prescriptions, "Recepción sin acceso clínico", "GET", f"/api/prescriptions/{NO_ID}", 403, token="recepcion")
req(prescriptions, "Sin token", "GET", f"/api/prescriptions/{NO_ID}", 401)

notifications = folder("7 Notificaciones y auditoría")
req(notifications, "Mis notificaciones", "GET", "/api/notifications", 200, token="paciente")
req(notifications, "Sin leer", "GET", "/api/notifications/unread-count", 200, token="paciente")
req(notifications, "Recepción sin notificaciones", "GET", "/api/notifications", 403, token="recepcion")
req(notifications, "Auditoría para administrador", "GET", "/api/audit?take=20", 200, token="admin",
    after="insomnia.test('Sin contenido clínico', () => { insomnia.expect(insomnia.response.text()).to.not.include('Glucosa'); });\n")
req(notifications, "Auditoría prohibida a recepción", "GET", "/api/audit", 403, token="recepcion")
req(notifications, "Usuarios prohibidos a médico", "GET", "/api/users", 403, token="medico")

data = {"_type": "export", "__export_format": 4, "__export_source": "medicore.generador", "resources": resources}
out = pathlib.Path(__file__).with_name("medicore.insomnia.json")
out.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"{order} peticiones en {out.name}")

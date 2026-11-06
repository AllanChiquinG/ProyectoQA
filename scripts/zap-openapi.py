"""Copia el contrato sin las rutas que cerrarían la sesión del escaneo o cambiarían permisos."""
import json
import sys

EXCLUDED = ("/api/auth/logout", "/api/auth/refresh", "/api/users/{id}/role")
spec = json.load(open("documentacion/openapi-v1.json", encoding="utf-8"))
spec["paths"] = {path: value for path, value in spec["paths"].items() if path not in EXCLUDED}
json.dump(spec, open(sys.argv[1], "w", encoding="utf-8"))
print(len(spec["paths"]), "rutas para el escaneo")

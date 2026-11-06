"""Mide qué rutas de la API alcanzó ZAP con cada rol, leyendo el registro del proxy. Una ruta con 2xx significa que el análisis entró a su lógica.

Uso: docker logs --since <hora> medicore-qa-proxy-1 2>&1 | zap-cobertura.py <rol> reports/zap/cobertura-api.json
"""
import json
import pathlib
import re
import sys

ID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
role, out = sys.argv[1], pathlib.Path(sys.argv[2])
routes = {}
for line in sys.stdin:
    try:
        entry = json.loads(line)
        uri, method, status = entry["request"]["uri"], entry["request"]["method"], int(entry["status"])
    except (ValueError, KeyError, TypeError):
        continue
    path = uri.split("?")[0]
    if not path.startswith("/api/") or path.startswith("/api/health"):
        continue
    key = method + " " + ID.sub("{id}", path)
    bucket = f"{status // 100}xx"
    counts = routes.setdefault(key, {})
    counts[bucket] = counts.get(bucket, 0) + 1

report = json.loads(out.read_text(encoding="utf-8")) if out.exists() else {}
report[role] = dict(sorted(routes.items()))
out.write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")
reached = sum(1 for counts in routes.values() if "2xx" in counts)
print(f"Cobertura {role}: {reached} de {len(routes)} rutas con respuesta 2xx")

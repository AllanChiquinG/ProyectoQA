"""Criterios de salida del pipeline. Sale con código 1 si algo impide el despliegue final.

Bloquean: Quality Gate de SonarQube rechazado, vulnerabilidad crítica no aceptada en npm audit, Dependency-Check o ZAP,
reporte ausente, colección de API fallida o umbral de k6 incumplido.
Las excepciones aceptadas viven en ci/riesgos-aceptados.json, cada una con motivo.
"""
import json
import pathlib
import sys

REPORTS = pathlib.Path("reports")
accepted = json.loads(pathlib.Path("ci/riesgos-aceptados.json").read_text(encoding="utf-8"))
blockers = []
notes = []


def load(path):
    file = REPORTS / path
    if not file.exists():
        blockers.append(f"Falta el reporte {path}")
        return None
    return json.loads(file.read_text(encoding="utf-8"))


sonar = load("sonarqube/quality-gate.json")
if sonar:
    status = sonar.get("projectStatus", {}).get("status")
    notes.append(f"SonarQube: {status}")
    if status != "OK":
        blockers.append(f"Quality Gate de SonarQube: {status}")

npm = load("npm-audit/audit.json")
if npm:
    counts = npm["metadata"]["vulnerabilities"]
    notes.append(f"npm audit: {counts}")
    if counts.get("critical", 0) > 0:
        blockers.append(f"npm audit: {counts['critical']} críticas")

dc = load("dependency-check/dependency-check-report.json")
if dc:
    found = {v["name"]: f"{d['fileName']}" for d in dc.get("dependencies", []) for v in d.get("vulnerabilities", [])
             if str(v.get("severity", "")).upper() == "CRITICAL"}
    open_items = sorted(set(found) - set(accepted.get("dependency-check", {})))
    notes.append(f"Dependency-Check: {len(found)} críticas, {len(open_items)} sin aceptar")
    if open_items:
        blockers.append("Dependency-Check críticas sin aceptar: " + ", ".join(open_items[:10]))

zap_reports = (("zap/zap.json", "ZAP web"), ("zap/zap-api.json", "ZAP API administrador"), ("zap/zap-api-recepcion.json", "ZAP API recepción"),
               ("zap/zap-api-medico.json", "ZAP API médico"), ("zap/zap-api-laboratorio.json", "ZAP API laboratorio"), ("zap/zap-api-paciente.json", "ZAP API paciente"))
for report, label in zap_reports:
    zap = load(report)
    if zap:
        high = sorted({a["alert"] for s in zap.get("site", []) for a in s.get("alerts", []) if str(a.get("riskcode")) == "3"})
        open_items = sorted(set(high) - set(accepted.get("zap", {})))
        notes.append(f"{label}: {len(high)} alertas altas, {len(open_items)} sin aceptar")
        if open_items:
            blockers.append(f"{label} altas sin aceptar: " + ", ".join(open_items))

api = REPORTS / "insomnia/resultado.txt"
if not api.exists():
    blockers.append("Falta el resultado de Insomnia")
else:
    lines = [line for line in api.read_text(encoding="utf-8", errors="replace").splitlines() if line.startswith("Tests:")]
    notes.append(lines[-1] if lines else "Insomnia sin resumen")
    if not lines or "failed" in lines[-1]:
        blockers.append("La colección de Insomnia falló")

k6 = load("k6/load.json")
if k6:
    failed = [f"{name} {rule}" for name, metric in k6["metrics"].items() for rule, result in metric.get("thresholds", {}).items() if not result["ok"]]
    notes.append(f"k6 load: {len(failed)} umbrales incumplidos")
    blockers.extend(f"k6: {item}" for item in failed)

print("\n".join(notes))
if blockers:
    print("\nDESPLIEGUE BLOQUEADO:")
    print("\n".join(f"- {item}" for item in blockers))
    sys.exit(1)
print("\nCriterios de salida cumplidos.")

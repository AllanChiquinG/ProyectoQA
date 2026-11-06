"""Resume los reportes de k6 en reports/k6/resumen.json e imprime una tabla Markdown."""
import json
import pathlib

folder = pathlib.Path("reports/k6")
rows = []
for name in ("load", "stress", "spike"):
    metrics = json.loads((folder / f"{name}.json").read_text(encoding="utf-8"))["metrics"]
    value = lambda key, field, default=0: metrics.get(key, {}).get("values", {}).get(field, default)
    rows.append({
        "perfil": name,
        "peticiones": value("http_reqs", "count"),
        "por_segundo": round(value("http_reqs", "rate"), 1),
        "p95_lectura_ms": round(value("http_req_duration{tipo:lectura}", "p(95)"), 1),
        "p95_reserva_ms": round(value("http_req_duration{tipo:reserva}", "p(95)"), 1),
        "p95_global_ms": round(value("http_req_duration", "p(95)"), 1),
        "error_inesperado_pct": round(value("error_inesperado", "rate") * 100, 3),
        "reservas": value("reservas_creadas", "count"),
        "conflictos_409": value("reservas_rechazadas_por_conflicto", "count"),
        "umbrales_incumplidos": [f"{k} {r}" for k, m in metrics.items() for r, d in m.get("thresholds", {}).items() if not d["ok"]],
    })
(folder / "resumen.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("| Perfil | Peticiones | req/s | p95 lectura | p95 reserva | Error inesperado | Reservas | 409 | Umbrales |")
print("|---|---|---|---|---|---|---|---|---|")
for r in rows:
    state = "cumplidos" if not r["umbrales_incumplidos"] else "INCUMPLIDOS: " + "; ".join(r["umbrales_incumplidos"])
    print(f"| {r['perfil']} | {r['peticiones']} | {r['por_segundo']} | {r['p95_lectura_ms']} ms | {r['p95_reserva_ms']} ms | {r['error_inesperado_pct']} % | {r['reservas']} | {r['conflictos_409']} | {state} |")

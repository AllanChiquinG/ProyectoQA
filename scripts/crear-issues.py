"""Crea en GitHub Issues un issue por cada defecto de documentacion/12-registro-de-defectos.md. Los cerrados quedan cerrados, con su corrección.

Es idempotente: si ya existe un issue con el mismo identificador entre corchetes, no lo repite. Requiere "gh auth login".
Uso: python scripts/crear-issues.py [--repo dueño/nombre] [--dry-run]
"""
import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = pathlib.Path(__file__).resolve().parent.parent
REGISTER = ROOT / "documentacion" / "12-registro-de-defectos.md"
GH = shutil.which("gh") or r"C:\Program Files\GitHub CLI\gh.exe"
COLORS = {"defecto": "b60205", "severidad:alta": "d93f0b", "severidad:media": "fbca04", "severidad:baja": "0e8a16"}


def gh(*args, payload=None):
    result = subprocess.run([GH, *args], input=None if payload is None else json.dumps(payload), capture_output=True, text=True, encoding="utf-8")
    if result.returncode != 0:
        raise SystemExit(f"gh {' '.join(args[:3])} falló: {result.stderr.strip()}")
    return result.stdout


def tables():
    """Filas de las secciones de cerrados y abiertos, como diccionarios por encabezado."""
    section, headers, rows = None, None, []
    for line in REGISTER.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            section, headers = {"## Defectos cerrados": "cerrado", "## Defectos abiertos": "abierto"}.get(line.strip()), None
        elif section and line.startswith("|") and not re.match(r"^\|[-| ]+\|$", line):
            cells = [cell.strip() for cell in line.strip().strip("|").split(" | ")]
            if headers is None:
                headers = cells
            else:
                rows.append({"Estado": section, **dict(zip(headers, cells))})
    return rows


def describe(row):
    last = ("Corrección", row["Corrección"]) if row["Estado"] == "cerrado" else ("Decisión", row["Decisión"])
    return "\n".join([
        f"**Módulo:** {row['Módulo']}  ", f"**Severidad:** {row['Severidad']}  ", f"**Prioridad:** {row['Prioridad']}  ", "",
        "## Pasos", row["Pasos"], "", "## Resultado esperado", row["Esperado"], "", "## Resultado obtenido", row["Obtenido"], "",
        f"## {last[0]}", last[1], "", "## Evidencia", row["Evidencia"], "",
        "Registro completo: `documentacion/12-registro-de-defectos.md`.",
    ])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default="Diegohan1/ProyectoQA")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    rows = tables()
    print(len(rows), "defectos en el registro:", sum(r["Estado"] == "cerrado" for r in rows), "cerrados y", sum(r["Estado"] == "abierto" for r in rows), "abiertos")
    if args.dry_run:
        for row in rows:
            print(f"[{row['ID']}] {row['Módulo']}: {row['Obtenido']} ({row['Estado']})")
        return

    gh("repo", "edit", args.repo, "--enable-issues")
    for label in {"defecto", *(f"severidad:{r['Severidad'].lower()}" for r in rows)}:
        gh("label", "create", label, "--repo", args.repo, "--force", "--color", COLORS.get(label, "ededed"))
    existing = json.loads(gh("issue", "list", "--repo", args.repo, "--state", "all", "--limit", "500", "--json", "title"))
    known = {match.group(1) for item in existing if (match := re.match(r"\[([^\]]+)\]", item["title"]))}
    for row in rows:
        if row["ID"] in known:
            print("Ya existe", row["ID"])
            continue
        url = gh("issue", "create", "--repo", args.repo, "--title", f"[{row['ID']}] {row['Módulo']}: {row['Obtenido']}"[:120],
                 "--body", describe(row), "--label", "defecto", "--label", f"severidad:{row['Severidad'].lower()}").strip()
        if row["Estado"] == "cerrado":
            gh("issue", "close", url, "--reason", "completed")
        print(row["Estado"], url)


if __name__ == "__main__":
    main()

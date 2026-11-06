"""Genera la versión PDF de la documentación en documentacion/pdf/. La fuente sigue siendo el Markdown: si cambia, se vuelve a generar.

Requisitos: Google Chrome instalado y  pip install markdown playwright  (no descarga navegadores: usa Chrome).
Uso: python scripts/docs-pdf.py [nombre-parcial]
"""
import datetime
import html
import pathlib
import re
import sys

import markdown
from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOCS = ROOT / "documentacion"
OUT = DOCS / "pdf"
MERMAID = "https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js"

STYLE = """
:root { color-scheme: light; }
* { box-sizing: border-box; }
body { font-family: "Segoe UI", Arial, sans-serif; font-size: 10.5pt; line-height: 1.45; color: #1b2b27; margin: 0; }
h1, h2, h3, h4 { color: #12332e; line-height: 1.2; break-after: avoid; }
h1 { font-size: 22pt; border-bottom: 3px solid #2a6f63; padding-bottom: 8px; margin: 0 0 16px; }
h2 { font-size: 15pt; margin: 22px 0 8px; border-bottom: 1px solid #d5ded7; padding-bottom: 4px; }
h3 { font-size: 12pt; margin: 16px 0 6px; }
p, ul, ol { margin: 6px 0; }
a { color: #1f5d52; text-decoration: none; }
code { font-family: Consolas, monospace; font-size: 9pt; background: #eef2ee; padding: 1px 3px; border-radius: 3px; overflow-wrap: anywhere; }
pre { background: #eef2ee; padding: 8px 10px; border-radius: 6px; white-space: pre-wrap; overflow-wrap: anywhere; break-inside: avoid; }
pre code { background: none; padding: 0; }
table { border-collapse: collapse; width: 100%; margin: 10px 0; font-size: 8.5pt; }
thead { display: table-header-group; }
tr { break-inside: avoid; }
th { background: #12332e; color: #fff; text-align: left; padding: 4px 6px; font-weight: 600; }
td { border: 1px solid #d5ded7; padding: 4px 6px; vertical-align: top; overflow-wrap: anywhere; }
tr:nth-child(even) td { background: #f6f8f3; }
blockquote { border-left: 4px solid #2a6f63; margin: 8px 0; padding: 2px 12px; background: #f6f8f3; }
"""


def links_to_pdf(text):
    """Los enlaces a otros documentos apuntan a su PDF, no al Markdown."""
    return text.replace(".md)", ".pdf)").replace(".md#", ".pdf#")


def render(page, source, target):
    text = source.read_text(encoding="utf-8-sig")
    body = markdown.markdown(links_to_pdf(text), extensions=["tables", "fenced_code", "sane_lists"])
    wide = max((line.count("|") - 1 for line in text.splitlines() if line.lstrip().startswith("|")), default=0) >= 6
    title = re.search(r"^#\s+(.+)$", text, re.M)
    title = html.escape(title.group(1) if title else source.stem)
    diagrams = re.sub(r'<pre><code class="language-mermaid">(.*?)</code></pre>', r'<div class="mermaid">\1</div>', body, flags=re.S)
    page.set_content(f'<!doctype html><html lang="es"><head><meta charset="utf-8"><title>{title}</title><style>{STYLE}</style></head><body>{diagrams}</body></html>')
    if diagrams != body:  # Los diagramas se dibujan antes de imprimir; requiere conexión la primera vez.
        page.add_script_tag(url=MERMAID)
        page.evaluate("mermaid.initialize({startOnLoad:false, theme:'neutral'}); mermaid.run()")
        page.wait_for_selector(".mermaid svg")
    footer = ('<div style="font-family:Arial;font-size:8px;color:#5a6b65;width:100%;padding:0 14mm;display:flex;justify-content:space-between">'
              f'<span>{title}</span><span>Página <span class="pageNumber"></span> de <span class="totalPages"></span></span></div>')
    target.parent.mkdir(parents=True, exist_ok=True)
    page.pdf(path=str(target), format="Letter", landscape=wide, print_background=True, display_header_footer=True,
             header_template="<span></span>", footer_template=footer,
             margin={"top": "16mm", "bottom": "16mm", "left": "14mm", "right": "14mm"})


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    sources = sorted(DOCS.glob("*.md")) + sorted((DOCS / "evidencias").glob("*.md"))
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome")
        page = browser.new_page()
        for source in sources:
            if only in source.name:
                target = OUT / source.relative_to(DOCS).with_suffix(".pdf")
                render(page, source, target)
                print(target.relative_to(ROOT), f"{target.stat().st_size // 1024} KB")
        browser.close()
    print("Generado:", datetime.datetime.now().strftime("%Y-%m-%d %H:%M"))


if __name__ == "__main__":
    main()

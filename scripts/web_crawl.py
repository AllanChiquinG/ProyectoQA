"""Rastreador de la web del personal: inicia sesión por rol con el formulario real, visita las pantallas conocidas y sigue los enlaces internos de cada una.
Informa por pantalla: estado HTTP, título, textos de "pendiente", enlaces rotos (4xx/5xx), campos de formulario sin etiqueta accesible, imágenes sin alt y
botones/enlaces vacíos. Solo lectura (GET); no envía formularios.

Uso: MEDICORE_PASSWORD=<Seed:DevPassword> python scripts/web_crawl.py [url-base]    (por defecto http://127.0.0.1:5298)
"""
import html
import html.parser
import http.cookiejar
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

sys.stdout.reconfigure(encoding='utf-8')
BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5298').rstrip('/')
PASSWORD = os.environ.get('MEDICORE_PASSWORD') or sys.exit('Define MEDICORE_PASSWORD.')
ROLES = {
    'Recepción': 'elena.vargas@example.com',
    'Médico': 'lucia.mendez@example.com',
    'Laboratorio': 'daniela.reyes@example.com',
    'Administrador': 'admin@example.com',
}
SEEDS = ['/', '/Agenda', '/Agenda/Reservar', '/Pacientes', '/Pacientes/Formulario', '/Medicos', '/Medicos/Formulario', '/Usuarios', '/MiAgenda', '/Laboratorio', '/Laboratorio/Registrar']
PENDING = re.compile(r'pendiente de|próximas entregas|todavía no|TODO|lorem|undefined|NaN|Exception|Stack trace', re.I)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def opener():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect()), jar


def fetch(op, path, data=None, follow=True):
    url = path if path.startswith('http') else BASE + path
    for _ in range(6):
        try:
            req = urllib.request.Request(url, data=data)
            with op.open(req, timeout=30) as r:
                return r.status, r.read().decode('utf-8', 'replace'), url
        except urllib.error.HTTPError as e:
            if e.code in (301, 302, 303, 307) and follow:
                url = urllib.parse.urljoin(url, e.headers['Location'])
                data = None
                continue
            return e.code, e.read().decode('utf-8', 'replace'), url
    return 599, '', url


def login(email):
    op, _ = opener()
    status, page, _ = fetch(op, '/Login')
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page)
    form = {'Input.Email': email, 'Input.Password': PASSWORD}
    names = re.findall(r'name="(Input\.[A-Za-z]+)"', page)
    form = {n: (email if 'mail' in n.lower() else PASSWORD) for n in names}
    if token:
        form['__RequestVerificationToken'] = token.group(1)
    status, body, url = fetch(op, '/Login', urllib.parse.urlencode(form).encode())
    return op, status, url


class Auditor(html.parser.HTMLParser):
    """Recorre el HTML y anota campos sin etiqueta, imágenes sin alt, botones/enlaces vacíos y atributos de fecha inválidos."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.issues = []
        self.label_depth = 0
        self.label_for = set()
        self.fields = []
        self.stack = []  # (tag, attrs, text)

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == 'label':
            self.label_depth += 1
            if a.get('for'):
                self.label_for.add(a['for'])
        if tag in ('input', 'select', 'textarea'):
            kind = a.get('type', 'text')
            if kind not in ('hidden', 'submit', 'button', 'image'):
                self.fields.append((tag, a, self.label_depth > 0))
            for attr in ('min', 'max'):
                if kind == 'date' and a.get(attr) and not re.fullmatch(r'\d{4}-\d{2}-\d{2}', a[attr]):
                    self.issues.append(f'atributo {attr} de fecha inválido: {a[attr]!r}')
        if tag == 'img' and 'alt' not in a:
            self.issues.append('imagen sin alt')
        if tag in ('a', 'button'):
            self.stack.append([tag, a, ''])

    def handle_endtag(self, tag):
        if tag == 'label':
            self.label_depth = max(0, self.label_depth - 1)
        if tag in ('a', 'button') and self.stack:
            t, a, text = self.stack.pop()
            if not text.strip() and not a.get('aria-label') and not a.get('title'):
                self.issues.append(f'<{t}> sin texto accesible: {a}')

    def handle_data(self, data):
        for item in self.stack:
            item[2] += data

    def finish(self):
        for tag, a, wrapped in self.fields:
            if wrapped or a.get('aria-label') or a.get('aria-labelledby') or (a.get('id') and a['id'] in self.label_for):
                continue
            self.issues.append(f'campo sin etiqueta: <{tag} name={a.get("name")!r} id={a.get("id")!r}>')
        return self.issues


def audit(page):
    auditor = Auditor()
    auditor.feed(page)
    issues = auditor.finish()
    m = re.search(r'próximas entregas|pendiente de implement|TODO|Lorem ipsum|undefined|NaN|Stack trace|System\.\w+Exception', page)
    if m:
        issues.append('texto sospechoso: ' + m.group(0))
    return issues


def main():
    total_problems = 0
    for role, email in ROLES.items():
        op, status, url = login(email)
        print(f'\n===== {role} ({email}) · login -> {status} {url}')
        seen, queue = set(), list(SEEDS)
        while queue and len(seen) < 60:
            path = queue.pop(0)
            key = path.split('?')[0]
            if key in seen:
                continue
            seen.add(key)
            st, page, final = fetch(op, path)
            title = re.search(r'<title>(.*?)</title>', page, re.S)
            title = html.unescape(title.group(1).strip()) if title else '(sin título)'
            problems = audit(page) if st == 200 else []
            mark = 'OK ' if st == 200 and not problems else ('403' if st == 403 or 'AccesoDenegado' in final else str(st))
            print(f'  [{mark}] {path:38} {title[:50]}' + (f'  -> {final.replace(BASE, "")}' if final.replace(BASE, '') != path else ''))
            for p in problems:
                print('        !', p)
                total_problems += 1
            if st == 200:
                for href in re.findall(r'href="(/[^"#]*)"', page):
                    href = html.unescape(href)
                    if href.startswith(('/css', '/js', '/lib', '/_', '/favicon')) or '.' in href.split('/')[-1] or href in seen or '/Logout' in href:
                        continue
                    queue.append(href)
    print(f'\nHallazgos del rastreo: {total_problems}')


main()

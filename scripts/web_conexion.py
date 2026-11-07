"""Comprueba cómo responde la web del personal cuando la API NO está disponible (error de conexión). Inicia sesión por rol con la API activa, detiene el
contenedor de la API de desarrollo (`medicore-dev-api`), pide las pantallas principales y la vuelve a levantar. Informa estado HTTP, redirección y si el
cuerpo muestra un mensaje comprensible (sin trazas ni texto técnico). No envía formularios ni modifica datos.

Uso: MEDICORE_PASSWORD=<Seed:DevPassword> python scripts/web_conexion.py [url-base]
"""
import importlib.util
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding='utf-8')
spec = importlib.util.spec_from_file_location('crawl', os.path.join(os.path.dirname(__file__), 'web_crawl.py'))
src = open(spec.origin, encoding='utf-8').read().replace('\nmain()\n', '\n')  # reutiliza login/fetch sin ejecutar el rastreo
crawl = type(sys)('crawl')
sys.argv = sys.argv[:2]
exec(compile(src, spec.origin, 'exec'), crawl.__dict__)

PAGES = {
    'Recepción': ['/Agenda', '/Agenda/Reservar', '/Pacientes'],
    'Administrador': ['/Medicos', '/Usuarios'],
    'Médico': ['/MiAgenda', '/Pacientes'],
    'Laboratorio': ['/Laboratorio', '/Laboratorio/Registrar'],
}
TECH = re.compile(r'Exception|Stack trace|at System\.|HttpRequestException|Npgsql|Connection refused', re.I)


def docker(*args):
    subprocess.run(['docker', *args], check=True, capture_output=True)


def main():
    sessions = {}
    for role in PAGES:
        op, status, url = crawl.login(crawl.ROLES[role])
        sessions[role] = op
        print(f'login {role}: {status} {url}')
    docker('stop', 'medicore-dev-api')
    try:
        time.sleep(2)
        for role, paths in PAGES.items():
            for path in paths:
                st, body, final = crawl.fetch(sessions[role], path)
                shown = re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', ' ', body))
                msg = re.findall(r'(No se pudo[^.<]*\.|El servicio no está disponible[^.<]*\.|Inténtalo de nuevo\.?|Inicia sesión[^.<]*\.)', shown)
                print(f'  [{role}] {path:22} -> {st} {final.replace(crawl.BASE, "")}  mensajes: {sorted(set(msg))[:2]}'
                      + ('  ¡TEXTO TÉCNICO!' if TECH.search(body) else ''))
    finally:
        docker('start', 'medicore-dev-api')
        print('API levantada de nuevo.')


main()

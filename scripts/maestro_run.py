"""Ejecuta los flujos Maestro de la app del paciente en un emulador o dispositivo Android y registra el resultado REAL.

Para cada flujo: prepara sus datos (maestro_prepare.py), ejecuta "maestro test" y anota aprobado/fallido. Si falta alguna herramienta, lista exactamente
qué falta y termina SIN escribir evidencia: nada se marca como aprobado si no se ejecutó.

Requisitos: Maestro (CLI) en el PATH, adb con un emulador/dispositivo conectado, la app instalada (com.medicore.paciente), la API y RabbitMQ en marcha
(docker compose up -d + API en Development, por ejemplo con scripts/dev-stack-linux.sh up) y MEDICORE_PASSWORD.

Uso:  python scripts/maestro_run.py [--only 03]
"""
import argparse
import datetime
import pathlib
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import maestro_prepare as prep  # noqa: E402

ROOT = prep.ROOT
FLOWS = [  # (archivo, escenario de datos)
    ('01-login-valido.yaml', 'base'),
    ('02-login-invalido.yaml', 'base'),
    ('03-reserva-cita.yaml', 'cita'),
    ('04-consulta-y-cancelacion.yaml', 'cancelar'),
    ('05-resultados.yaml', 'resultados'),
    ('06-recetas.yaml', 'recetas'),
    ('07-notificaciones.yaml', 'notificaciones'),
    ('08-cierre-de-sesion.yaml', 'base'),
]
APP_ID = 'com.medicore.paciente'


def missing_tools():
    missing = []
    if not shutil.which('maestro'):
        missing.append('Maestro CLI (comando "maestro" en el PATH)')
    adb = shutil.which('adb')
    if not adb:
        missing.append('adb (platform-tools del SDK de Android)')
    else:
        devices = subprocess.run([adb, 'devices'], capture_output=True, text=True).stdout.splitlines()[1:]
        ready = [line for line in devices if line.strip().endswith('\tdevice')]
        if not ready:
            missing.append('un emulador o dispositivo Android conectado ("adb devices" no lista ninguno en estado "device")')
        else:
            packages = subprocess.run([adb, 'shell', 'pm', 'list', 'packages', APP_ID], capture_output=True, text=True).stdout
            if APP_ID not in packages:
                missing.append(f'la app instalada en el dispositivo ({APP_ID}); instálala con "adb install -r <apk>"')
    if not prep.PASSWORD:
        missing.append('la variable MEDICORE_PASSWORD (Seed:DevPassword)')
    return missing


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--only', help='Prefijo del flujo a ejecutar (por ejemplo 03)')
    parser.add_argument('--out', default='documentacion/evidencias/entrega-7-maestro.md', help='Archivo de evidencia a escribir')
    parser.add_argument('--title', default='Entrega 7 · resultado de los flujos Maestro')
    args = parser.parse_args()

    missing = missing_tools()
    if missing:
        print('NO se ejecutó ningún flujo. Falta:')
        for item in missing:
            print(f'  - {item}')
        return 2

    results = []
    for flow, scenario in FLOWS:
        if args.only and not flow.startswith(args.only):
            continue
        env = prep.prepare(scenario)
        command = [shutil.which('maestro'), 'test']
        for key, value in env.items():
            command += ['-e', f'{key}={value}']
        command.append(str(ROOT / 'tests' / 'maestro' / flow))
        started = time.time()
        run = subprocess.run(command, capture_output=True, text=True, cwd=ROOT)
        seconds = time.time() - started
        passed = run.returncode == 0
        tail = '\n'.join((run.stdout + run.stderr).strip().splitlines()[-12:]).replace(env['PASSWORD'], '***')
        results.append((flow, scenario, passed, seconds, tail))
        print(f'{"APROBADO" if passed else "FALLIDO "} {flow} ({seconds:.0f} s)')
        if not passed:
            print(tail)

    prep.clean_patient()
    out = ROOT / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    stamp = datetime.datetime.now().astimezone().strftime('%Y-%m-%d %H:%M:%S %z')
    lines = [f'# {args.title}', '', f'Ejecutado el {stamp} con `scripts/maestro_run.py` en un dispositivo Android real o emulado, contra la API y RabbitMQ de desarrollo.', '',
             '| Flujo | Escenario | Resultado | Duración |', '|---|---|---|---|']
    for flow, scenario, passed, seconds, _ in results:
        lines.append(f'| `{flow}` | {scenario} | {"APROBADO" if passed else "FALLIDO"} | {seconds:.0f} s |')
    for flow, _, passed, _, tail in results:
        if not passed:
            lines += ['', f'### Fallo en `{flow}`', '```', tail, '```']
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return 0 if all(r[2] for r in results) else 1


if __name__ == '__main__':
    sys.exit(main())

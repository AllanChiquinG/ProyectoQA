"""Genera el archivo de variables de entorno (fuera del repositorio) para los contenedores de desarrollo.

Lee .env (ignorado por Git) y los user-secrets de la API. No imprime ningún valor.
Uso: python scripts/dev-stack-env.py <raíz del repositorio> <archivo de salida>
"""
import pathlib
import subprocess
import sys
import urllib.parse

root, out = sys.argv[1], sys.argv[2]

env = {}
for line in pathlib.Path(root, '.env').read_text(encoding='utf-8').splitlines():
    if '=' in line and not line.lstrip().startswith('#'):
        key, value = line.split('=', 1)
        env[key.strip()] = value.strip()

secrets = {}
listing = subprocess.run(['dotnet', 'user-secrets', 'list', '--project', str(pathlib.Path(root, 'src', 'MediCore.Api'))],
                         capture_output=True, text=True).stdout
for line in listing.splitlines():
    if ' = ' in line:
        key, value = line.split(' = ', 1)
        secrets[key.strip()] = value.strip()

quote = lambda text: urllib.parse.quote(text, safe='')
rows = {
    'ASPNETCORE_ENVIRONMENT': 'Development',
    'ConnectionStrings__Postgres': (f"Host=postgres;Port=5432;Database={env.get('POSTGRES_DB', 'medicore')};"
                                    f"Username={env.get('POSTGRES_USER', 'medicore')};Password={env['POSTGRES_PASSWORD']}"),
    'ConnectionStrings__Redis': f"redis:6379,password={env['REDIS_PASSWORD']}",
    'ConnectionStrings__RabbitMq': (f"amqp://{quote(env.get('RABBITMQ_DEFAULT_USER', 'medicore'))}:"
                                    f"{quote(env['RABBITMQ_DEFAULT_PASS'])}@rabbitmq:5672/"),
    'Jwt__SigningKey': secrets['Jwt:SigningKey'],
    'Seed__DevPassword': secrets['Seed:DevPassword'],
    'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
    'DOTNET_NOLOGO': '1',
}
pathlib.Path(out).write_text('\n'.join(f'{k}={v}' for k, v in rows.items()) + '\n', encoding='utf-8')

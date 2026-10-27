#!/usr/bin/env bash
# Ejecuta comandos de .NET dentro de un contenedor Linux (SDK oficial) con el código del repositorio.
#
# Para qué: en equipos Windows con Smart App Control activado, los .dll sin firma recién compilados no se pueden cargar
# (migraciones, pruebas, API). En Linux no aplica. No cambia ningún ajuste de seguridad del equipo.
#
# - Copia el código a /work (sin bin/obj/.git/node_modules): no pisa la compilación de Windows.
# - Usa el socket de Docker del anfitrión para que Testcontainers levante PostgreSQL, Redis y RabbitMQ desechables.
#   No toca los contenedores, volúmenes ni redes de docker compose (medicore_*): solo crea el volumen de caché NuGet "medicore-nuget".
# - Si el comando genera migraciones, se copian de vuelta a src/MediCore.Infrastructure/Persistence/Migrations.
#
# Uso (desde la raíz del repositorio):
#   scripts/dotnet-linux.sh test MediCore.slnx
#   scripts/dotnet-linux.sh format                      (dotnet format --verify-no-changes)
#   scripts/dotnet-linux.sh ef-migration <Nombre>
#   scripts/dotnet-linux.sh test tests/MediCore.Api.Tests --filter "FullyQualifiedName~Laboratorio"
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -W 2>/dev/null || pwd)"
image="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"
cmd="${1:?Indica el comando: test | build | format | ef-migration <Nombre>}"
shift

case "$cmd" in
  ef-migration)
    name="${1:?Indica el nombre de la migración}"
    inner="dotnet tool restore >/dev/null; dotnet restore MediCore.slnx >/dev/null; \
dotnet ef migrations add $name --project src/MediCore.Infrastructure --startup-project src/MediCore.Api --output-dir Persistence/Migrations && \
cp -f src/MediCore.Infrastructure/Persistence/Migrations/* /repo/src/MediCore.Infrastructure/Persistence/Migrations/"
    ;;
  test | build)
    inner="dotnet $cmd $*"
    ;;
  format)
    inner="dotnet restore MediCore.slnx >/dev/null; dotnet format MediCore.slnx --verify-no-changes"
    ;;
  *)
    echo "Comando no reconocido: $cmd" >&2
    exit 2
    ;;
esac

MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$root:/repo" \
  -v medicore-nuget:/root/.nuget/packages \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  "$image" bash -c "
    set -e
    mkdir -p /work
    tar -C /repo --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude=node_modules --exclude=./reports -cf - . | tar -C /work -xf -
    cd /work
    rm -f global.json   # el SDK de la imagen puede ser más nuevo que el fijado en Windows
    $inner
  "

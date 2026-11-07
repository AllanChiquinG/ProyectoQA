#!/usr/bin/env bash
# OWASP Dependency-Check sobre lo publicado de la API, la web y el worker. La base de la NVD vive en un volumen.
# NVD_API_KEY es opcional, pero sin ella la primera descarga tarda mucho más.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
image="${DEPENDENCY_CHECK_IMAGE:-owasp/dependency-check:12.1.0}"
mkdir -p reports/dependency-check

MSYS_NO_PATHCONV=1 docker run --rm -v "$root:/repo" -v medicore-nuget:/root/.nuget/packages \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 mcr.microsoft.com/dotnet/sdk:10.0 bash -c "
    set -e
    mkdir -p /work
    tar -C /repo --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude=node_modules --exclude=./reports --exclude=./artifacts --exclude=./.vs -cf - . | tar -C /work -xf -
    cd /work && rm -f global.json
    rm -rf /repo/reports/dependency-check/publicado
    for p in Api Web Worker; do dotnet publish src/MediCore.\$p -c Release -o /repo/reports/dependency-check/publicado/\$p >/dev/null; done
    dotnet list MediCore.slnx package --vulnerable --include-transitive > /repo/reports/dependency-check/nuget-vulnerable.txt || true
  "

extra=()
[ -n "${NVD_API_KEY:-}" ] && extra=(--nvdApiKey "$NVD_API_KEY")
MSYS_NO_PATHCONV=1 docker run --rm --user 0:0 \
  -v "$root/reports/dependency-check:/report" -v "${NVD_VOLUME:-medicore-dependency-check}:/usr/share/dependency-check/data" \
  "$image" --project MediCore --scan /report/publicado --format JSON --format HTML --out /report \
  --enableExperimental --disableOssIndex "${extra[@]}"
code=$?
rm -rf reports/dependency-check/publicado
exit $code

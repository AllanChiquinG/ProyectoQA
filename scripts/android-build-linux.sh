#!/usr/bin/env bash
# Compila el APK de DESARROLLO de la app Android (.NET MAUI) dentro de un contenedor Linux. No instala nada en el equipo.
#
# IMPORTANTE: esto solo COMPILA. No prueba la app: probarla exige un emulador o un dispositivo Android (ver documentacion/05-app-movil.md).
#
# - Imagen local "medicore-android-build:local" (scripts/android/Dockerfile): SDK de .NET 10 + JDK 17 + workload maui-android. Se construye la primera vez.
# - El SDK de Android (plataformas, build-tools) se instala la primera vez en el volumen NUEVO "medicore-android-sdk".
# - El código se copia a /work (no pisa bin/obj de Windows). No toca volúmenes ni redes de docker compose.
# - El APK queda en artifacts/ (ignorado por Git). Dirección de la API: API_BASE_URL (por defecto http://10.0.2.2:5011/, el emulador).
#
# Uso:  scripts/android-build-linux.sh [API_BASE_URL]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -W 2>/dev/null || pwd)"
api="${1:-${API_BASE_URL:-http://10.0.2.2:5011/}}"
image="medicore-android-build:local"

if ! docker image inspect "$image" >/dev/null 2>&1; then
  echo "Construyendo la imagen $image (una sola vez)..."
  docker build -t "$image" -f "$root/scripts/android/Dockerfile" "$root/scripts/android"
fi

mkdir -p "$root/artifacts"
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$root:/repo:ro" \
  -v "$root/artifacts:/out" \
  -v medicore-nuget:/root/.nuget/packages \
  -v medicore-android-sdk:/opt/android-sdk \
  -e USER=root -e USERNAME=root \
  "$image" bash -c "
    set -e
    mkdir -p /work
    tar -C /repo --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude=node_modules --exclude=./reports --exclude=./artifacts -cf - . | tar -C /work -xf -
    cd /work
    rm -f global.json
    dotnet build src/MediCore.Mobile -f net10.0-android -c Debug -t:InstallAndroidDependencies -p:AndroidSdkDirectory=/opt/android-sdk -p:AcceptAndroidSDKLicenses=True -p:JavaSdkDirectory=\$JAVA_HOME
    dotnet build src/MediCore.Mobile -f net10.0-android -c Debug -p:ApiBaseUrl=$api -p:AndroidSdkDirectory=/opt/android-sdk -p:JavaSdkDirectory=\$JAVA_HOME
    find src/MediCore.Mobile/bin -name '*.apk' -exec cp -v {} /out/ \;
  "
echo "APK en $root/artifacts (compilado para ApiBaseUrl=$api). Instálalo con: adb install -r artifacts/<archivo>.apk"

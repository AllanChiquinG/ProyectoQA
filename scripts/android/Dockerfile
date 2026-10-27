# Imagen de COMPILACIÓN de la app Android (no la ejecuta): SDK de .NET 10 + JDK 17 + workload maui-android.
# El SDK de Android (plataformas, build-tools) se instala la primera vez en el volumen medicore-android-sdk (ver scripts/android-build-linux.sh).
# Compilar en Linux produce el APK; probarlo exige un emulador o dispositivo Android (ver documentacion/05-app-movil.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jdk-headless unzip curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install maui-android
ENV JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 \
    ANDROID_HOME=/opt/android-sdk \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

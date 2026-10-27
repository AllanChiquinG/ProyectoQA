# Entrega 7 · comandos y resultados verificados

Equipo Windows con Smart App Control activo: el código se compila y prueba en contenedores Linux (`mcr.microsoft.com/dotnet/sdk:10.0`). Docker Desktop con los servicios de `docker compose`.

## xUnit

```bash
scripts/dotnet-linux.sh test MediCore.slnx
```

| Proyecto | Resultado |
|---|---|
| `MediCore.UnitTests` | 44 superadas |
| `MediCore.Mobile.Tests` (cliente HTTP con respuestas programadas y ViewModels con API simulada para aislarlos) | 50 superadas |
| `MediCore.Api.Tests` (incluye `MobileApiIntegrationTests`: 17 pruebas del cliente móvil contra la API REAL con PostgreSQL y Redis en Testcontainers) | 199 superadas |

## Preparación de datos de Maestro contra la API real

```bash
MEDICORE_PASSWORD=<Seed:DevPassword> python scripts/maestro_prepare.py <base|cita|cancelar|resultados|recetas|notificaciones>
```
Los 6 escenarios se prepararon sin errores (cita futura, cita para cancelar, resultado con su notificación asíncrona real, receta con consulta, notificación sin leer).

## Compilación de la app Android (contenedor Linux)

```bash
scripts/android-build-linux.sh        # imagen medicore-android-build:local + volumen medicore-android-sdk
```
Resultado: **Build succeeded**; `artifacts/com.medicore.paciente-Signed.apk` (SHA-256 `ef9f513cdd4264df855a314a13291624e305369409af6752b95de46f0d999a66`) y `com.medicore.paciente.apk` (`f003bdace2a2ab9802c0fd06e57176053d71ad57429001519e035f0a61b44e9f`). Contenido comprobado: `AndroidManifest.xml`, `res/xml/network_security_config.xml`, bibliotecas `arm64-v8a` y `x86_64`. Esto es **compilación**, no prueba en Android.

## Android (ejecución)

**Ejecutado en el emulador `medicore_pixel`:** los 8 flujos Maestro pasan (8/8, una sola ejecución). Resultado completo en `entrega-7-maestro.md`.

(Registro previo, antes de instalar las herramientas: `python scripts/maestro_run.py` sin herramientas responde, sin escribir evidencia:

```
NO se ejecutó ningún flujo. Falta:
  - Maestro CLI (comando "maestro" en el PATH)
  - adb (platform-tools del SDK de Android)
  - la variable MEDICORE_PASSWORD (Seed:DevPassword)
```)

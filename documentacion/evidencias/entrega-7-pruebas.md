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

## xUnit: qué se ejecutó y cuándo (sin presentar resultados viejos como nuevos)

| Momento | Qué se ejecutó | Resultado |
|---|---|---|
| **Antes** de las correcciones de Android (estado del commit `9f7d464`) | `scripts/dotnet-linux.sh test MediCore.slnx` (suite completa) | 293 superadas: 44 unitarias, 50 de la lógica móvil, 199 de integración |
| **Después** (commit `f269f0c`) | **No se volvió a ejecutar xUnit** | — |

Entre ambos commits cambiaron únicamente: `src/MediCore.Mobile/` (`App.xaml.cs`, `AppShell.xaml`, `MediCore.Mobile.csproj`, `AndroidManifest.xml`), `scripts/maestro_run.py`, `tests/maestro/` (flujos 01 y 02 y el subflujo) y documentación. **No cambió** `MediCore.Mobile.Core`, la API ni ningún proyecto de pruebas xUnit (`git diff --name-only 9f7d464 f269f0c`), y ninguno de esos proyectos compila el proyecto MAUI. Por eso los 293 resultados corresponden al mismo código xUnit, pero **no son una ejecución posterior a las últimas correcciones**. Lo que sí se ejecutó después es la compilación de la app (Build succeeded) y los 8 flujos Maestro en el emulador.

## Ubicación de los artefactos
- APK de desarrollo: `artifacts/com.medicore.paciente-Signed.apk` (≈84 MB, firmado con una clave de depuración; `com.medicore.paciente.apk` es el mismo sin firmar). **No está en Git** (carpeta `artifacts/` ignorada): se genera con `scripts/android-build-linux.sh`.
- Resultado de Maestro: `documentacion/evidencias/entrega-7-maestro.md`. Registros y capturas de cada ejecución de Maestro: `%USERPROFILE%\.maestro\tests\<fecha>\` en el equipo donde se ejecutó (no versionados).

# Entrega 7 · resultado de los flujos Maestro

Ejecutado el 2026-10-27 15:25:13 -0600 con `scripts/maestro_run.py` en el emulador `medicore_pixel` (Pixel 6, Android 35 con Google APIs, x86_64, WHPX), contra la API y RabbitMQ de desarrollo.

| Flujo | Escenario | Resultado | Duración |
|---|---|---|---|
| `01-login-valido.yaml` | base | APROBADO | 33 s |
| `02-login-invalido.yaml` | base | APROBADO | 58 s |
| `03-reserva-cita.yaml` | cita | APROBADO | 62 s |
| `04-consulta-y-cancelacion.yaml` | cancelar | APROBADO | 53 s |
| `05-resultados.yaml` | resultados | APROBADO | 43 s |
| `06-recetas.yaml` | recetas | APROBADO | 43 s |
| `07-notificaciones.yaml` | notificaciones | APROBADO | 44 s |
| `08-cierre-de-sesion.yaml` | base | APROBADO | 55 s |

**Entorno:** Maestro 2.12.0, APK de desarrollo compilado en contenedor Linux (`scripts/android-build-linux.sh`) e instalado con `adb`, API/web en contenedores Linux sobre PostgreSQL, Redis y RabbitMQ de `docker compose` (la app llega por `10.0.2.2:5011`). Datos del paciente aislado `e2e.paciente@example.com` (CYP-E2E-004), preparados antes de cada flujo con `scripts/maestro_prepare.py`.

**Historial honesto de esta sesión (antes de este resultado):** la app no abría hasta corregir 3 defectos solo visibles en Android (ver `documentacion/05-app-movil.md`); en la primera pasada completa fallaron 3 flujos: 01 y 02 por selectores de los propios flujos (texto con ícono; campo de texto ya escrito) y 07 por un corte momentáneo del emulador (`device offline`), no de la app. Se corrigieron los flujos 01 y 02 y se repitió la suite completa: **8 de 8 aprobados en una sola ejecución**.

**Límites:** un emulador (Android 35), no dispositivos físicos; sin pruebas de accesibilidad, rendimiento ni rotación; Maestro valida lo visible, no la apariencia pixel a pixel.

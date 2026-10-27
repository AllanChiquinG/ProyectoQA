# Flujos Maestro de la app del paciente

Ocho flujos (el documento exige entre 5 y 8) con datos aislados del paciente de pruebas `e2e.paciente@example.com` (identificación `CYP-E2E-004`).

| Flujo | Pantallas | Escenario de datos | Negativo |
|---|---|---|---|
| `01-login-valido` | MOB-01, MOB-02 | base | |
| `02-login-invalido` | MOB-01 | base | sí (formulario vacío, correo inválido, credenciales incorrectas) |
| `03-reserva-cita` | MOB-04, 05, 06, 07 | cita | sí (sin horario no continúa) |
| `04-consulta-y-cancelacion` | MOB-02, 03, 07 | cancelar | sí (conservar la cita) |
| `05-resultados` | MOB-08, 09 | resultados | |
| `06-recetas` | MOB-10, 11 | recetas | |
| `07-notificaciones` | MOB-12, 09 | notificaciones | |
| `08-cierre-de-sesion` | MOB-01, 02 | base | |

Cada escenario lo prepara `scripts/maestro_prepare.py` (por la API real y la base local de desarrollo, solo para `CYP-E2E-*`). `scripts/maestro_run.py` prepara, ejecuta cada flujo con `maestro test` y escribe `documentacion/evidencias/entrega-7-maestro.md` con el resultado real. Requisitos y pasos en `documentacion/05-app-movil.md`.

# Entrega 8 · flujos Maestro contra el ambiente QA en contenedores

Ejecutado el 2026-10-27 16:16:11 -0600 con `scripts/maestro_run.py` en un dispositivo Android real o emulado, contra la API y RabbitMQ de desarrollo.

| Flujo | Escenario | Resultado | Duración |
|---|---|---|---|
| `01-login-valido.yaml` | base | APROBADO | 39 s |
| `02-login-invalido.yaml` | base | APROBADO | 61 s |
| `03-reserva-cita.yaml` | cita | APROBADO | 65 s |
| `04-consulta-y-cancelacion.yaml` | cancelar | APROBADO | 59 s |
| `05-resultados.yaml` | resultados | APROBADO | 48 s |
| `06-recetas.yaml` | recetas | APROBADO | 47 s |
| `07-notificaciones.yaml` | notificaciones | APROBADO | 49 s |
| `08-cierre-de-sesion.yaml` | base | APROBADO | 61 s |

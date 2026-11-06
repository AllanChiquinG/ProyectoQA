# Maestro · repetición con el código final

Ejecutado el 2026-11-06 14:15:08 -0600 con `scripts/maestro_run.py` en un dispositivo Android real o emulado, contra la API de http://127.0.0.1:8080 y su RabbitMQ.

| Flujo | Escenario | Resultado | Duración |
|---|---|---|---|
| `01-login-valido.yaml` | base | APROBADO | 29 s |
| `02-login-invalido.yaml` | base | APROBADO | 52 s |
| `03-reserva-cita.yaml` | cita | APROBADO | 53 s |
| `04-consulta-y-cancelacion.yaml` | cancelar | APROBADO | 47 s |
| `05-resultados.yaml` | resultados | APROBADO | 36 s |
| `06-recetas.yaml` | recetas | APROBADO | 38 s |
| `07-notificaciones.yaml` | notificaciones | APROBADO | 37 s |
| `08-cierre-de-sesion.yaml` | base | APROBADO | 43 s |

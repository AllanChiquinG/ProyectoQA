# MediCore · Pruebas de rendimiento (k6)

Escenario: mitad de los usuarios virtuales reserva citas y la otra mitad consulta agenda, pacientes, catálogo y expediente (`tests/k6/medicore.js`). Se ejecuta con `scripts/perf-tests.sh <perfil>` contra el ambiente QA.

## Umbrales declarados antes de ejecutar

| Perfil | Carga | p95 lectura | p95 reserva | Error inesperado |
|---|---|---|---|---|
| Load | 30 usuarios durante 3 min | < 800 ms | < 1000 ms | < 1 % |
| Stress | sube hasta 150 usuarios | informativo | informativo | < 5 % |
| Spike | salto de 10 a 200 usuarios | < 2000 ms | < 3000 ms | < 2 % |

Una reserva rechazada con 409 es el comportamiento correcto (horario ocupado) y no cuenta como error. Además, al terminar debe cumplirse: ninguna cita activa solapada por médico o paciente, y ningún mensaje en la cola de mensajes fallidos.

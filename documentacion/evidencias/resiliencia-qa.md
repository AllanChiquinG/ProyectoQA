# Evidencia de resiliencia en QA

Ejecutada el 2026-11-06 02:16 con `scripts/evidencia_resiliencia.py` sobre el ambiente QA en contenedores. Solo datos ficticios.

## 1. Redis detenido

- Antes, `GET /api/doctors` con sesión: **200**
- `docker stop` de Redis. Login: **503**
- El mismo token: **401**
- Redis iniciado de nuevo. Login: **200**, `GET /api/doctors`: **200**

## 2. Worker detenido

- `docker stop` de `medicore-qa-worker-1`. Registrar resultado: **201**
- 5 segundos después, lectura: **200**, notificación: **Pending**
- Servicio iniciado de nuevo. Notificación enviada en **0.0 s**, estado: **Sent**
- Notificaciones del paciente para ese resultado: **1**

## 3. RabbitMQ detenido

- `docker stop` de `medicore-qa-rabbitmq-1`. Registrar resultado: **201**
- 5 segundos después, lectura: **200**, notificación: **Pending**
- Servicio iniciado de nuevo. Notificación enviada en **1.0 s**, estado: **Sent**
- Notificaciones del paciente para ese resultado: **1**


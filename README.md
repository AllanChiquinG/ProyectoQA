# MediCore

Plataforma hospitalaria: web del personal (Razor Pages), API (ASP.NET Core) y app móvil del paciente (.NET MAUI, Android). Proyecto 2 del Proyecto Final de QA. **Estado: estructura inicial (entrega 0).** Ver el plan en [documentacion/02-plan-de-trabajo.md](documentacion/02-plan-de-trabajo.md).

## Requisitos

| Herramienta | Para qué | Versión |
|---|---|---|
| .NET SDK | compilar y ejecutar (hoy) | 10.0.x |
| Git | control de versiones | cualquiera reciente |
| Visual Studio 2022 17.14+ o 2026 | IDE (soporta `.slnx`) | opcional |
| Docker Desktop | Postgres, Redis, RabbitMQ (entrega 1) | opcional por ahora |
| Workload MAUI + Android SDK | app móvil (entrega 9) | opcional por ahora |
| Terraform, Ansible | infraestructura (entrega 12) | opcional por ahora |

## Ejecutar

Abre `MediCore.slnx` en Visual Studio, o desde la terminal en la raíz:

```bash
dotnet build MediCore.slnx
dotnet test MediCore.slnx
dotnet run --project src/MediCore.Api --launch-profile http   # http://localhost:5011/api/health
dotnet run --project src/MediCore.Web --launch-profile http   # http://localhost:5298
```

Con la API en Development, el contrato OpenAPI está en `http://localhost:5011/openapi/v1.json`.

## Estructura

```
MediCore.slnx
src/MediCore.Api        Web API
src/MediCore.Web        Razor Pages del personal
tests/MediCore.Api.Tests
documentacion/          alcance, plan de trabajo, trazabilidad
Docs/  Referencias/     documento del proyecto y diseño aprobado (solo lectura)
```

## Seguridad

No se guardan secretos en el repositorio. Copia `.env.example` a `.env` (ignorado por Git) cuando una entrega lo requiera. Todos los datos de ejemplo son ficticios.

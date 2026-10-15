# MediCore

Plataforma hospitalaria: web del personal (Razor Pages), API (ASP.NET Core) y app móvil del paciente (.NET MAUI, Android). Proyecto 2 del Proyecto Final de QA. **Estado: entrega 2 (identidad y acceso) en curso.** Ver el plan en [documentacion/02-plan-de-trabajo.md](documentacion/02-plan-de-trabajo.md).

## Requisitos

| Herramienta | Para qué | Versión |
|---|---|---|
| .NET SDK | compilar y ejecutar (hoy) | 10.0.x |
| Git | control de versiones | cualquiera reciente |
| Visual Studio 2022 17.14+ o 2026 | IDE (soporta `.slnx`) | opcional |
| Docker Desktop | PostgreSQL, Redis, RabbitMQ y pruebas de integración | necesario desde la entrega 1 |
| Workload MAUI + Android SDK | app móvil (entrega 9) | opcional por ahora |
| Terraform, Ansible | infraestructura (entrega 12) | opcional por ahora |

## Ejecutar

Primera vez (dependencias y base de datos):

```bash
cp .env.example .env                 # cambia las contraseñas; .env no se versiona
docker compose up -d                 # PostgreSQL (puerto 5433), Redis y RabbitMQ (panel en http://localhost:15672)
dotnet tool restore                  # instala dotnet-ef (herramienta local del repositorio)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5433;Database=medicore;Username=medicore;Password=<POSTGRES_PASSWORD de tu .env>" --project src/MediCore.Api
```

Aplicar migraciones (`database update` lee la cadena de la variable de entorno; ejemplo en PowerShell):

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5433;Database=medicore;Username=medicore;Password=<POSTGRES_PASSWORD>"
dotnet ef database update --project src/MediCore.Infrastructure --startup-project src/MediCore.Api
```

Secretos locales de la API (inventa valores propios; nunca se versionan):

```bash
dotnet user-secrets set "ConnectionStrings:Redis" "127.0.0.1:6379,password=<REDIS_PASSWORD de tu .env>" --project src/MediCore.Api
dotnet user-secrets set "Jwt:SigningKey" "<texto aleatorio de 32 caracteres o más>" --project src/MediCore.Api
dotnet user-secrets set "Seed:DevPassword" "<contraseña de desarrollo para los usuarios ficticios>" --project src/MediCore.Api
```

Usa `127.0.0.1` y no `localhost` para Redis: Docker publica el puerto solo en IPv4 y el cliente puede intentar IPv6 primero.

Al arrancar la API en Development se aplican las migraciones y, si la tabla de usuarios está vacía, se crean usuarios ficticios con la contraseña de `Seed:DevPassword`: `elena.vargas@example.com` (Recepción), `lucia.mendez@example.com` (Médico), `daniela.reyes@example.com` (Laboratorio), `admin@example.com` (Administrador) y `ana.garcia@example.com` (Paciente, solo móvil).

El puerto 5433 evita choques con un PostgreSQL local en 5432 (cámbialo con `POSTGRES_PORT`).

Después:

Abre `MediCore.slnx` en Visual Studio, o desde la terminal en la raíz (la web necesita la API en marcha para iniciar sesión):

```bash
dotnet build MediCore.slnx
dotnet test MediCore.slnx             # las de integración levantan PostgreSQL y Redis con Testcontainers (requiere Docker)
dotnet run --project src/MediCore.Api --launch-profile http   # http://localhost:5011/api/health  (y /api/health/ready comprueba PostgreSQL)
dotnet run --project src/MediCore.Web --launch-profile http   # http://localhost:5298
```

Con la API en Development, el contrato OpenAPI está en `http://localhost:5011/openapi/v1.json`.

## Estructura

```
MediCore.slnx
docker-compose.yml      dependencias locales
src/MediCore.Api        Web API
src/MediCore.Contracts  contratos compartidos API/Web/móvil (DTO, roles)
src/MediCore.Domain     entidades y reglas (sin dependencias)
src/MediCore.Infrastructure  EF Core, PostgreSQL, Redis, migraciones
src/MediCore.Web        Razor Pages del personal
tests/MediCore.Api.Tests   integración (Testcontainers)
tests/MediCore.UnitTests   reglas de dominio
documentacion/          alcance, plan de trabajo, trazabilidad
Docs/  Referencias/     documento del proyecto y diseño aprobado (solo lectura)
```

## Seguridad

No se guardan secretos en el repositorio. Copia `.env.example` a `.env` (ignorado por Git) cuando una entrega lo requiera. Todos los datos de ejemplo son ficticios.

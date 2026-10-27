# MediCore

Plataforma hospitalaria: web del personal (Razor Pages), API (ASP.NET Core) y app móvil del paciente (.NET MAUI, Android). Proyecto 2 del Proyecto Final de QA. **Estado: entrega 4 (disponibilidad, agenda y citas) en curso.** Ver el plan en [documentacion/02-plan-de-trabajo.md](documentacion/02-plan-de-trabajo.md).

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

Al arrancar la API en Development se aplican las migraciones y, si la tabla de usuarios está vacía, se crean usuarios ficticios con la contraseña de `Seed:DevPassword`: `elena.vargas@example.com` (Recepción), `lucia.mendez@example.com` (Médico), `daniela.reyes@example.com` (Laboratorio), `admin@example.com` (Administrador) y `ana.garcia@example.com` (Paciente, solo móvil). También se crean 4 pacientes y 3 médicos ficticios del diseño (Ana García queda vinculada a su cuenta y la Dra. Lucía Méndez a la suya). Cada médico recibe un horario de lunes a viernes de 08:00 a 12:00 y la Dra. Méndez cuatro citas el próximo día hábil, como en la agenda del diseño.

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

## Agenda y citas

- Las horas son del reloj de la clínica (`Clinic:TimeZone`, por defecto `America/Guatemala`); se puede reservar desde hoy hasta `Scheduling:MaxDaysAhead` días (90). Las citas duran 30 minutos.
- La disponibilidad se cachea en Redis (`Cache:AvailabilityTtl`, 2 min) y se invalida al reservar, cancelar o cambiar el horario del médico.
- Para reintentar sin duplicar, `POST /api/appointments` acepta el encabezado `Idempotency-Key` (8 a 64 caracteres, letras, números, `-` y `_`). La web lo genera en cada formulario de reserva.
- Una cita no se puede solapar con otra activa del mismo médico ni del mismo paciente: lo garantiza PostgreSQL (extensión `btree_gist`, que incluye la imagen oficial de Docker; en Azure Database for PostgreSQL debe habilitarse en la lista de extensiones permitidas).

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

## Estado de las pruebas

Hoy: 128 pruebas xUnit superadas y 1 omitida a propósito (la relación médico–paciente por consulta, pendiente de la entrega 5), entre unitarias e integración con PostgreSQL y Redis reales. **Pendientes y obligatorios según el documento del proyecto**: Cypress (web), colección de Insomnia (API), Maestro (móvil) y pruebas de rendimiento con k6 o JMeter. Detalle en [documentacion/03-matriz-trazabilidad.md](documentacion/03-matriz-trazabilidad.md); los hallazgos y la verificación manual en [documentacion/04-registro-de-hallazgos.md](documentacion/04-registro-de-hallazgos.md).

## Seguridad

No se guardan secretos en el repositorio. Copia `.env.example` a `.env` (ignorado por Git) cuando una entrega lo requiera. Todos los datos de ejemplo son ficticios.

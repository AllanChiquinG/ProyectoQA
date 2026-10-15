# MediCore

Plataforma hospitalaria (Proyecto 2 de `Docs/Proyecto Final QA.docx`, curso 048 Aseguramiento de la Calidad, UMG). Web para el personal, app Android para el paciente, API backend. Todo el producto en **español**.

## Reglas del proyecto

- **No modificar `Docs/` ni `Referencias/`.** Son entradas de solo lectura. (Windows no distingue mayúsculas: la documentación propia va en `documentacion/`, nunca en `docs/`.)
- **No inventar requisitos.** Alcance = Proyecto 2 + requisitos generales del documento. Ver `documentacion/01-alcance-y-pantallas.md`.
- **Fuera de alcance:** portal web del paciente, módulos no listados, microservicios.
- **Diseño:** la referencia es `Referencias/medicore/diseno/`. Apariencia final = `style.css` y luego `appearance.css` (copias sin modificar en `src/MediCore.Web/wwwroot/css/`; ajustes propios van en un tercer archivo cargado después). `app.js` define pantallas, ids (WEB-xx / MOB-xx), textos y recorridos.
- **Nunca copiar** la barra del prototipo (selector Web/Móvil y rol) ni sus datos/reglas en memoria. Acceso y navegación salen del usuario autenticado.
- Cambios de accesibilidad/adaptación permitidos sin alterar la identidad visual; explicarlos al hacerlos.
- **Sin secretos en el repo.** Solo datos ficticios. Configuración sensible por variables de entorno / user-secrets; `.env.example` con valores de ejemplo.
- Una entrega pequeña a la vez; actualizar `documentacion/03-matriz-trazabilidad.md` en cada una. No declarar comprobaciones que no se ejecutaron.

## Decisiones técnicas acordadas

- .NET 10 / C#. Solución `MediCore.slnx`, SDK fijado en `global.json` (10.0.401, `latestFeature`).
- Monolito modular: un despliegue, módulos con límites claros (Identidad, Pacientes, Médicos, Agenda, Consultas, Recetas, Laboratorio, Notificaciones, Auditoría). Sin microservicios.
- Web del personal: ASP.NET Core Razor Pages (`src/MediCore.Web`). Backend: ASP.NET Core Web API con controladores (`src/MediCore.Api`), OpenAPI documentado.
- Móvil: .NET MAUI solo Android (`src/MediCore.Mobile`, aún no creado; requiere workload `maui-android` + Android SDK).
- Datos: EF Core + PostgreSQL. Cache/sesiones: Redis. Mensajería: RabbitMQ (productor, consumidor, reintentos, DLQ, idempotencia). Para que una caída del broker no pierda datos clínicos: guardar primero en BD y publicar con outbox transaccional.
- Agenda: la prevención de doble reserva se garantiza en la base de datos (restricción única + transacción/concurrencia optimista), no en memoria ni solo en cache.
- Infra: Azure + Terraform (ruta A), Ansible, Docker/Compose, GitHub, Jenkins.
- Autorización por rol **y** por recurso (un paciente solo ve lo suyo; un médico solo lo autorizado). Auditoría de accesos y cambios clínicos.
- Decisión pendiente (entrega 2): mecanismo de autenticación (propuesta: cookie en web, JWT corto + refresh en móvil/API, sesiones en Redis).

## Estructura

```
MediCore.slnx          solución (abrir en Visual Studio)
src/MediCore.Api       Web API
src/MediCore.Domain    entidades por módulo, sin dependencias externas
src/MediCore.Infrastructure  EF Core/Npgsql, migraciones (Persistence/Migrations), futuros Redis/RabbitMQ
docker-compose.yml     Postgres 17 (5433), Redis 7, RabbitMQ 4; credenciales desde .env
src/MediCore.Web       Razor Pages (personal)
tests/MediCore.Api.Tests  xUnit + WebApplicationFactory
documentacion/         alcance, plan, trazabilidad (propios)
Docs/, Referencias/    entradas, no tocar
```

Puertos de desarrollo: API `http://localhost:5011` (https 7293), Web `http://localhost:5298` (https 7216).

## Comandos

```bash
dotnet build MediCore.slnx
dotnet test MediCore.slnx
dotnet run --project src/MediCore.Api --launch-profile http
dotnet run --project src/MediCore.Web --launch-profile http
dotnet format MediCore.slnx --verify-no-changes
```

Base de datos (ver README para variables):

```bash
docker compose up -d
dotnet ef migrations add <Nombre> --project src/MediCore.Infrastructure --startup-project src/MediCore.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/MediCore.Infrastructure --startup-project src/MediCore.Api   # requiere ConnectionStrings__Postgres en el entorno
```

Cadena de conexión: user-secrets (`ConnectionStrings:Postgres`) en la API; nunca en appsettings. `dotnet ef` usa `DesignTimeDbContextFactory` (variable de entorno). Flujo Git: ramas `feature/*`, PR hacia `main`; `main` debe protegerse en GitHub.

Advertencias tratadas como errores (`Directory.Build.props`).

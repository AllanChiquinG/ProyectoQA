# syntax=docker/dockerfile:1
# Aplica las migraciones de EF Core y termina (paquete autocontenido "efbundle"). Lee ConnectionStrings__Postgres del entorno.
#   docker build -f docker/migrator.Dockerfile -t medicore/migrator:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props dotnet-tools.json ./
COPY src/MediCore.Api/MediCore.Api.csproj src/MediCore.Api/
COPY src/MediCore.Contracts/MediCore.Contracts.csproj src/MediCore.Contracts/
COPY src/MediCore.Domain/MediCore.Domain.csproj src/MediCore.Domain/
COPY src/MediCore.Infrastructure/MediCore.Infrastructure.csproj src/MediCore.Infrastructure/
RUN dotnet tool restore && dotnet restore src/MediCore.Api/MediCore.Api.csproj
COPY src/MediCore.Api src/MediCore.Api
COPY src/MediCore.Contracts src/MediCore.Contracts
COPY src/MediCore.Domain src/MediCore.Domain
COPY src/MediCore.Infrastructure src/MediCore.Infrastructure
RUN dotnet ef migrations bundle --project src/MediCore.Infrastructure --startup-project src/MediCore.Api \
    --self-contained -r linux-x64 --output /out/efbundle --force

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

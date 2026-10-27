# syntax=docker/dockerfile:1
# API de MediCore. Contexto de construcción: la raíz del repositorio.
#   docker build -f docker/api.Dockerfile -t medicore/api:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Primero solo los archivos de proyecto: la restauración de paquetes queda en caché mientras no cambien las dependencias.
COPY global.json Directory.Build.props ./
COPY src/MediCore.Api/MediCore.Api.csproj src/MediCore.Api/
COPY src/MediCore.Contracts/MediCore.Contracts.csproj src/MediCore.Contracts/
COPY src/MediCore.Domain/MediCore.Domain.csproj src/MediCore.Domain/
COPY src/MediCore.Infrastructure/MediCore.Infrastructure.csproj src/MediCore.Infrastructure/
RUN dotnet restore src/MediCore.Api/MediCore.Api.csproj
COPY src/MediCore.Api src/MediCore.Api
COPY src/MediCore.Contracts src/MediCore.Contracts
COPY src/MediCore.Domain src/MediCore.Domain
COPY src/MediCore.Infrastructure src/MediCore.Infrastructure
RUN dotnet publish src/MediCore.Api/MediCore.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
# Usuario sin privilegios (incluido en la imagen base). Puerto interno 8080: solo lo alcanza el proxy inverso.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=QA \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "MediCore.Api.dll"]

# syntax=docker/dockerfile:1
# Web del personal (Razor Pages). Contexto de construcción: la raíz del repositorio.
#   docker build -f docker/web.Dockerfile -t medicore/web:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/MediCore.Web/MediCore.Web.csproj src/MediCore.Web/
COPY src/MediCore.Contracts/MediCore.Contracts.csproj src/MediCore.Contracts/
RUN dotnet restore src/MediCore.Web/MediCore.Web.csproj
COPY src/MediCore.Web src/MediCore.Web
COPY src/MediCore.Contracts src/MediCore.Contracts
# Los archivos estáticos se publican con huella al compilar (ver CLAUDE.md): no se copian a mano.
RUN dotnet publish src/MediCore.Web/MediCore.Web.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
# Carpeta de las claves de protección de datos (volumen): debe pertenecer al usuario sin privilegios para que el volumen nuevo herede el permiso.
RUN mkdir /keys && chown "$APP_UID" /keys
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=QA \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "MediCore.Web.dll"]

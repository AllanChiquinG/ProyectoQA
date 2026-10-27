# syntax=docker/dockerfile:1
# Proceso de mensajería: publicador del outbox y consumidor de notificaciones. Sin HTTP.
#   docker build -f docker/worker.Dockerfile -t medicore/worker:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/MediCore.Worker/MediCore.Worker.csproj src/MediCore.Worker/
COPY src/MediCore.Domain/MediCore.Domain.csproj src/MediCore.Domain/
COPY src/MediCore.Infrastructure/MediCore.Infrastructure.csproj src/MediCore.Infrastructure/
RUN dotnet restore src/MediCore.Worker/MediCore.Worker.csproj
COPY src/MediCore.Worker src/MediCore.Worker
COPY src/MediCore.Domain src/MediCore.Domain
COPY src/MediCore.Infrastructure src/MediCore.Infrastructure
RUN dotnet publish src/MediCore.Worker/MediCore.Worker.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV DOTNET_ENVIRONMENT=QA \
    DOTNET_EnableDiagnostics=0 \
    Worker__HeartbeatFile=/tmp/medicore-worker.alive
ENTRYPOINT ["dotnet", "MediCore.Worker.dll"]

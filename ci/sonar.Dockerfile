# SDK de .NET con el escáner de SonarQube para C#.
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get install -y --no-install-recommends default-jre-headless && rm -rf /var/lib/apt/lists/*
RUN dotnet tool install --global dotnet-sonarscanner --version 10.3.0
ENV PATH="$PATH:/root/.dotnet/tools"

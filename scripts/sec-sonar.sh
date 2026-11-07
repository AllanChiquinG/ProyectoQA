#!/usr/bin/env bash
# Analiza el código con SonarQube y guarda el estado del Quality Gate en reports/sonarqube/.
# Variables: SONAR_TOKEN (obligatoria), SONAR_HOST_URL (por defecto http://sonarqube:9000), SONAR_NETWORK.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
host="${SONAR_HOST_URL:-http://sonarqube:9000}"
network="${SONAR_NETWORK:-medicore-ci}"
key="medicore"
mkdir -p reports/sonarqube

docker build -q -t medicore/sonar-scanner -f ci/sonar.Dockerfile ci >/dev/null
MSYS_NO_PATHCONV=1 docker run --rm --network "$network" \
  -v "$root:/repo:ro" -v medicore-nuget:/root/.nuget/packages \
  -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e SONAR_TOKEN="$SONAR_TOKEN" -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  medicore/sonar-scanner bash -c "
    set -e
    mkdir -p /work
    tar -C /repo --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude=node_modules --exclude=./reports --exclude=./artifacts --exclude=./.vs --exclude=./Referencias -cf - . | tar -C /work -xf -
    cd /work && rm -f global.json
    dotnet sonarscanner begin /k:$key /n:MediCore /d:sonar.host.url=$host /d:sonar.token=\$SONAR_TOKEN \
      /d:sonar.exclusions='**/Migrations/**,**/wwwroot/css/style.css,**/wwwroot/css/appearance.css,tests/cypress/node_modules/**' /d:sonar.scanner.skipJreProvisioning=true       /d:sonar.cs.opencover.reportsPaths='tests/**/TestResults/**/coverage.opencover.xml'       /d:sonar.coverage.exclusions='tests/**,scripts/**,ci/**,infrastructure/**,src/MediCore.Mobile/**,**/wwwroot/**,**/*.cshtml,**/*Seeder.cs,**/DevelopmentBootstrap.cs'
    dotnet build MediCore.slnx -c Release
    dotnet test MediCore.slnx -c Release --no-build --collect:'XPlat Code Coverage' -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
    dotnet sonarscanner end /d:sonar.token=\$SONAR_TOKEN
  "

# El servidor procesa el análisis en segundo plano: se espera y se consulta el resultado.
query() {
  MSYS_NO_PATHCONV=1 docker run --rm --network "$network" curlimages/curl:8.13.0 -s -u "$SONAR_TOKEN:" "$host$1"
}
for _ in $(seq 1 60); do
  status="$(query "/api/ce/component?component=$key" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(len(d.get("queue",[])))' 2>/dev/null || python -c 'print(1)')"
  [ "$status" = "0" ] && break
  sleep 5
done
query "/api/qualitygates/project_status?projectKey=$key" > reports/sonarqube/quality-gate.json
query "/api/measures/component?component=$key&metricKeys=bugs,vulnerabilities,security_hotspots,code_smells,coverage,duplicated_lines_density,ncloc,reliability_rating,security_rating,sqale_rating" > reports/sonarqube/medidas.json
cat reports/sonarqube/quality-gate.json; echo

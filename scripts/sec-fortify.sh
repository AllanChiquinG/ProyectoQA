#!/usr/bin/env bash
# Fortify SCA. Requiere licencia e instalación propia: FORTIFY_SCA_PATH apunta a la carpeta bin de Fortify.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${FORTIFY_SCA_PATH:?Define FORTIFY_SCA_PATH: Fortify requiere licencia}"
mkdir -p reports/fortify
"$FORTIFY_SCA_PATH/sourceanalyzer" -b medicore -clean
"$FORTIFY_SCA_PATH/sourceanalyzer" -b medicore dotnet build MediCore.slnx
"$FORTIFY_SCA_PATH/sourceanalyzer" -b medicore -scan -f reports/fortify/medicore.fpr

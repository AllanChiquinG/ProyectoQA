#!/usr/bin/env bash
# Valida la infraestructura como código y los contenedores SIN crear nada en la nube y sin instalar herramientas en el equipo: todo corre en
# contenedores oficiales. Esto es validación de ARCHIVOS (formato, sintaxis, esquema); NO es un despliegue en Azure ni prueba que el ambiente funcione allí.
#   Terraform: fmt, init sin backend, validate (infrastructure/terraform/{azure,bootstrap})
#   Ansible:   sintaxis del playbook y ansible-lint (infrastructure/ansible)
#   Docker:    hadolint sobre los Dockerfile y "docker compose config" del ambiente QA
# Salida distinta de 0 si algo falla.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
failures=0
step() { echo; echo "=== $1"; }
run() { MSYS_NO_PATHCONV=1 "$@" || failures=$((failures + 1)); }

step "Terraform (hashicorp/terraform:1.12): fmt, init -backend=false, validate"
run docker run --rm --entrypoint sh -v "$root/infrastructure/terraform:/src:ro" hashicorp/terraform:1.12 -c '
  set -e
  cp -r /src /work && cd /work
  terraform version | head -1
  terraform fmt -check -recursive -diff . && echo "fmt: formato correcto"
  for m in azure bootstrap; do
    cd /work/$m
    terraform init -backend=false -input=false -no-color >/dev/null
    echo -n "validate $m: "; terraform validate -no-color | head -1
  done'

step "Ansible (ansible-core + ansible-lint): sintaxis y lint"
run docker run --rm -v "$root/infrastructure/ansible:/src:ro" python:3.12-slim bash -c '
  set -e
  pip install -q --no-cache-dir ansible-core ansible-lint >/dev/null 2>&1
  cp -r /src /work && cd /work
  ansible-galaxy collection install -r requirements.yml >/dev/null
  ansible --version | head -1
  ANSIBLE_CONFIG=/work/ansible.cfg ansible-playbook -i inventory/qa.ini.example --syntax-check site.yml
  ansible-lint site.yml && echo "ansible-lint: sin problemas"'

step "Dockerfiles (hadolint)"
for f in api web worker migrator; do
  echo -n "docker/$f.Dockerfile: "
  if MSYS_NO_PATHCONV=1 docker run --rm -i hadolint/hadolint hadolint --failure-threshold warning - < "docker/$f.Dockerfile"; then echo "sin advertencias ni errores (los avisos informativos DL3066 listados arriba son esperados: se usa el UID numérico de la imagen)"; else failures=$((failures + 1)); fi
done

step "Docker Compose del ambiente QA (config)"
run docker compose -p medicore-qa --env-file .env.qa.example -f docker-compose.yml -f docker-compose.qa.yml config -q
echo "compose config: válido"

echo
[ "$failures" -eq 0 ] && echo "Validación de archivos correcta (NO es un despliegue en Azure)." || { echo "$failures validación(es) fallaron."; exit 1; }

#!/usr/bin/env bash
# Ejecuta la infraestructura como código SIN Azure y sin costo. Esto NO es un despliegue en la nube: no hay suscripción, IP pública ni certificado real.
#   terraform  ciclo aprovisionar, modificar y destruir con el proveedor de Azure simulado (tests/simulacion.tftest.hcl)
#   ansible    aplica infrastructure/ansible/site.yml sobre un Ubuntu 24.04 desechable (contenedor con systemd y Docker propio), dos veces, y prueba la aplicación
# Resultados en reports/iac/. El contenedor y su volumen se eliminan al terminar.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root="$(pwd -W 2>/dev/null || pwd)"
server="medicore-iac-simulada"
volume="medicore-iac-docker"
mkdir -p reports/iac

run_terraform() {
  MSYS_NO_PATHCONV=1 docker run --rm --entrypoint sh -v "$root/infrastructure/terraform:/src:ro" hashicorp/terraform:1.12 -c '
    cp -r /src /work && cd /work/azure && terraform init -backend=false -input=false -no-color >/dev/null && terraform test -no-color' 2>&1 | tee reports/iac/terraform-test.txt
  return "${PIPESTATUS[0]}"
}

cleanup() {
  docker rm -f "$server" >/dev/null 2>&1
  docker volume rm "$volume" "$volume-containerd" >/dev/null 2>&1
}

run_ansible() {
  cleanup
  trap cleanup EXIT
  docker build -q -t medicore-iac-ubuntu:local -f scripts/iac/ubuntu.Dockerfile scripts/iac >/dev/null || return 1
  # Privilegiado solo para que el Docker interno funcione. El repositorio se monta de solo lectura.
  MSYS_NO_PATHCONV=1 docker run -d --name "$server" --hostname medicore-qa --privileged --cgroupns=host -v /sys/fs/cgroup:/sys/fs/cgroup:rw \
    -v "$volume:/var/lib/docker" -v "$volume-containerd:/var/lib/containerd" -v "$root:/repo:ro" -p 127.0.0.1:8088:80 medicore-iac-ubuntu:local >/dev/null || return 1
  for _ in $(seq 1 30); do
    state="$(docker exec "$server" systemctl is-system-running 2>/dev/null)"
    case "$state" in running|degraded) break ;; esac
    sleep 2
  done
  MSYS_NO_PATHCONV=1 docker exec "$server" bash /repo/scripts/iac/aplicar-ansible.sh 2>&1 | tee reports/iac/ansible.txt
  return "${PIPESTATUS[0]}"
}

case "${1:-all}" in
  terraform) run_terraform ;;
  ansible) run_ansible ;;
  all) run_terraform && run_ansible ;;
  *) echo "Uso: $0 [terraform|ansible|all]" >&2; exit 2 ;;
esac

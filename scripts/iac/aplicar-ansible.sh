#!/usr/bin/env bash
# Corre DENTRO del servidor desechable de scripts/iac-simulacion.sh: instala Ansible, aplica site.yml dos veces y prueba la aplicación.
# Lo único distinto de Azure: conexión local en vez de SSH, sin disco de datos, repositorio local y secretos aleatorios de un solo uso.
set -euo pipefail

python3 -m venv /opt/ansible
/opt/ansible/bin/pip -q install ansible-core
cp -r /repo/infrastructure/ansible /opt/playbook
cd /opt/playbook
export PATH="/opt/ansible/bin:$PATH" ANSIBLE_HOST_KEY_CHECKING=False ANSIBLE_NOCOLOR=1
ansible-galaxy collection install -r requirements.yml >/dev/null

git config --global --add safe.directory '*'
git clone -q --no-hardlinks /repo /opt/repo-simulado
commit="$(git -C /repo rev-parse HEAD)"
random() { head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "$1"; }
cat > /opt/playbook/inventory/qa.ini <<EOF
[qa]
medicore-qa ansible_connection=local ansible_python_interpreter=/usr/bin/python3
EOF
cat > /opt/playbook/group_vars/qa/simulacion.yml <<EOF
medicore_repo_url: /opt/repo-simulado
medicore_version: $commit
medicore_data_disk_device: ""
medicore_site_address: ":80"
medicore_seed: true
vault_postgres_password: "$(random 28)"
vault_redis_password: "$(random 28)"
vault_rabbitmq_password: "$(random 28)"
vault_jwt_signing_key: "$(random 48)"
vault_seed_password: "Qa-$(random 20)-1!"
EOF

echo "=== Primera aplicación (commit $commit)"
ansible-playbook site.yml
echo "=== Segunda aplicación (idempotencia)"
ansible-playbook site.yml | tee /tmp/segunda.txt

echo "=== Comprobación de la aplicación"
for _ in $(seq 1 30); do
  code="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1/api/health || true)"
  [ "$code" = 200 ] && break
  sleep 2
done
echo "GET /api/health = $code"
echo "GET /Login = $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1/Login)"
docker compose -p medicore-qa --env-file /opt/medicore/.env.qa -f /opt/medicore/docker-compose.yml -f /opt/medicore/docker-compose.qa.yml ps --format '{{.Service}} {{.Status}}'
ufw status | head -8
[ "$code" = 200 ]

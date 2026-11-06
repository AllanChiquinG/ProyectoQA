#!/bin/sh
# Respaldo cifrado de PostgreSQL. Uso: respaldo.sh ciclo | una | restaurar <archivo>
# Cada copia se comprueba antes de darla por buena: se descifra y pg_restore la lee completa, sin tocar la base. Una copia que falla se borra y se registra.
# En las bitácoras solo hay nombres y tamaños: nunca la clave ni datos.
set -eu
# Sin esto, un pg_dump que falla a medias pasaría inadvertido: la tubería solo reportaría a openssl.
set -o pipefail

dir="${BACKUP_DIR:-/backups}"
every="${BACKUP_INTERVAL_HOURS:-6}"
keep="${BACKUP_KEEP:-28}"
export PGHOST="${PGHOST:-postgres}" PGUSER="$POSTGRES_USER" PGDATABASE="$POSTGRES_DB" PGPASSWORD="$POSTGRES_PASSWORD"

cipher() { openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -pass env:BACKUP_PASSPHRASE "$@"; }

one() {
  name="medicore-$(date -u +%Y%m%dT%H%M%SZ).dump.enc"
  temp="$dir/.$name.tmp"
  umask 077
  if ! pg_dump --format=custom --no-owner --no-privileges | cipher -salt -out "$temp" || ! cipher -d -in "$temp" | pg_restore --file=/dev/null; then
    rm -f "$temp"
    echo "respaldo: FALLÓ la comprobación de $name" >&2
    return 1
  fi
  mv "$temp" "$dir/$name"
  (cd "$dir" && sha256sum "$name" > "$name.sha256")
  echo "respaldo: $name ($(wc -c < "$dir/$name") bytes)"
  # Retención: conserva las copias más recientes.
  ls -1 "$dir"/medicore-*.dump.enc | sort -r | tail -n +"$((keep + 1))" | while read -r old; do
    rm -f "$old" "$old.sha256"
    echo "respaldo: retirado $(basename "$old")"
  done
}

restore() {
  file="$1"
  (cd "$dir" && sha256sum -c "$(basename "$file").sha256" >/dev/null)
  cipher -d -in "$dir/$(basename "$file")" | pg_restore --clean --if-exists --no-owner --no-privileges --single-transaction --dbname="$PGDATABASE"
  echo "restauración: $(basename "$file") aplicada"
}

case "${1:-ciclo}" in
  una) one ;;
  restaurar) restore "$2" ;;
  ciclo)
    until pg_isready -q; do sleep 2; done
    while true; do
      one || echo "respaldo: se reintentará en el próximo ciclo" >&2
      sleep "$((every * 3600))"
    done
    ;;
  *) echo "Uso: respaldo.sh ciclo|una|restaurar <archivo>" >&2; exit 2 ;;
esac

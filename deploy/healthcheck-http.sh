#!/usr/bin/env bash
# Comprobación de salud HTTP sin curl (las imágenes de ASP.NET no lo incluyen). Uso: healthcheck-http.sh <puerto> <ruta>
# Sale con 0 solo si la respuesta es 200.
set -u
port="${1:?puerto}"
path="${2:?ruta}"
exec 3<>"/dev/tcp/127.0.0.1/${port}" || exit 1
printf 'GET %s HTTP/1.0\r\nHost: localhost\r\n\r\n' "$path" >&3
read -r status <&3
case "$status" in
  *" 200 "*) exit 0 ;;
  *) exit 1 ;;
esac

# Proxy inverso de QA. La configuración va dentro de la imagen: un montaje desde el anfitrión se pierde si Jenkins creó el contenedor y Docker se reinicia.
FROM caddy:2-alpine
COPY deploy/caddy/Caddyfile /etc/caddy/Caddyfile

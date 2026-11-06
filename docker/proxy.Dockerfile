# Proxy inverso de QA. La configuración va dentro de la imagen: un montaje desde el anfitrión se pierde si Jenkins creó el contenedor y Docker se reinicia.
FROM caddy:2-alpine
COPY deploy/caddy/Caddyfile /etc/caddy/Caddyfile
# Sin root: la imagen oficial ya permite a caddy abrir los puertos 80 y 443.
RUN addgroup -S caddy \
    && adduser -S -G caddy caddy \
    && mkdir -p /data/caddy /config/caddy \
    && chown -R caddy:caddy /data /config
USER caddy

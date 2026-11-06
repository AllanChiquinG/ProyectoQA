# Respaldos de PostgreSQL. Misma versión mayor que el servidor: pg_dump no puede leer un servidor más nuevo.
FROM postgres:17-alpine
# hadolint ignore=DL3018
RUN apk add --no-cache openssl \
    && mkdir /backups \
    && chown postgres:postgres /backups
COPY deploy/respaldos/respaldo.sh /usr/local/bin/respaldo.sh
USER postgres
ENTRYPOINT ["sh", "/usr/local/bin/respaldo.sh"]
CMD ["ciclo"]

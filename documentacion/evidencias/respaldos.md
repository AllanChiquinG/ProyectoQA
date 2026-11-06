# Respaldos automáticos y simulacro de restauración

Ejecutado el 2026-11-06 en el ambiente QA local, con `scripts/simulacro-respaldo.sh`. Resultado completo: `reports/respaldos/simulacro.txt`. Cierra H8-08 y reduce R-16.

## Cómo funciona

El servicio `backup` de `docker-compose.qa.yml` ejecuta `deploy/respaldos/respaldo.sh`:

- Crea una copia al arrancar y luego cada 6 horas (`BACKUP_INTERVAL_HOURS`).
- Conserva las 28 más recientes (`BACKUP_KEEP`), o sea 7 días.
- Cifra con AES-256 y `BACKUP_PASSPHRASE`. Sin esa clave la copia no se puede leer.
- Descifra y lee cada copia completa antes de aceptarla. Si falla, la borra y lo registra.
- Guarda una huella SHA-256 y la verifica antes de restaurar.
- Marca el servicio como no saludable si no hay una copia reciente.
- Registra solo nombres y tamaños, nunca datos ni la clave.

Las copias van en el volumen externo `medicore-qa-respaldos`, que sobrevive a `qa.sh destroy`.

```bash
scripts/qa.sh backup                  # copia inmediata
scripts/qa.sh backups                 # lista las copias
scripts/qa.sh export artifacts/copia  # las saca de Docker
scripts/qa.sh import <archivo>        # devuelve una copia exportada
scripts/qa.sh restore <archivo>       # reemplaza la base, pide confirmación
```

## Simulacro

1. Se tomó la huella md5 y el conteo de filas de cada tabla.
2. Se creó una copia y se exportó fuera de Docker.
3. **Desastre:** se destruyeron la base y también el volumen de respaldos.
4. Se reconstruyó QA. La base quedó con 0 usuarios.
5. Se importó la copia exportada y se restauró.
6. Se compararon las tablas y se ejecutaron las pruebas de humo.

| Medida | Resultado |
|---|---|
| Tablas comparadas | 14 |
| Filas antes y después | 36 849 y 36 849 |
| Tablas idénticas | Sí, en filas y md5 |
| Pruebas de humo | Aprobadas |
| Servicios después de restaurar | 8 saludables |
| Tiempo de recuperación medido | 72 s |
| Datos perdidos en el simulacro | 0, porque la copia se tomó justo antes |

## Otras comprobaciones

| Prueba | Resultado |
|---|---|
| Ciclo y retención, con intervalo 0 y límite 3 | Quedan 3 copias y sus 3 huellas; las antiguas se retiran |
| Base caída durante el ciclo | Registra el fallo, sigue en marcha y no deja archivos temporales |
| Archivo cortado | `pg_restore` lo rechaza |
| Clave incorrecta | El descifrado falla |
| Ansible en Ubuntu desechable, commit `3fe9d40` | 8 servicios saludables, incluido `backup` |

## Qué encontró el simulacro

- `.gitignore` ignoraba `deploy/backup/` por la regla `Backup*/`: Jenkins no habría recibido el script. Se movió a `deploy/respaldos/`.
- La primera comprobación solo leía el índice de la copia. Ahora la lee completa y detecta un `pg_dump` interrumpido.
- La primera comparación dio una fila de más en `audit_entries`: el script comparaba después del inicio de sesión de las pruebas de humo. Se corrigió el orden y se repitió el simulacro completo.

## Límites

- **Pérdida máxima esperada: 6 horas.** Es el intervalo entre copias; no hay recuperación a un punto exacto en el tiempo.
- **Las copias siguen en el mismo disco.** Sobreviven a `qa.sh destroy`, pero no a una falla del disco. Sacarlas a otro lugar es manual, con `qa.sh export`. Un destino automático externo requiere un servicio de pago o una decisión del equipo.
- **La clave debe guardarse aparte.** Si se pierde `BACKUP_PASSPHRASE`, las copias no sirven.
- **El tiempo de 72 s es de este equipo y de una base pequeña** (1,7 MB cifrada). En un servidor real debe medirse otra vez.
- **El ciclo de 6 horas no se observó en tiempo real.** Se probó con intervalo 0.
- **Solo se respalda PostgreSQL.** Redis y RabbitMQ no. Las sesiones se cierran al restaurar y los eventos pendientes del outbox sí están en la copia.
- **No hay alertas.** Un respaldo fallido queda en las bitácoras y en el estado del contenedor; nadie recibe un aviso.
- **No se probó en Azure.**

## Entrega 8: contenedores e infraestructura reproducible (preparada y verificada en local; sin despliegue en Azure)

Contra `main` (incluye las entregas 1-7). **No se ejecutó `terraform apply`, no se creó ningún recurso de pago ni se publicó ningún servicio.** El despliegue real espera la aprobación del plan y costo (más abajo).

### Qué incluye
**Contenedores**
- `docker/{api,web,worker,migrator}.Dockerfile` (multi-etapa, usuario sin privilegios, contexto = raíz) y `.dockerignore`.
- Proyecto nuevo `src/MediCore.Worker` (proceso de mensajería sin HTTP: publicador del outbox + consumidor, mismo código de Infrastructure). En QA la API corre con `Messaging__Enabled=false`.
- `docker-compose.qa.yml` se **superpone al `docker-compose.yml` existente** (dependencias con su salud y volúmenes): quita los puertos publicados, red `backend` interna, migración por contenedor (`efbundle`), salud en api/web/worker/proxy, proxy Caddy (`deploy/caddy`), claves de la cookie de la web en volumen. Orden: dependencias saludables → `migrate` → api/worker → web → proxy. Todo por variables de entorno (`.env.qa.example`; `.env.qa` no se versiona).
- `scripts/qa.sh` (`init|up|seed|status|smoke|restart|down|destroy`), `qa-smoke.sh`, `qa-persistence.sh`. `down` conserva los volúmenes; `destroy` pide confirmación y solo toca `medicore-qa_*`.
- La app Android es un APK: no es un servicio del Compose.

**Infraestructura (solo archivos)**
- `infrastructure/terraform/bootstrap`: estado remoto en Blob Storage (privado, TLS 1.2, **sin claves de acceso**, solo Entra ID, versionado + retención 30 días, candado). `infrastructure/terraform/azure`: grupo, red, NSG (22 solo desde tu IP, 80, 443), IP estática con DNS, VM Ubuntu 24.04 `Standard_B2s` por clave SSH, disco de datos, apagado diario. `.terraform.lock.hcl` versionado.
- `infrastructure/ansible`: roles `base` (SSH endurecido, UFW, fail2ban, actualizaciones), `docker`, `medicore` (mismos Compose; `.env.qa` desde Ansible Vault). **Terraform no configura el SO y Ansible no crea recursos de Azure.**
- PostgreSQL, Redis y RabbitMQ **no se exponen** (sin regla en el NSG, UFW, ni puertos publicados en el Compose).
- Documentación: `documentacion/06-infraestructura.md` (arquitectura, **costos por recurso**, crear/modificar/destruir, qué falta para Azure).

### Plan y costo estimado para tu aprobación (USD/mes, `eastus`, aproximado; verifica en la calculadora de Azure)
| Recurso | 24/7 | ~8 h/día |
|---|---|---|
| VM `Standard_B2s` (2 vCPU, 4 GiB) | ≈ 30 | ≈ 7 |
| Disco del sistema StandardSSD 64 GiB | ≈ 5 | ≈ 5 |
| Disco de datos StandardSSD 32 GiB | ≈ 2,4 | ≈ 2,4 |
| IP pública Standard estática | ≈ 3,7 | ≈ 3,7 |
| Cuenta de almacenamiento del estado (LRS) | < 0,1 | < 0,1 |
| Red, NSG, DNS, HTTPS (Let's Encrypt), software | 0 | 0 |
| **Total** | **≈ 41** | **≈ 18** |

### Verificación local (ejecutada; detalle en `documentacion/evidencias/entrega-8-verificacion.md`)
- 4 imágenes construidas; ambiente levantado **desde cero** y todos los servicios `healthy`; también desde un **clon limpio** de la rama (`init`, `compose config` y las 4 construcciones).
- Humo web + API a través del proxy: 11/11. Cypress de Recepción **8/8** contra el ambiente en contenedores (inicio de sesión real con cookie).
- Persistencia (`qa-persistence.sh`) **13/13**: paciente, resultado y notificación asíncrona (worker + RabbitMQ) sobreviven a `restart` y a `down` + `up` sin borrar volúmenes. Volúmenes de desarrollo (`medicore_*`) intactos.
- App Android (APK contra `http://10.0.2.2:8080/`) conectada al ambiente en contenedores: **Maestro 8/8** en la segunda ejecución (la primera tuvo 6/8 por un fallo del driver de Maestro en el emulador, no de la app; ver evidencia).
- xUnit tras los cambios de código: **293/293** (44 + 50 + 199). No hay pruebas xUnit nuevas para el worker ni para `--seed-qa` (verificados con el ambiente).
- **Validación de archivos** (`scripts/validate-iac.sh`, contenedores oficiales): Terraform 1.12 `fmt`/`init -backend=false`/`validate` (azure y bootstrap), Ansible `--syntax-check` y `ansible-lint` perfil *production* (0 fallos), `hadolint`, `docker compose config`. **Es validación de archivos, no un despliegue en Azure.**

### Defectos encontrados y corregidos al ejecutar
Permisos del volumen de claves de la web (quedaba `unhealthy`); el sembrado fallaba en una base nueva porque los datos E2E se creaban antes que los de demostración (no se veía en desarrollo); cabeceras reenviadas para que el límite de intentos de login vea la IP real detrás del proxy; 2 faltas de `ansible-lint`.

### No ejecutado / pendiente
- `terraform plan/apply`, `ansible-playbook` contra un servidor, HTTPS con certificado real y pruebas contra Azure. **Falta**: tu aprobación, suscripción con cuota de serie B, Azure CLI/Terraform/Ansible en la máquina de administración, clave SSH, Vault, acceso de la VM al repositorio y un APK con `https://<fqdn>/`.
- Siguen **pendientes** (no marcados como hechos): Jenkins (Jenkinsfile de 18 etapas), SonarQube, Dependency-Check/npm audit/Fortify, OWASP ZAP, Insomnia, k6/JMeter, ampliar Cypress y los entregables de QA.
- Mejoras anotadas: registro de imágenes (GHCR/ACR), copias de seguridad de PostgreSQL y monitoreo, imágenes por *digest* + escaneo, clave de depuración estable del APK.

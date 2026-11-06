# Terraform y Ansible ejecutados sin Azure

Ejecutado el 2026-11-06 con `scripts/iac-simulacion.sh`. El equipo decidió no usar recursos de pago, así que **esto no es un despliegue en Azure**: no hubo suscripción, IP pública, certificado ni costo.

## Terraform

`terraform test` ejecuta el código de `infrastructure/terraform/azure` con el proveedor de Azure simulado. Resultado: 3 de 3 etapas aprobadas (`reports/iac/terraform-test.txt`).

| Etapa | Qué se comprobó |
|---|---|
| Aprovisionar | Crea todos los recursos. VM `Standard_B2s`, sin contraseñas, con disco de datos y apagado automático. Solo abre 22, 80 y 443, y SSH solo a los rangos de administración |
| Modificar | Cambia la VM a `Standard_B2ms`, el disco a 64 GB y retira el apagado automático |
| Rechazar SSH abierto | Con `0.0.0.0/0`, Terraform se niega a continuar |
| Destruir | Al terminar, Terraform destruye todo lo creado en la simulación |

**Qué no prueba:** cuotas, precios, disponibilidad de la región ni que Azure acepte cada valor.

## Ansible

`infrastructure/ansible/site.yml` se aplicó sobre un Ubuntu 24.04 desechable (contenedor con systemd y Docker propio). El detalle está en `reports/iac/ansible.txt`.

| Pasada | Tareas correctas | Con cambios | Fallidas |
|---|---|---|---|
| Primera | 22 | 17 | 0 |
| Segunda | 20 | 2 | 0 |

- **Paquetes y sistema:** se aplicaron los paquetes base, SSH solo con clave, actualizaciones automáticas, fail2ban y cortafuegos.
- **Docker:** se instaló desde el repositorio oficial.
- **Aplicación:** subió con los 7 servicios saludables, desde el commit `fc9b01d`. `GET /api/health` y `GET /Login` respondieron 200.
- **Cortafuegos:** quedó activo y solo permite 22, 80 y 443.
- **Segunda pasada:** los 2 cambios son la siembra de datos, que siempre se marca como cambio, y la tarea que levanta Compose. Esa tarea no reinició ni dañó la aplicación, pero tampoco es idempotente de forma estricta.

**Diferencias con Azure:** conexión local en vez de SSH, sin disco de datos, repositorio local y secretos aleatorios de un solo uso. El servidor se elimina al terminar.

**Defecto encontrado:** el rol de Docker necesitaba `python3-debian` y nadie lo instalaba (H9-02). Sin esta ejecución habría fallado el primer día en una VM mínima.

## Cómo repetirlo

```bash
bash scripts/iac-simulacion.sh all
```

# MediCore · Registro de defectos

Defectos reales encontrados durante el proyecto. La causa detallada de cada uno, y los errores de pruebas o de entorno, están en [04-registro-de-hallazgos.md](04-registro-de-hallazgos.md).

La severidad y la prioridad siguen la sección 17 del [plan de pruebas](10-plan-de-pruebas.md). Se asignaron al consolidar este registro, a partir del efecto observado.

## Resumen

| Severidad | Cerrados | Abiertos |
|---|---|---|
| Crítica | 0 | 0 |
| Alta | 6 | 1 |
| Media | 12 | 0 |
| Baja | 3 | 3 |

## Defectos cerrados

| ID | Módulo | Severidad | Prioridad | Pasos | Esperado | Obtenido | Corrección | Evidencia |
|---|---|---|---|---|---|---|---|---|
| H4-01 | Agenda | Alta | P1 | Enviar 12 reservas simultáneas al mismo horario | Una 201 y las demás 409 | 11 respuestas 500 por deadlock | `pg_advisory_xact_lock` y reintento ante 40P01 | `AppointmentsApiTests` |
| H4-03 | Web | Alta | P1 | Abrir Agenda o Mi agenda | La página carga | Error 500 | `_Shell` reenvía la sección de scripts | Cypress `recepcion-agenda`, `medico-agenda` |
| H4-04 | Web | Alta | P1 | Cambiar fecha en Reservar y pulsar Confirmar | Se crea la cita | La página queda igual | `action` explícito | Cypress `recepcion-reserva` |
| H4-05 | Web | Alta | P1 | Confirmar la reserva después de H4-04 | Se crea la cita | Página vacía con 400 | Token antifalsificación explícito | Cypress `recepcion-reserva` |
| H4-12 | Web | Media | P2 | Escribir una fecha en el filtro de agenda | La agenda cambia de fecha | Conservaba la fecha anterior | Envío automático al completar la fecha | Cypress `recepcion-agenda` |
| H5-04 | Web | Baja | P2 | Abrir el expediente de un paciente sin relación | Mensaje con 404 | Página en blanco | Página `/Estado` | Cypress `medico-expediente` |
| H6-02 | Mensajería | Alta | P1 | Detener y reiniciar RabbitMQ, registrar un resultado | Se crea la notificación | El consumidor dejaba de recibir | La conexión se recupera sin reemplazarla | `MessagingTests` |
| H7-08 | Móvil | Media | P1 | Instalar el APK y abrir la app | La app abre | Se cierra al abrir | Ensamblados dentro del APK | `documentacion/evidencias/entrega-7-maestro.md` |
| H7-09 | Móvil | Media | P1 | Abrir la app tras H7-08 | La app abre | Error de bibliotecas nativas | `extractNativeLibs` | `documentacion/evidencias/entrega-7-maestro.md` |
| HW-03 | Web | Media | P2 | Entrar como Administrador en 360 px | Sin desplazamiento horizontal | La página medía 427 px | Grid con `minmax(0,1fr)` | Cypress `pantalla-pequena` |
| H8-01 | Contenedores | Media | P1 | Levantar QA en volúmenes nuevos | Web saludable | `unhealthy` al escribir en `/keys` | Dueño correcto en el Dockerfile | `documentacion/evidencias/entrega-8-verificacion.md` |
| H8-02 | Datos QA | Media | P1 | Ejecutar `--seed-qa` en una base nueva | Siembra completa | Fallaba sin la Dra. Méndez | Orden del sembrado corregido | `documentacion/evidencias/entrega-8-verificacion.md` |
| HP-02 | Mensajería | Alta | P1 | Ejecutar el perfil Spike de k6 y contar pendientes | Eventos procesados | 14 405 eventos pendientes por un tope de 20 cada 2 s | Lote de 200 y drenado continuo | `reports/k6/corrida.txt` |
| HS-01 | Código | Media | P2 | Analizar con SonarQube | 0 bugs | S1751 en `MediCoreApi.cs` y `MobileApiClient.cs` | Bucles corregidos | `reports/sonarqube/medidas.json` |
| HA-01 | Accesibilidad | Media | P2 | Ejecutar axe en el calendario | Contraste AA | Contraste insuficiente | Colores en `app.css` | `reports/accesibilidad/axe.json` |
| HZ-01 | API | Media | P2 | Buscar o guardar texto con un carácter nulo (`%00`) | Respuesta 400 | Error 500 de PostgreSQL | `InvalidTextExceptionHandler` | `PatientsApiTests`, `reports/zap/zap-api.html` |
| HC-01 | Código | Baja | P3 | Ejecutar `dotnet format --verify-no-changes` en Jenkins | Sin cambios | 7 archivos con formato incorrecto | Formato aplicado | Etapa 3 de Jenkins |
| HS-02 | Código | Media | P2 | Ejecutar SonarQube sobre código nuevo | Duplicación de 3 % o menos | 9,3 % de duplicación y 49 % de cobertura | Lectura de errores compartida y 4 pruebas | Corrida 2 de Jenkins, `reports/sonarqube/quality-gate.json` |
| H9-01 | Contenedores | Media | P1 | Crear QA desde Jenkins y reiniciar Docker | QA vuelve a arrancar | API, web y proxy terminan con código 127: montaban archivos del workspace de Jenkins | Script de salud y Caddyfile dentro de las imágenes | `docker/proxy.Dockerfile`, `scripts/qa.sh smoke` |
| H9-02 | Infraestructura | Media | P2 | Aplicar `site.yml` en un Ubuntu mínimo | Docker instalado | Falla: faltaba `python3-debian` | Paquete agregado al rol `base` | `documentacion/evidencias/iac-sin-costo.md` |
| HZ-02 | Web | Baja | P3 | Ejecutar ZAP | Cabeceras presentes | Faltaban `Permissions-Policy` y `Cross-Origin-Resource-Policy` | Agregadas en Caddy | `reports/zap/zap.html` |

## Defectos abiertos

| ID | Módulo | Severidad | Prioridad | Pasos | Esperado | Obtenido | Decisión | Evidencia |
|---|---|---|---|---|---|---|---|---|
| H8-08 | Datos | Alta | P1 antes de producción | Revisar la infraestructura | Copias automáticas | No hay respaldos | Abierto, bloquea producción | R-16 |
| HQ-01 | Datos QA | Baja | P2 | Revisar `.env.qa` | Contraseña sembrada fuerte | Contraseña de demostración de 5 letras | Ficticia y solo local. Regenerar con `qa.sh init` antes de exponer QA | `.env.qa` (no versionado) |
| HZ-03 | Web | Baja | P3 | Ejecutar ZAP | Cabeceras COEP y COOP | No están | Aceptado en QA; revisar con HTTPS | `reports/zap/zap.html` |
| HJ-04 | Pipeline | Baja | P3 | Reiniciar Docker Desktop | Jenkins conserva su historial | Se pierde `/var/jenkins_home` y la numeración vuelve a 1 | Aceptado: las consolas se guardan en `reports/jenkins/` | `reports/jenkins/corridas.txt` |

## Falsos positivos

| Herramienta | Alerta | Justificación |
|---|---|---|
| ZAP | HTTP Only Site | QA local por HTTP. En Azure se usa HTTPS |
| ZAP | Unexpected Content-Type | Ocurre en rutas que no son de la API |
| Dependency-Check | 4 críticas en bibliotecas cliente | Son CVE de servidores PostgreSQL y Redis. Ver [15-seguridad-y-accesibilidad.md](15-seguridad-y-accesibilidad.md) |
| ZAP | Private IP Disclosure | Es la IP interna registrada en la auditoría |

# MediCore · Casos de prueba y evidencias

Selección de casos de los temas exigidos por el documento: roles, citas, laboratorio, APIs, Redis, RabbitMQ, seguridad y móvil. Cada caso apunta a una prueba automatizada que se puede repetir. La lista completa son las propias suites.

**Prioridad:** A crítica, M media. El resultado global de cada suite está en [13-informe-final.md](13-informe-final.md).

## Citas y concurrencia

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-01 | A | Reservas simultáneas al mismo horario | Una sola cita activa, las demás 409 | `Solicitudes_simultaneas_al_mismo_horario_producen_una_unica_reserva_activa` | Aprobado |
| CP-02 | A | Alta demanda en varios horarios | Nunca más de una cita por horario | `Alta_demanda_en_varios_horarios_nunca_supera_una_cita_por_horario` | Aprobado |
| CP-03 | A | Mismo pedido reenviado con la misma clave | Devuelve la cita original | `Reenviar_la_misma_solicitud_con_la_misma_clave_devuelve_la_cita_original_sin_duplicar` | Aprobado |
| CP-04 | A | Reenvíos simultáneos con la misma clave | Se crea una sola cita | `Reenvios_simultaneos_con_la_misma_clave_crean_una_sola_cita` | Aprobado |
| CP-05 | M | Misma clave con otra solicitud | 409 | `Reutilizar_una_clave_con_otra_solicitud_devuelve_409` | Aprobado |
| CP-06 | A | Cancelar libera el horario | Otro paciente puede reservar | `Cancelar_libera_el_horario_para_otro_paciente_y_es_idempotente` | Aprobado |
| CP-07 | M | Cancelaciones simultáneas | Sin errores ni dobles cambios | `Cancelaciones_simultaneas_de_la_misma_cita_son_seguras` | Aprobado |
| CP-08 | A | Reservas con 200 usuarios mientras se consulta | 0 solapes y 0 % de error inesperado | k6 `spike`, `reports/k6/integridad.txt` | Aprobado |
| CP-09 | A | Dos reservas al mismo horario, una tras otra, en la web | La segunda se rechaza | Cypress, `Prevención de doble reserva` | Aprobado |

## Redis

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-10 | M | Primer acceso y segundo acceso | MISS con TTL, luego HIT | `Cache_MISS_puebla_Redis_con_TTL_y_el_siguiente_acceso_es_HIT` | Aprobado |
| CP-11 | A | Reservar o cancelar | La disponibilidad cacheada se invalida | `Reservar_y_cancelar_invalidan_la_disponibilidad_cacheada` | Aprobado |
| CP-12 | M | Cambio de horario del médico | Se invalidan todas sus fechas | `Cambiar_el_horario_de_atencion_invalida_todas_las_fechas_cacheadas_del_medico` | Aprobado |
| CP-13 | M | Dato vencido | Se recarga desde PostgreSQL | `Dato_vencido_el_cache_expira_por_TTL_y_se_recarga_desde_la_base_de_datos` | Aprobado |
| CP-14 | A | Redis caído en disponibilidad y reserva | Siguen funcionando | `Si_Redis_no_esta_disponible_la_disponibilidad_y_la_reserva_siguen_funcionando` | Aprobado |
| CP-15 | A | Redis caído en autenticación | Login 503 y token rechazado | `Con_redis_caido_el_login_devuelve_503_en_lugar_de_un_500`, `Con_redis_caido_un_token_valido_deja_de_aceptarse` | Aprobado |

## RabbitMQ y laboratorio

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-16 | A | Broker detenido al registrar un resultado | Se guarda y el evento se publica al volver | `Con_el_broker_detenido_el_resultado_se_guarda_y_el_evento_se_publica_al_volver_el_broker` | Aprobado |
| CP-17 | A | Broker detenido al reservar | Se guarda y la notificación llega al volver | `Con_el_broker_detenido_la_cita_se_guarda_y_la_notificacion_llega_al_volver_el_broker` | Aprobado |
| CP-18 | A | Mensaje repetido | No duplica la notificación | `Un_mensaje_reentregado_no_duplica_la_notificacion`, `Un_evento_repetido_no_duplica_la_notificacion` | Aprobado |
| CP-19 | M | El consumidor falla dos veces | Se reintenta y termina procesado | `Si_el_consumidor_falla_dos_veces_el_mensaje_se_reintenta_y_termina_procesado` | Aprobado |
| CP-20 | A | Registrar, consultar y notificar | Laboratorio y el médico lo ven, con su estado de notificación | Cypress, `Laboratorio · registrar, consultar y notificar un resultado` | Aprobado |
| CP-21 | M | Reintento de notificación | No duplica | `Reintentar_si_la_notificacion_ya_existe_no_hace_nada` | Aprobado |

## Roles, recursos y sesión

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-22 | A | Cada rol entra solo a lo suyo | Sin permiso, respuesta 403 o redirección | Cypress, `Permisos por rol (web y API)` | Aprobado |
| CP-23 | A | Paciente pide datos ajenos (IDOR) | 404 y auditoría | Insomnia, carpetas 3, 5 y 6 | Aprobado |
| CP-24 | A | Registros clínicos no se editan | PUT, PATCH y DELETE devuelven 405 | Insomnia, `ClinicalApiTests` | Aprobado |
| CP-25 | A | Bloqueo tras 5 intentos | Cuenta bloqueada aun con la clave correcta | `Cuenta_se_bloquea_tras_cinco_intentos_fallidos_incluso_con_la_contrasena_correcta` | Aprobado |
| CP-26 | A | Reutilizar un refresh rotado | Se revoca la sesión | `Reutilizar_un_refresh_token_ya_rotado_revoca_toda_la_sesion` | Aprobado |
| CP-27 | M | Token vencido | 401 | `Token_vencido_devuelve_401` | Aprobado |
| CP-28 | A | Datos clínicos en auditoría y bitácoras | No aparecen | `Ni_la_auditoria_ni_las_bitacoras_contienen_el_contenido_clinico` | Aprobado |
| CP-29 | M | Carácter nulo en búsqueda y datos | 400, no 500 | `Un_caracter_nulo_en_busqueda_o_en_datos_devuelve_400_y_no_500` | Aprobado |

## App móvil

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-30 | A | Login válido e inválido | Entra o muestra el error | Maestro 01 y 02 | 8/8 antes de los eventos, repetir |
| CP-31 | A | Reservar, consultar y cancelar | La cita aparece y se cancela | Maestro 03 y 04 | 8/8 antes de los eventos, repetir |
| CP-32 | A | Resultados, recetas y notificaciones | Se abre el detalle | Maestro 05, 06 y 07 | 8/8 antes de los eventos, repetir |

## Accesibilidad

| ID | Prioridad | Caso | Resultado esperado | Prueba | Estado |
|---|---|---|---|---|---|
| CP-33 | M | WCAG A y AA en 14 pantallas | 0 violaciones | `accesibilidad-axe.cy.js`, `reports/accesibilidad/axe.json` | Aprobado |
| CP-34 | M | Pantalla de 375 px | Sin desplazamiento horizontal | `pantalla-pequena.cy.js` | Aprobado |

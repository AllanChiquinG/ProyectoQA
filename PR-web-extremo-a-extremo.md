## Web del personal de extremo a extremo (revisada y probada)

Contra `main` (incluye las entregas 1 a 7). **No incluye la entrega 8** (contenedores e infraestructura): sigue en su propia rama, `feature/entrega-8-contenedores-infraestructura`, sin fusionar. No se crearon recursos cloud ni se tocó la app móvil.

### Funciones revisadas por rol
- **Acceso:** inicio y cierre de sesión, mensajes de error en español, sesión vencida o revocada, cambio de rol con efecto inmediato, permisos por rol y por recurso, navegación por teclado.
- **Recepción:** pacientes (lista, búsqueda, paginación, alta con validaciones, edición), agenda (filtro de médico, fecha, calendario, días sin atención o pasados), reserva con validaciones, detalle y cancelación con confirmación, prevención de doble reserva.
- **Administrador:** médicos (lista, búsqueda, alta, edición, validaciones), disponibilidad (y su efecto real en la reserva), usuarios y roles (cuenta propia bloqueada, cambio de rol con cierre de sesiones).
- **Médico:** Mi agenda, pacientes autorizados, expediente compartido entre colegas, consultas, recetas con varios medicamentos (máximo 10) y sus validaciones.
- **Laboratorio:** listado, búsqueda, paginación, registro con validaciones (hasta 10 parámetros, fecha futura rechazada), detalle, "Reintentar notificación", consulta por médicos autorizados.
- Transversal: pantalla de 375 px, etiquetas de formularios, listas vacías y mensajes cuando la API no responde.

### Defectos corregidos
1. El inicio decía que las pantallas llegarían en "próximas entregas" → accesos directos por rol.
2. El `max` de la fecha de nacimiento tenía formato inválido (no limitaba fechas futuras) → corregido.
3. Con el menú del Administrador la página se ensanchaba en pantallas de 360 px → la columna del diseño ya no crece (`minmax(0,1fr)`), el menú se desplaza en su franja.
4. Las tablas anchas (médicos, usuarios, pacientes, resultados) podían ensanchar la página → se desplazan dentro de su panel.

Ajustes de adaptación solo en `app.css` (tercer archivo); `style.css` y `appearance.css` no se modificaron.

### Resultados de las pruebas (ejecutadas)
- **Cypress, Chrome 155: 102/102** (10 specs; datos aislados `CYP-E2E-*`, limpieza al empezar y al terminar).
- **xUnit: 293/293** (44 unitarias + 50 móvil + 199 integración), en contenedor Linux.
- Rastreo por rol de las 18 pantallas (solo GET): todas responden, permisos correctos.
- Caída de la API comprobada a mano (`scripts/web_conexion.py`): las pantallas responden con un mensaje en español, sin trazas.

### Limitaciones pendientes
- No se ejecutó Cypress en Electron tras la ampliación ni en otros navegadores; el celular se probó con viewport de 375 px, no en dispositivo.
- Sin auditoría automática de accesibilidad (axe/Lighthouse) ni lector de pantalla.
- La concurrencia real de doble reserva la cubren las pruebas de API; la prueba Cypress reserva dos veces seguidas.
- Siguen pendientes (no marcados como hechos): Insomnia, k6/JMeter, Jenkins/DevSecOps, Azure y la fusión de la entrega 8.

### Probarlo
Web en `http://127.0.0.1:5298`. Cuentas de demostración: archivo local `CUENTAS-DEMO.local.txt` (ignorado por Git). Registro de hallazgos: `documentacion/04-registro-de-hallazgos.md`.

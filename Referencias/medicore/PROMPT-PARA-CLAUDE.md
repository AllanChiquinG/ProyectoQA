Quiero desarrollar MediCore, el proyecto 2 de mi documento académico, usando .NET. Esta carpeta contiene el diseño aprobado como referencia.

Primero lee estos archivos del repositorio:
- referencias/medicore/diseno/index.html
- referencias/medicore/diseno/style.css
- referencias/medicore/diseno/appearance.css
- referencias/medicore/diseno/app.js
- referencias/medicore/LEEME.md

Si están en otra ruta, localiza la carpeta de referencia antes de continuar. Revisa también la captura referencias/medicore/capturas/vista-movil.png si puedes visualizar imágenes. No afirmes haber visto imágenes o probado pantallas si no puedes hacerlo.

El diseño aprobado es el resultado de style.css MÁS appearance.css, cargados en ese orden. Respeta sus colores, tipografía, espacios, tamaños, tarjetas, menú, formularios y jerarquía. Las vistas están generadas con JavaScript en app.js: inventaría sus identificadores y flujos antes de implementar. No esperes encontrar un HTML por pantalla.

Mantén las pantallas en español. La web corresponde al personal y la aplicación móvil al paciente. Para la implementación propuesta usa ASP.NET Core en web/API y .NET MAUI para la aplicación móvil, con PostgreSQL, Redis y RabbitMQ según los requisitos del proyecto académico. Revisa el documento del proyecto para validar el alcance y sus entregables antes de implementar.

No agregues módulos nuevos, no rediseñes la interfaz ni añadas el portal web del paciente. Puedes corregir detalles de accesibilidad o adaptación a diferentes tamaños sin alterar la identidad visual; explica los cambios necesarios.

No copies al producto final la barra del prototipo que permite cambiar Web/Móvil y seleccionar un rol. El usuario debe iniciar sesión y recibir únicamente los permisos y navegación que le correspondan. Los datos ficticios, el acceso simulado y las reglas en memoria sirven solo para ilustrar la maqueta, no como arquitectura de producción.

Antes de programar, presenta un inventario breve de las pantallas y un plan de implementación por flujos. Después avanza con un flujo pequeño por vez, comenzando por inicio de sesión y agenda de recepción. Reutiliza componentes y verifica el resultado contra esta referencia. Conserva los archivos de referencia sin modificarlos.

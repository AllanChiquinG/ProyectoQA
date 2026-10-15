# Diseño de MediCore

## Abrir el prototipo

Abre `diseno/index.html` con un navegador. Puedes cambiar entre Web y Móvil, y entre roles, desde la barra superior. Los cambios y registros de ejemplo se reinician al recargar. Las fuentes se descargan de Google Fonts si hay conexión; sin Internet se usan fuentes alternativas.

## Usarlo con Claude Code

1. Guarda esta carpeta dentro del repositorio del sistema, por ejemplo en `referencias/medicore/`.
2. Inicia Claude Code desde el repositorio.
3. Pega el contenido de `PROMPT-PARA-CLAUDE.md`, ajustando la ruta si cambiaste la ubicación.
4. Trabaja un flujo a la vez y compara el resultado con el prototipo.

## Archivos de referencia

- `diseno/index.html`: entrada del prototipo.
- `diseno/style.css`: distribución base de web y móvil.
- `diseno/appearance.css`: colores y apariencia visual final; sus reglas sobrescriben las de style.css.
- `diseno/app.js`: pantallas, textos, datos ficticios e interacciones demostrativas.
- `capturas/vista-movil.png`: captura de la vista móvil.

Las pantallas tienen identificadores WEB y MOB. No hay un archivo HTML por pantalla: JavaScript genera las vistas dentro de index.html.

## Alcance del diseño

Web del personal: recepción, médicos, laboratorio y administración. Aplicación móvil: paciente. No incluye el portal web del paciente que se canceló durante la revisión.

La barra superior para elegir vista y rol sirve exclusivamente para revisar los mockups. No debe formar parte del sistema final: el rol se determina mediante autenticación y autorización reales. Mover esa barra a Opciones del prototipo se conversó como propuesta, pero esta copia conserva la versión aprobada y publicada.

Este material define una referencia visual, no una aplicación clínica terminada. No implementa seguridad real, backend, persistencia, concurrencia en base de datos, infraestructura ni herramientas QA. Los datos clínicos y las identidades son ficticios. La planificación técnica y la evaluación deben seguir el documento del proyecto 2 MediCore.

## Conservar una copia

Guarda el ZIP original y una copia descomprimida. Puedes versionar esta carpeta en Git para mantener la referencia que Claude debe seguir.

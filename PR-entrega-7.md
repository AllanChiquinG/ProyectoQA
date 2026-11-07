## Entrega 7: app móvil del paciente (.NET MAUI, Android)

Contra `main` (que ya incluye la entrega 6). 2 commits, solo cambios de esta entrega: app y lógica móvil, pruebas, flujos Maestro, scripts y documentación. No incluye cloud.

### Pantallas implementadas (todas en español, diseño y ids MOB aprobados)
| ID | Pantalla |
|---|---|
| MOB-01 | Inicio de sesión (validación local, mensaje único ante credenciales incorrectas, aviso de sesión vencida) |
| MOB-02 | Inicio: próxima cita, accesos, notificaciones sin leer, cerrar sesión |
| MOB-03 / MOB-07 | Mis citas / detalle con cancelación (con confirmación) |
| MOB-04 / 05 / 06 | Reserva en 3 pasos: médico → fecha y horario → confirmar |
| MOB-08 / 09 | Resultados de laboratorio / detalle |
| MOB-10 / 11 | Recetas / detalle |
| MOB-12 | Notificaciones; abrir una lleva al resultado y la marca como leída |

Estados de interfaz: cargando, lista vacía, error de conexión con "Reintentar" y sesión vencida (vuelve a MOB-01). La app usa **solo la API real** (sin respuestas simuladas); la autorización la valida siempre la API. Reserva sin duplicados: el botón ignora pulsaciones repetidas y cada solicitud lleva una `Idempotency-Key` que se reutiliza en los reintentos; si el horario se ocupa antes de confirmar, se muestra el aviso, se conservan médico y fecha y se elige otro.

Seguridad: sesión en `SecureStorage` (Keystore; nunca la contraseña), renovación de tokens con candado (el refresco es de un solo uso), HTTP solo hacia `10.0.2.2`/`localhost`, `allowBackup=false`, sin secretos en el repositorio. El personal que inicia sesión en la app es rechazado y su token revocado.

### Resultados
**Maestro (8 flujos) — 8/8 aprobados en una sola ejecución en el emulador Android 35** (Pixel 6, x86_64, WHPX), contra la API y RabbitMQ reales en Docker: login válido, login inválido, reserva, consulta y cancelación, resultados, recetas, notificaciones, cierre de sesión. Evidencia: `documentacion/evidencias/entrega-7-maestro.md` (resumen) y `documentacion/evidencias/entrega-7-pruebas.md`; los registros y capturas de Maestro quedan en `%USERPROFILE%\.maestro\tests\` del equipo que los ejecutó.

**xUnit — qué se ejecutó y cuándo:**
- **Antes** de las correcciones de Android (commit `9f7d464`): suite completa en contenedor Linux, **293 superadas** (44 unitarias, 50 de la lógica móvil, 199 de integración con PostgreSQL, Redis y RabbitMQ reales; 17 de ellas del cliente móvil contra la API real).
- **Después** (`f269f0c`): **no se volvió a ejecutar xUnit.** Solo cambiaron `src/MediCore.Mobile` (MAUI), `scripts/maestro_run.py`, los flujos Maestro 01/02 y documentación; `MediCore.Mobile.Core`, la API y las pruebas xUnit no cambiaron. Los 293 NO se presentan como una ejecución posterior; lo ejecutado después es la compilación de la app y Maestro.

### Compilar, iniciar el emulador e instalar (detalle en `documentacion/05-app-movil.md`)
```bash
docker compose up -d
scripts/dev-stack-linux.sh up            # API real en Docker (127.0.0.1:5011 = 10.0.2.2 desde el emulador)
scripts/android-build-linux.sh           # compila el APK en un contenedor Linux (imagen y SDK en volúmenes nuevos)
```
```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
& "$sdk\cmdline-tools\latest\bin\avdmanager.bat" create avd -n medicore_pixel -k "system-images;android-35;google_apis;x86_64" -d pixel_6
Start-Process "$sdk\emulator\emulator.exe" -ArgumentList "-avd","medicore_pixel","-no-audio","-gpu","swiftshader_indirect"
& "$sdk\platform-tools\adb.exe" uninstall com.medicore.paciente
& "$sdk\platform-tools\adb.exe" install artifacts\com.medicore.paciente-Signed.apk
$env:MEDICORE_PASSWORD = "<Seed:DevPassword>"; $env:MAESTRO_CLI_NO_ANALYTICS = "1"
python scripts/maestro_run.py            # 8 flujos; escribe la evidencia solo si pudo ejecutarlos
```
Herramientas instaladas en el equipo desde fuentes oficiales: SDK de Android (command-line tools, platform-tools, emulator, imagen android-35) y Maestro 2.12.0. Smart App Control sigue activo.

### APK de desarrollo
`artifacts/com.medicore.paciente-Signed.apk` (≈84 MB, firmado con clave de depuración; arm64-v8a y x86_64). **No está en el repositorio** (`artifacts/` ignorado): se genera con `scripts/android-build-linux.sh`.

### Errores corregidos durante la prueba en Android
1. La app se cerraba al abrir: el APK Debug no incluía los ensamblados (Fast Deployment) → `EmbedAssembliesIntoApk=true`.
2. Bibliotecas nativas comprimidas (`lib/x86_64 MUST be STORED`) → `android:extractNativeLibs="true"`.
3. `App` creaba el Shell antes de cargar los recursos de estilo → se crea en `CreateWindow`.
4. `adb install -r` fallaba por cambio de clave de depuración y se seguía probando una versión vieja → desinstalar antes de instalar.
5. Flujos Maestro: 01 y 02 con selectores incorrectos (texto con ícono; campo ya escrito), subflujo sin cabecera de configuración, `maestro` es un `.bat` en Windows; el 07 falló una vez por `device offline` del emulador y pasó al repetirlo (no fue un defecto de la app).
También se corrigieron al compilar: `DatePicker.NewDate` es `DateTime?` (CS1503), `DisplayAlert` obsoleto y `RuntimeIdentifiers` ausentes.

### Limitaciones y pendientes
- **Solo se probó un emulador** (Android 35). No se probó en dispositivo físico, ni accesibilidad, rendimiento o rotación.
- La app MAUI no se compiló de forma nativa en Windows (Smart App Control activo); se compila en contenedor Linux.
- **Mejora pendiente:** conservar una clave de firma de depuración **estable entre compilaciones, fuera del repositorio**, para poder actualizar el APK con `adb install -r` sin desinstalarlo y perder la sesión y los datos locales (H7-13).
- Notificaciones: hoy solo existe `LabResultReady`; otros tipos del diseño ("Cita confirmada", "Nueva receta") llegarán con los eventos pendientes y la app los muestra sin enlace.
- Tipografías del sistema (no Manrope/DM Sans) e íconos de la barra inferior solo con texto; adaptaciones documentadas.
- Siguen pendientes: Insomnia, k6/JMeter, ampliar Cypress y la fase cloud (no incluida).

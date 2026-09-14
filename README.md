# RsyncZilla 🚀

Cliente SFTP para Windows con interfaz gráfica de doble panel (estilo FileZilla) y **transferencias verificadas mediante rsync portable (v3.3.0) sobre SSH**.

Diseñado para resolver el problema clásico de fallos silenciosos de transmisiones FTP, garantizando sincronizaciones atómicas, reanudables y que solo transfieren archivos nuevos y modificados.

---

## 🌟 Características Principales

1. **Navegación Fluida con SFTP (SSH.NET):**
   - Panel izquierdo: Explorador del sistema de archivos local de Windows.
   - Panel derecho: Explorador de archivos del servidor remoto vía SFTP.
   - **Selección múltiple:** Selecciona varios archivos a la vez con Shift / Ctrl o arrastrando el ratón en ambos paneles.
   - **Drag & Drop bidireccional:**
     - Arrastra elementos entre el panel Local y Remoto para subirlos o descargarlos de inmediato.
     - Si sueltas sobre una subcarpeta concreta, se transfiere directamente al interior de esa carpeta.
   - **Drag & Drop desde el Explorador de Windows:** Arrastra archivos o carpetas directamente desde Windows Explorer hacia el panel Remoto para subirlos con rsync.
   - Doble clic para entrar en carpetas o subir de nivel (`..`).
   - Creación y eliminación de carpetas y archivos locales y remotos (individual o múltiple).

2. **Transferencias con rsync Portable (v3.3.0):**
   - Utiliza una suite portable empaquetada de `rsync.exe` y `ssh.exe` (Cygwin64) de ~6 MB.
   - **Cero fallos silenciosos:** Si algo se interrumpe o falla, rsync devuelve un código de salida inequívoco (`ExitCode != 0`) y la app alerta de inmediato con el error detallado.
   - **Solo envía modificaciones:** Emplea los flags `-avzP --stats --update` para enviar exclusivamente archivos nuevos o deltas modificados.
   - **Escritura atómica:** rsync crea ficheros temporales ocultos antes de reemplazarlos, impidiendo que queden archivos corruptos o a medias.

3. **Autenticación y Gestor de Conexiones:**
   - **Gestor de Sitios (📂 Sitios):** Almacena tus servidores habituales (Host, Usuario y Puerto) para conectarte en un clic.
   - **Seguridad estricta:** **NUNCA guarda contraseñas** en disco.
   - Permite consultar y eliminar conexiones guardadas en cualquier momento.
   - Navega automáticamente al **directorio personal (`home`)** del usuario remoto al iniciar sesión.
   - Asistente integrado `RsyncAskPass.exe` y `SSH_ASKPASS` para alimentar las credenciales al subproceso de forma segura y transparente, sin ventanas emergentes molestas.

4. **Pestañas de Control de Transferencias y Logs:**
   - **🚀 Cola de Transferencias:** Muestra la tarea en curso (con progreso, velocidad y ETA) junto con todas las tareas en espera (`⏳ Pendiente`).
   - **⏹ Cancelar todo:** Detiene la transferencia activa de rsync de inmediato y retira todas las tareas pendientes de la cola.
   - **❌ Transferencias Fallidas:** Pestaña dedicada con el motivo exacto del fallo y código de salida. Permite **reintentar la seleccionada** o **reintentar todas las fallidas** con un solo clic.
   - **✅ Historial de Éxitos:** Registro detallado de transferencias finalizadas correctamente.
   - **📜 Registro de Servidor y rsync:** Consola en tiempo real de eventos SFTP y comandos rsync.

---

## 🏗️ Estructura del Repositorio

- `src/RsyncZilla/`: Aplicación principal WPF (.NET 8).
  - `tools/cygwin64/`: Binarios portables (`rsync.exe`, `ssh.exe`, DLLs).
  - `Models/`: Modelos de archivos, tareas de transferencia, conexión y logs.
  - `Services/`:
    - `LocalFileService.cs`: Exploración de disco local.
    - `SftpService.cs`: Conexión persistente SFTP con SSH.NET.
    - `RsyncService.cs`: Orquestador de subprocesos rsync, parsing de progreso y validación de ExitCode.
  - `ViewModels/`: Lógica MVVM desacoplada.
  - `Views/`: Interfaz XAML moderna, InputDialog personalizado y converters.
- `src/RsyncAskPass/`: Micro-helper para canalizar contraseñas a OpenSSH de manera desatendida.
- `tests/RsyncZilla.Tests/`: Suite de pruebas unitarias xUnit (conversión de rutas cygwin, validación de binarios, etc.).
- `dist/RsyncZilla/`: Ejecutable compilado listo para usar (`RsyncZilla.exe`).

---

## 🚀 Cómo Ejecutar y Compilar

### Opción 1: Ejecutar directamente
Hacer doble clic en:
```
dist\RsyncZilla\RsyncZilla.exe
```

### Opción 2: Compilar desde cero
Ejecutar el archivo `build.bat` o desde la terminal:
```bash
dotnet publish src/RsyncZilla/RsyncZilla.csproj -c Release -r win-x64 --self-contained false -o dist/RsyncZilla
```

### Opción 3: Ejecutar pruebas unitarias
```bash
dotnet test
```

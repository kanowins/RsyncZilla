# RsyncZilla Release Notes

## Version 1.3.0 (2026-09-17)

### ✨ New Features & Improvements
- **Integrated Flat-Tree Directory Transfer Queue**:
  - Folders in transfer queues (`Active`, `Completed`, and `Failed`) are now treated as expandable container tasks.
  - Expanding a folder inserts individual file items directly into the main table beneath the parent folder, sharing the exact same columns, headers, and main scrollbar.
  - Sub-files feature a clear visual indentation indicator (`↳`) and shaded background styling.
  - Folder expand/collapse is accessible via the chevron toggle button or by double-clicking the folder row.
- **Real-Time Sub-File Progress Tracking**:
  - Live progress display reflects individual sub-file completion, file size, transfer rate, and container percentage in real time.
  - Dynamic folder item counter (`📁 folder_name (X/Y)`).
  - Pre-enumeration of local directory hierarchies prior to transfer for accurate file count and sizing.
  - Dynamic discovery during remote folder downloads seamlessly adds discovered files to the container.
- **Dedicated Size Column in Completed Tab**:
  - Added a `Size` column to the Completed Transfers table for easy visibility into individual file and sub-file transfer sizes.

---

## Version 1.2.2 (2026-09-16)

### ✨ New Features & Improvements
- **Configurable Transfer Overwrite Action (FileZilla Style)**:
  - Added new **`Transfer`** menu with a configurable *Overwrite Action* selector.
  - Supports 4 sync modes:
    1. **Overwrite if size or date differ**: Standard `rsync -avzP` without `--update`. Updates the destination file whenever its size or modification timestamp differs from the source, resolving issues where files with newer destination timestamps were silently skipped.
    2. **Overwrite only if source is newer (`--update`)**: Preserves newer remote modifications when desired.
    3. **Overwrite always (`--ignore-times`)**: Forces rsync delta-block calculation on every selected file.
    4. **Compare by content checksum (`--checksum`)**: Validates differences by file contents instead of modification times.
- **Fixed Menu Drop Alignment on Touch / Tablet PCs**:
  - Ensures menu popups always open left-aligned on all computers, bypassing Windows Tablet PC handedness settings.
- **Persistent User Settings**:
  - Automatically remembers the selected overwrite action across sessions (`%AppData%\RsyncZilla\settings.json`).

---

## Version 1.2.0 (2026-09-15)

### ✨ New Features & Improvements
- **Official Windows Installer (`.exe`) via Inno Setup**:
  - Full Windows setup wizard with Start Menu and Desktop shortcuts, clean Program Files installation, and native uninstaller.
  - Releases now include both the portable `.zip` and the standalone installer `.exe`.
- **In-App One-Click Auto-Update**:
  - The update dialog now features a direct **"⚡ Actualizar Ahora"** button with a real-time download progress bar.
  - Automatically launches the installer, closes RsyncZilla, applies the update, and relaunches the application seamlessly.
- **Verified rsync Delta Uploads for Live Remote Editing**:
  - Live remote file editing uploads saved changes using the robust rsync engine with atomic file replacement.
  - Solves group-permission conflicts when saving shared remote files.
- **Smart Attribute Warning Handling**:
  - Non-fatal attribute errors (`failed to set times: Operation not permitted`) are treated as warnings instead of failing the transfer.

---

## Version 1.1.0 (2026-09-15)

### ✨ New Features & Improvements
- **Verified rsync Delta Uploads for Live Remote Editing**:
  - Live remote file editing now uploads saved changes using the robust rsync engine instead of direct SFTP.
  - Guarantees atomic file replacement and checksum validation on every save.
  - Resolves permission issues when editing files belonging to different users within shared group directories.
- **Smart Timestamp Warning Handling (`failed to set times`)**:
  - Non-fatal attribute errors (`rsync: failed to set times: Operation not permitted`) are now recognized as warnings.
  - Successfully transferred files are no longer sent to the Failed tab when only timestamp synchronization is restricted by the remote OS.
- **Enhanced Log Console**:
  - Added warning level styling (amber/orange) in the Server & rsync log console for clear diagnostic visibility.

---

## Version 1.0.1 (2026-09-14)

### ✨ New Features & Improvements
- **Standard Windows Multi-Selection (Ctrl + Shift)**:
  - `Ctrl + Click`: Toggles individual file selection without unselecting previously chosen items (full Windows Explorer parity).
  - `Shift + Click`: Range selection between the anchor item and the clicked file.
- **Top Application Menu**:
  - `File`: Disconnect, Reconnect, New Tab / Connection, Site Manager, Exit.
  - `Actions`: Upload / Download Selected, New Directory, Refresh All (`F5`), KiTTY Terminal.
  - `View`: Clear completed, clear failed, retry all, clear logs.
  - `Help`: Check for updates, About RsyncZilla, GitHub repository link, Developed by Sumalab link.
- **Auto Update Detection**:
  - Dynamic notification banner in the top menu whenever a newer version is detected.
- **About Dialog**:
  - Detailed build and system environment info with one-click clipboard copying.

---

## Version 1.0.0 (2026-09-14)

Initial public release of **RsyncZilla**, the modern Windows SFTP client with delta-transfer synchronization powered by `rsync`.

### ✨ Key Features
- **High-Performance rsync Delta Sync**: Only transfers modified bytes instead of re-uploading whole files, delivering up to 10x-50x faster updates.
- **Dual-Panel File Explorer**: Intuitive local and remote browsing with file icons, permissions, sorting, and fast path navigation.
- **Live Remote Editing**: Edit remote text and code files in your favorite default text editor with automatic re-synchronization on save.
- **Integrated KiTTY / PuTTY Terminal**: One-click remote terminal launcher with automatic login and directory synchronization.
- **Multi-Tab Session Manager**: Connect to multiple remote servers concurrently in separate tabs with independent paths and states.
- **Site Manager**: Save connection presets for instant access (credentials are kept securely in memory per session).
- **Universal Drag & Drop**:
  - Drag files from **Visual Studio Code** directly into the remote panel.
  - Drag remote files and folders into **Windows Explorer** and Desktop via on-demand OLE streaming (`VirtualFileDataObject`).
  - Drag between local and remote panels to trigger synchronized batch transfers.
- **Rubber-band Marquee Selection**: Fluent multi-item marquee selection by dragging across empty list areas.

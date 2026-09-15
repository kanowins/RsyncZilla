# RsyncZilla Release Notes

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

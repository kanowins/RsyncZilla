# RsyncZilla Release Notes

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

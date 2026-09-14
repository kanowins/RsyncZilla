# RsyncZilla 🚀
RsyncZilla is a FileZilla-like file transfer client powered by rsync.

A modern SFTP client for Windows with an intuitive dual-pane GUI (FileZilla-style) powered by **verified transfers using portable rsync (v3.3.0) over SSH**.

Engineered to eliminate the classic issue of silent FTP transfer failures, ensuring atomic, resumable synchronizations that only upload new and modified byte deltas.

> 📖 **[👉 Read the detailed comparison: Why RsyncZilla is Better Than FileZilla](WHY_RSYNCZILLA.md)**

---

## 🌟 Key Features

1. **Fluid Navigation via SFTP (SSH.NET):**
   - **Left Panel:** Windows local filesystem browser.
   - **Right Panel:** Remote server filesystem browser over SFTP.
   - **Multi-Selection:** Select multiple items with Shift / Ctrl or drag a selection box in either panel.
   - **Bidirectional Drag & Drop:**
     - Drag items between Local and Remote panels to trigger instant uploads or downloads.
     - Drop directly onto subfolders to transfer into that specific destination.
   - **Drag & Drop from Windows Explorer:** Drag files or folders directly from desktop or Windows Explorer windows into the Remote panel to upload with rsync.
   - Double-click to navigate directories or open files in their native Windows associated applications.
   - **📂 Show in Explorer:** Right-click any local file or folder (or click the header button) to reveal and select it directly in Windows Explorer (`/select`).

2. **Delta Transfers with Portable rsync (v3.3.0):**
   - Bundles a clean, portable suite of `rsync.exe` and `ssh.exe` (Cygwin64) (~6 MB total).
   - **Zero Silent Failures:** If a transfer is interrupted or rejected, rsync returns an unequivocal exit code (`ExitCode != 0`) and the UI immediately reports the exact error log.
   - **Transfers Deltas Only:** Uses `-avzP --stats --update` to transmit only modified byte blocks and new files.
   - **Atomic Writes:** rsync writes to hidden temporary files before swapping, preventing corrupt or half-written files on your server.

3. **💻 Integrated Remote SSH Terminal (Portable KiTTY) (`Ctrl + T`):**
   - Instantly launches an authenticated SSH console positioned directly inside your current remote directory.
   - Run server-side commands (`npm run build`, `composer install`, `git pull`, `pm2 restart`, etc.) without opening PuTTY or manually typing `cd` paths.

4. **📝 Live Remote File Editing (`F4`):**
   - Press `F4` or double-click on any remote file to open it in your favorite local text editor (VS Code, Notepad++, Sublime, etc.).
   - On save (`Ctrl + S`), a background file watcher debounces writes (450 ms), computes SHA256 hashes, and quietly auto-uploads the file via SFTP.
   - If the connection drops or permissions fail, a clear error dialog alerts you immediately and preserves changes for automatic retry.

5. **Site Manager & Zero-Leak Credential Security:**
   - **Site Manager (📂 Sites):** Save frequently used servers (Host, User, Port) to connect in a single click.
   - **Strict Security:** **NEVER saves passwords to disk** (unlike FileZilla's plaintext XML).
   - Automatically navigates to the remote user's **home directory** upon login.
   - Built-in `RsyncAskPass.exe` and `SSH_ASKPASS` helper pipes credentials to OpenSSH subprocesses in-memory without intrusive popups.

6. **Transfer Queue & Real-Time Logs:**
   - **🚀 Queue:** Shows the active transfer with live progress, speed, and ETA, alongside queued tasks.
   - **⏹ Cancel All:** Immediately terminates active rsync processes and clears pending queue tasks.
   - **❌ Failed Transfers:** Dedicated tab with the exact exit code and stderr error output. Retry single items or **retry all failed** with one click.
   - **✅ Completed History:** Clean audit record of successful operations.
   - **📜 Live Log Console:** Real-time log stream of SFTP events, rsync commands, and diagnostics.

---

## 🏗️ Repository Architecture

- `src/RsyncZilla/`: Main WPF application (.NET 8 Windows x64).
  - `tools/cygwin64/`: Portable binaries (`rsync.exe`, `ssh.exe`, runtime DLLs).
  - `tools/kitty.exe`: Portable KiTTY SSH terminal.
  - `Models/`: Data models for files, transfer tasks, connections, and logs.
  - `Services/`:
    - `LocalFileService.cs`: Local disk browsing and file management.
    - `SftpService.cs`: Persistent SFTP connection and file stream handling via SSH.NET.
    - `RsyncService.cs`: Subprocess orchestration, progress stream parsing, and exit code validation.
    - `RemoteEditService.cs`: Temporary file lifecycle, SHA256 hashing, and live file watcher auto-sync.
    - `TerminalService.cs`: KiTTY terminal launcher with auto-navigation.
    - `ConnectionManagerService.cs`: Site manager persistence (metadata only).
  - `ViewModels/`: Decoupled MVVM presentation logic.
  - `Views/`: Custom XAML dialogs (Site Manager, InputDialog, SiteEditDialog).
- `src/RsyncAskPass/`: Secure credential pipe bridge for OpenSSH `SSH_ASKPASS`.
- `tests/RsyncZilla.Tests/`: xUnit test suite (78 tests covering rsync path translation, watchers, explorer integration, multi-selection, update checks, and CLI args).
- `dist/RsyncZilla/`: Ready-to-run precompiled portable distribution (`RsyncZilla.exe`).

---

## 🚀 Installation & Running

### Option 1: Download Latest Release
Download the ready-to-run portable `.zip` from **[GitHub Releases](https://github.com/kanowins/RsyncZilla/releases/latest)**, extract it anywhere, and launch `RsyncZilla.exe`.

> [!NOTE]
> **Windows Defender SmartScreen Notice:**
> When launching RsyncZilla for the first time on Windows, Microsoft Defender SmartScreen may display a blue warning screen: *"Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting"*.
>
> This is standard Windows behavior for open-source software downloaded from the internet that does not carry an expensive paid commercial code-signing certificate. The application is completely open-source, and all release binaries are compiled transparently by [GitHub Actions](https://github.com/kanowins/RsyncZilla/actions).
>
> **How to bypass it (first launch only):**
> 1. Click **More info** (*Más información*).
> 2. Click **Run anyway** (*Ejecutar de todas formas*).
>
> *Alternatively*, right-click the downloaded `.zip` or `RsyncZilla.exe` → **Properties** → check the **Unblock** box at the bottom → click **OK**. Or run in PowerShell:
> ```powershell
> Unblock-File .\RsyncZilla.exe
> ```

### Option 2: Run Local Build
Run directly from the local repository:
```cmd
dist\RsyncZilla\RsyncZilla.exe
```

### Option 3: Build from Source
Run `build.bat` or compile with the .NET 8 SDK:
```bash
dotnet publish src/RsyncZilla/RsyncZilla.csproj -c Release -r win-x64 --self-contained false -o dist/RsyncZilla
```

### Option 4: Run the Test Suite
```bash
dotnet test
```

### 🛠️ Versioning & Automated Release Scripts
For project maintainers:
- **Set or bump version**: `.\set-version.bat 1.0.2` (or `.\set-version.bat -Patch` / `-Minor` / `-Major`)
- **One-click publish to GitHub**: `.\publish-release.bat` (or `.\publish-release.bat -Patch`) — runs tests, commits, pushes tag, and triggers automated GitHub Actions release build.

---

## ☕ Want to support the developer?

RsyncZilla is free and open source.

If it saves you some time and you feel like buying me a coffee... **I'd rather you check out one of the games we make at Sumalab! 🎮**

- **[HeadHunters](https://store.steampowered.com/app/3675690/)** — A chaotic multiplayer party game where you literally play as a head.
- **[Vertigo Rush](https://store.steampowered.com/app/1483380/Vertigo_Rush/)** — VR racing + parkour. Basically, what happens when Mario Kart meets Gorilla Tag.

You can also find our games on other platforms at **[sumalab.com](https://sumalab.com/)**.

A wishlist, a review, or simply telling someone about them helps us more than a coffee ever could. ❤️

---

## 📄 License

MIT License. Open source and free to use.

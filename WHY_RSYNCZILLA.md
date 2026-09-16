# Why RsyncZilla is Better Than FileZilla 🚀

If you are a developer, sysadmin, or DevOps engineer managing servers over SSH/SFTP, you know the daily pain of **FileZilla**: transfers that fail silently, production files truncated and corrupted halfway through an upload, re-uploading multi-gigabyte files just because a single line changed, zero integrated terminal support, and server passwords stored in plaintext XML files on your hard drive.

**RsyncZilla** is the modern evolution of SFTP clients for Windows. It retains the intuitive, dual-pane GUI you already know, while replacing legacy, fragile FTP streams with the speed and atomic reliability of the native **rsync (v3.3.0) delta-transfer algorithm** over SSH.

---

## 📊 Quick Comparison Matrix

| Feature | 🦖 FileZilla | 🚀 RsyncZilla |
| :--- | :---: | :---: |
| **Transfer Engine** | Standard sequential SFTP / FTP stream | **Native rsync 3.3.0** with delta-transfer |
| **Differential Sync** | ❌ Always uploads the whole file (0% to 100%) | ✅ **Transfers only modified byte deltas** (saves up to 99% bandwidth) |
| **Atomic Writes & Integrity** | ❌ Network blip = truncated/corrupted file in production | ✅ **Atomic replacement** via hidden temp files; zero broken deploys |
| **Integrated SSH Terminal** | ❌ None. Must open external PuTTY and `cd` manually | ✅ **Built-in portable KiTTY (`Ctrl + T`)** launched in the active remote folder |
| **Live Remote Editing** | ⚠️ Annoying popup prompts every time you save | ✅ **Seamless auto-upload on save (`Ctrl + S`)** with `F4` & SHA256 hashing |
| **Local File Handling** | ⚠️ Clunky, rigid experience | ✅ Double-click, `Enter`, `F4`, and native **"Show in Explorer"** (`/select`) |
| **Credential Security** | 🚨 **Plaintext passwords** stored on disk (`sitemanager.xml`) | 🔒 **Zero passwords saved to disk** (in-memory only) |
| **Installer Cleanliness** | ⚠️ Infamous bundled adware / PUP installers | 🛡️ **100% Clean, portable, and open source** |
| **Error Handling** | ❌ Silent drops or ambiguous status codes | ✅ Dedicated Failed tab with exact rsync `ExitCode` & stderr log |
| **Drag & Drop** | Basic | ✅ Between panes & **directly from Windows Explorer** |

---

## 💥 The 7 Core Reasons to Switch

### 1. Intelligent Delta-Transfer with rsync
- **The FileZilla Problem:** When working with large JavaScript bundles (25+ MB), SQLite databases, Docker volumes, SQL dumps, or logs, changing just one line forces FileZilla to re-upload the entire multi-megabyte file across the internet.
- **The RsyncZilla Advantage:** Powered by the rsync rolling-checksum algorithm (`-avzP`), RsyncZilla **only transmits modified byte blocks (deltas)**. If a 100 MB file changed by 2 KB, only those 2 KB are compressed and sent. Your deployments take fractions of a second instead of minutes. Offers customizable file-exists actions under the *Transfer* menu.

### 2. Zero Corrupted Files: Atomic Writes
- **The FileZilla Problem:** If your Wi-Fi flickers or the remote server drops the connection while FileZilla is uploading a PHP, Python, or configuration file, the target file is left truncated in half—instantly breaking production with a 500 error before you even realize what happened.
- **The RsyncZilla Advantage:** rsync writes incoming data to a hidden temporary file on the remote server (`.~tmp~...`) and performs an atomic swap only when the full transfer succeeds and verifies integrity (`ExitCode == 0`). If the connection drops, your live production file remains untouched and safe.

### 3. Integrated Remote Terminal in Current Folder (`Ctrl + T`)
- **The FileZilla Problem:** You navigate deep into `/var/www/my-app/releases/v2.4/` and need to run `npm run build`, `composer install`, `git pull`, `pm2 restart`, or inspect system logs. You have to open a separate terminal (PuTTY or Windows Terminal), re-enter your host, username, and credentials, and manually `cd` through the directory tree.
- **The RsyncZilla Advantage:** RsyncZilla bundles a fully portable **KiTTY** terminal. Press **`Ctrl + T`** or right-click -> *"Open terminal here"*, and an authenticated SSH terminal instantly pops up, already positioned inside the exact remote directory you were browsing.

### 4. Frictionless Live Remote Editing (`F4`)
- **The FileZilla Problem:** FileZilla’s remote editing interrupts your flow with constant confirmation dialogs: *"A file has changed. Do you want to upload it now? [Yes/No]"*. If you miss the prompt or close it by mistake, your changes are lost on the server.
- **The RsyncZilla Advantage:**
  - Press **`F4`** (or double-click) on any remote file to open it in your favorite local editor (VS Code, Notepad++, Sublime, etc.).
  - Every time you press **`Ctrl + S`**, a background watcher debounces writes (450 ms), checks the SHA256 hash, and quietly auto-uploads the updated file via SFTP.
  - If your network drops or permissions fail, a clear error dialog alerts you immediately and preserves the file state so re-saving retries automatically without losing code.

### 5. Modern Local Navigation & "Show in Explorer"
- **The FileZilla Problem:** FileZilla’s local browser feels like Windows 98; opening files in native apps or jumping to the local directory in Windows Explorer is frustrating and clunky.
- **The RsyncZilla Advantage:**
  - Double-click or press `Enter` on any local file to launch it directly in Windows.
  - Right-click -> **`📂 Show in Explorer`** (or click the header button) to reveal and highlight that exact file inside Windows Explorer (`/select`).
  - Native Drag & Drop: Drag files directly from your desktop or Windows Explorer windows into the remote panel to trigger an rsync upload.

### 6. Real Security: Plaintext Passwords Are Gone
- **The FileZilla Danger:** FileZilla notoriously stores all saved Site Manager passwords in **unencrypted plaintext XML** inside `%APPDATA%\FileZilla\sitemanager.xml`. This file is the primary target for infostealers and trojans (RedLine, Raccoon, Vidar, AgentTesla), causing countless server compromises every day.
- **The RsyncZilla Security Model:** RsyncZilla’s Site Manager **NEVER writes passwords to disk**. It only persists connection metadata (Host, User, Port, Site Name). Passwords live solely in ephemeral session memory and are piped securely to OpenSSH subprocesses via our specialized `RsyncAskPass` / `SSH_ASKPASS` helper.

### 7. No Adware, No Bloatware, 100% Open Source
- **The FileZilla Installer Issue:** The official FileZilla website has historically bundled adware, search hijackers, and Potentially Unwanted Programs (PUPs) into its primary installer executable.
- **The RsyncZilla Standard:** 100% clean, transparent, open-source code built on modern .NET 8 WPF, Cygwin rsync 3.3.0, SSH.NET, and KiTTY. Fully portable—extract and run anywhere without system bloat.

---

## 🛠️ Built With

- **Frontend:** WPF / .NET 8 (Native Windows x64).
- **Sync Engine:** Portable rsync 3.3.0 (Cygwin64) over SSH.
- **SFTP Engine:** SSH.NET.
- **Embedded Terminal:** Portable KiTTY.
- **Credential Bridge:** Internal `RsyncAskPass.exe` secure pipe.

---

## 📥 Getting Started

Run the pre-compiled portable application from `dist/RsyncZilla/RsyncZilla.exe`, or build it from source:
```bash
build.bat
```
or via the .NET CLI:
```bash
dotnet publish src/RsyncZilla/RsyncZilla.csproj -c Release -r win-x64 --self-contained false -o dist/RsyncZilla
```

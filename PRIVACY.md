# Privacy Policy — RsyncZilla

**Last Updated:** 2026-11-18

RsyncZilla is a free, open-source Windows desktop client for SSH/SFTP file browsing, rsync delta transfers, and interactive SSH terminals. We respect your privacy and keep all user data strictly under your control.

---

## 1. No Telemetry or Tracking

RsyncZilla **does not** collect, store, sell, or transmit any telemetry, analytics, behavioral tracking, crash dumps, advertising identifiers, or personal information to the developer or any third party.

---

## 2. Network Communications

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it, except for the read-only update check described below:

1. **User-Initiated SSH / SFTP / Rsync Connections:**
   - RsyncZilla connects exclusively to the remote servers (host, port, username, credentials) that you explicitly configure and initiate.
   - Credentials are sent directly to your configured SSH server over encrypted SSH/SFTP channels and nowhere else.
2. **Update Availability Check (GitHub):**
   - On startup and when clicking **Help → Check for Updates...**, RsyncZilla performs read-only HTTPS `GET` requests to GitHub (`https://api.github.com/repos/kanowins/RsyncZilla/releases/latest` and `https://raw.githubusercontent.com/kanowins/RsyncZilla/main/dist/version.json`) solely to compare the installed version against the latest public release.
   - No personal data, server addresses, credentials, file names, or unique device identifiers are sent. Only standard HTTP request headers (including `User-Agent: RsyncZilla-Updater`) are transmitted, subject to [GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
   - Downloading and running an update installer happens only when you explicitly click **Update Now**.
3. **Optional Local AI MCP Server (Loopback Only):**
   - If you explicitly enable AI access on a terminal tab or click the `🤖 MCP` button, RsyncZilla opens a local-only IPC listener (`127.0.0.1:49876` and local Windows Named Pipe `\\.\pipe\RsyncZilla_MCP_Pipe`) strictly on your local machine.
   - It is never bound to external network interfaces and all terminal tabs default to `AI: Off`.

---

## 3. Local Data Storage

All application configuration is stored locally on your Windows machine:

- **Saved Sites & Optional Encrypted Password Vault (`%APPDATA%\RsyncZilla\`):**
  - Site connection profiles (host, port, username, remote/local paths) are saved locally.
  - Passwords are **never** stored in plaintext. If you opt in to the Password Vault, passwords are encrypted locally with **AES-256-GCM** using a key derived from your Master Password via **PBKDF2-HMAC-SHA256** (100,000 iterations and a random 32-byte salt).
- **Embedded Terminal Runtime Cache (`%LOCALAPPDATA%\RsyncZilla\WebView2\`):**
  - Used locally by Microsoft Edge WebView2 to render the local `xterm.js` terminal interface (served strictly from local disk via a virtual host mapping; no external web pages are loaded).

---

## 4. Third-Party Open Source Components

RsyncZilla bundles open-source command-line utilities (`rsync`, `OpenSSH`, `Cygwin`, `OpenSSL`) used solely to execute transfers to the servers you specify. See [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) for complete component details and licenses.

---

## 5. Contact

If you have any questions about this Privacy Policy or RsyncZilla's security practices, please open an issue on GitHub:
https://github.com/kanowins/RsyncZilla/issues

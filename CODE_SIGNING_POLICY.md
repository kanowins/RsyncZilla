# Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

---

## 1. Project Information

- **Project Name:** RsyncZilla
- **Repository:** https://github.com/kanowins/RsyncZilla
- **Website:** https://rsynczilla.github.io/
- **License:** [MIT License](LICENSE) (OSI-approved; no commercial dual-licensing or proprietary components)
- **Third-Party Licensing & Source Attribution:** [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md)

---

## 2. Team Roles

All team members with write access to the repository, review permissions, or access to SignPath are required to have Multi-Factor Authentication (MFA / 2FA) enabled on both GitHub and SignPath.

- **Authors** (trusted to commit and maintain source code):
  - [@kanowins](https://github.com/kanowins) (Project Owner & Maintainer)
- **Reviewers** (responsible for reviewing pull requests and external contributions before merging into `main`):
  - [@kanowins](https://github.com/kanowins)
- **Approvers** (authorized to approve release signing requests in SignPath):
  - [@kanowins](https://github.com/kanowins)

Any external contributions (including automated dependency updates) must be submitted via Pull Request and explicitly reviewed by a Reviewer before being merged into `main`.

---

## 3. Build & Signing Process

- **Build Origin:** All signed releases are built exclusively from source on GitHub-hosted runners (`windows-latest`) via [GitHub Actions](.github/workflows/release.yml).
- **Automated Verification:** Every release workflow runs the full automated unit test suite (`dotnet test`) before packaging or requesting signatures.
- **Signing Scope:** Only binaries compiled directly from the RsyncZilla repository source code and the RsyncZilla Windows Installer are submitted for signing:
  - `RsyncZilla.exe` and `RsyncZilla.dll`
  - `RsyncAskPass.exe` and `RsyncAskPass.dll`
  - `RsyncZilla-Setup-v*-win-x64.exe` (Inno Setup installer)
  - Pre-compiled third-party open-source tools bundled in `tools/` (such as `rsync.exe`, `ssh.exe`, and `cygwin1.dll`) are **not** signed with the SignPath Foundation certificate.
- **Secrets Management:** SignPath API credentials are stored strictly as encrypted GitHub Actions Secrets and are never committed to source control or exposed in build logs or artifacts.

---

## 4. Privacy Policy

RsyncZilla does **not** collect telemetry, analytics, crash reports, usage statistics, or personal data.

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it, with the sole exception of read-only HTTPS update checks against GitHub Releases (`api.github.com` / `raw.githubusercontent.com`) to notify the user when a newer version is available.

For full details on local storage and network behavior, please see our **[Privacy Policy](PRIVACY.md)**.

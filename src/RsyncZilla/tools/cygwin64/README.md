# Cygwin & Third-Party Binaries

The files in this directory are bundled with RsyncZilla to provide standalone, out-of-the-box file synchronization functionality on Windows.

RsyncZilla itself is open source software licensed under the **MIT License**. The binaries in this directory are distributed as an aggregate under their respective open source licenses.

## Included Components

| Binary / Library | Upstream Project | License | Source Code & Homepage |
| :--- | :--- | :--- | :--- |
| `rsync.exe` (3.3.0) | Andrew Tridgell, Wayne Davison, et al. | GNU GPL v3 or later | [rsync.samba.org](https://rsync.samba.org/) &bull; [Source Download](https://download.samba.org/pub/rsync/) |
| `cygwin1.dll` (3.6.3) | Cygwin Authors / Red Hat | GNU LGPL v3 or later | [cygwin.com](https://cygwin.com/) &bull; [Git Repository](https://cygwin.com/git/newlib-cygwin.git) |
| `cygpath.exe` (3.6.3) | Cygwin Authors | GNU GPL v3 or later | [cygwin.com](https://cygwin.com/) |
| `ssh.exe` (OpenSSH 10.0p2) | OpenSSH Project | BSD / ISC | [openssh.com](https://www.openssh.com/) |
| `cygcrypto-3.dll` (3.0.16) | OpenSSL Project | Apache License 2.0 | [openssl.org](https://www.openssl.org/) |
| `cygkrb5-3.dll`, `cyggssapi_krb5-2.dll`, `cygk5crypto-3.dll`, `cygkrb5support-0.dll`, `cygcom_err-2.dll` | MIT Kerberos / e2fsprogs | MIT / BSD-style | [web.mit.edu/kerberos](https://web.mit.edu/kerberos/) |
| `cyglz4-1.dll` | Yann Collet | BSD 2-Clause | [lz4.org](https://lz4.org/) |
| `cygxxhash-0.dll` | Yann Collet | BSD 2-Clause | [xxhash.com](https://xxhash.com/) |
| `cygz.dll` | Jean-loup Gailly, Mark Adler | zlib License | [zlib.net](https://zlib.net/) |
| `cygzstd-1.dll` | Meta Platforms, Inc. | BSD 3-Clause / GPLv2 | [facebook.github.io/zstd](https://facebook.github.io/zstd/) |
| `cygiconv-2.dll` | GNU libiconv | GNU LGPL v2.1 or later | [gnu.org/software/libiconv](https://www.gnu.org/software/libiconv/) |
| `cygintl-8.dll` | GNU gettext | GNU LGPL v2.1 or later | [gnu.org/software/gettext](https://www.gnu.org/software/gettext/) |
| `cyggcc_s-seh-1.dll` | Free Software Foundation | GPL v3 with GCC Runtime Library Exception | [gcc.gnu.org](https://gcc.gnu.org/) |

## License Texts

* Full text of the **GNU General Public License v3** is available in [COPYING.gpl3.txt](file:///e:/Dropbox/Dropbox/_David/projects/filezilla/src/RsyncZilla/tools/cygwin64/COPYING.gpl3.txt).
* Full text of the **GNU Lesser General Public License v3** is available in [COPYING.lgpl3.txt](file:///e:/Dropbox/Dropbox/_David/projects/filezilla/src/RsyncZilla/tools/cygwin64/COPYING.lgpl3.txt).
* Full third-party license details and attributions are available in the repository root [THIRD_PARTY_LICENSES.md](file:///e:/Dropbox/Dropbox/_David/projects/filezilla/THIRD_PARTY_LICENSES.md).

## Source Code Availability (GPL Compliance)

In compliance with Section 6 of the GNU GPL v3:
The source code for `rsync` 3.3.0 and the corresponding Cygwin packages can be downloaded directly from:
- Official rsync release archive: https://download.samba.org/pub/rsync/src/rsync-3.3.0.tar.gz
- Official rsync git repository: https://github.com/WayneD/rsync
- Official Cygwin source packages: https://cygwin.com/packages/

# Third-Party Software and Licenses

RsyncZilla is an open-source application licensed under the **MIT License**.

The distributed binary packages (ZIP releases and Inno Setup installers) include bundled third-party tools and runtime libraries to provide an out-of-the-box file synchronization experience on Microsoft Windows without requiring separate manual installation.

This document lists all third-party software, their licenses, copyright notices, and instructions on how to access the corresponding source code in compliance with the **GNU General Public License (GPLv3)** and **GNU Lesser General Public License (LGPLv3)**.

---

## 1. Notice of Aggregate Distribution

RsyncZilla interacts with `rsync.exe` and `ssh.exe` exclusively as independent external processes via standard operating system process creation (`System.Diagnostics.Process`), command-line arguments, and standard I/O streams (pipes).

According to the Free Software Foundation (FSF) GNU GPL licensing guidelines, distributing independent programs together on the same installation medium constitutes a **compilation / aggregate** ("mere aggregation"). The author's original C#/WPF source code of RsyncZilla remains under the **MIT License**, while each bundled third-party tool remains governed by its respective license.

---

## 2. Bundled Binaries (Cygwin & Core Utilities)

The standalone distribution includes the following binaries located in `tools/cygwin64/`:

### rsync (Version 3.3.0)
* **License**: GNU General Public License v3.0 or later (GPL-3.0-or-later)
* **Copyright**: Copyright &copy; 1996–2024 by Andrew Tridgell, Wayne Davison, and others.
* **Homepage**: [https://rsync.samba.org/](https://rsync.samba.org/)
* **License Text**: See `tools/cygwin64/COPYING.gpl3.txt` or [https://www.gnu.org/licenses/gpl-3.0.html](https://www.gnu.org/licenses/gpl-3.0.html)
* **Source Code Access**: In compliance with Section 6 of the GPLv3, the corresponding source code for rsync 3.3.0 is publicly available for download from:
  * Official Source Tarball: [https://download.samba.org/pub/rsync/src/rsync-3.3.0.tar.gz](https://download.samba.org/pub/rsync/src/rsync-3.3.0.tar.gz)
  * Official Git Repository: [https://github.com/WayneD/rsync](https://github.com/WayneD/rsync)

### Cygwin API Library (`cygwin1.dll`)
* **License**: GNU Lesser General Public License v3.0 or later (LGPL-3.0-or-later)
* **Copyright**: Copyright &copy; 1998–2025 Cygwin Authors / Red Hat, Inc.
* **Homepage**: [https://www.cygwin.com/](https://www.cygwin.com/)
* **License Text**: See `tools/cygwin64/COPYING.lgpl3.txt` or [https://www.gnu.org/licenses/lgpl-3.0.html](https://www.gnu.org/licenses/lgpl-3.0.html)
* **Source Code Access**: Source code for Cygwin can be obtained from:
  * Git Repository: [https://cygwin.com/git/newlib-cygwin.git](https://cygwin.com/git/newlib-cygwin.git)
  * Cygwin Package Repository: [https://cygwin.com/packages/](https://cygwin.com/packages/)

### Cygwin Utilities (`cygpath.exe`)
* **License**: GNU General Public License v3.0 or later (GPL-3.0-or-later)
* **Copyright**: Copyright &copy; 1998–2025 Cygwin Authors.
* **Homepage**: [https://www.cygwin.com/](https://www.cygwin.com/)
* **License Text**: See `tools/cygwin64/COPYING.gpl3.txt`.

### OpenSSH (`ssh.exe`)
* **License**: BSD / ISC / MIT-style Licenses
* **Copyright**: Copyright &copy; Markus Friedl, Todd C. Miller, Theo de Raadt, Damien Miller, and others.
* **Homepage**: [https://www.openssh.com/](https://www.openssh.com/)
* **License Information**: [https://www.openssh.com/licence.html](https://www.openssh.com/licence.html)

### OpenSSL (`cygcrypto-3.dll`)
* **License**: Apache License 2.0
* **Copyright**: Copyright &copy; 1998–2025 The OpenSSL Project.
* **Homepage**: [https://www.openssl.org/](https://www.openssl.org/)
* **License Text**: [https://www.openssl.org/source/license.html](https://www.openssl.org/source/license.html)

### MIT Kerberos / e2fsprogs (`cygkrb5-3.dll`, `cyggssapi_krb5-2.dll`, `cygk5crypto-3.dll`, `cygkrb5support-0.dll`, `cygcom_err-2.dll`)
* **License**: MIT / BSD-style License
* **Copyright**: Copyright &copy; 1985–2024 Massachusetts Institute of Technology.
* **Homepage**: [https://web.mit.edu/kerberos/](https://web.mit.edu/kerberos/)

### LZ4 Compression Library (`cyglz4-1.dll`)
* **License**: BSD 2-Clause License
* **Copyright**: Copyright &copy; 2011–2020 Yann Collet.
* **Homepage**: [https://lz4.org/](https://lz4.org/)

### xxHash Library (`cygxxhash-0.dll`)
* **License**: BSD 2-Clause License
* **Copyright**: Copyright &copy; 2012–2021 Yann Collet.
* **Homepage**: [https://xxhash.com/](https://xxhash.com/)

### zlib Data Compression Library (`cygz.dll`)
* **License**: zlib License
* **Copyright**: Copyright &copy; 1995–2024 Jean-loup Gailly and Mark Adler.
* **Homepage**: [https://zlib.net/](https://zlib.net/)

### Zstandard Compression (`cygzstd-1.dll`)
* **License**: BSD 3-Clause License / GPLv2 dual license
* **Copyright**: Copyright &copy; Meta Platforms, Inc. and affiliates.
* **Homepage**: [https://facebook.github.io/zstd/](https://facebook.github.io/zstd/)

### GNU libiconv & gettext (`cygiconv-2.dll`, `cygintl-8.dll`)
* **License**: GNU Lesser General Public License v2.1 or later (LGPL-2.1-or-later)
* **Copyright**: Free Software Foundation, Inc.
* **Homepage**: [https://www.gnu.org/software/libiconv/](https://www.gnu.org/software/libiconv/) &bull; [https://www.gnu.org/software/gettext/](https://www.gnu.org/software/gettext/)

### GCC Runtime Support (`cyggcc_s-seh-1.dll`)
* **License**: GNU General Public License v3.0 with GCC Runtime Library Exception
* **Copyright**: Free Software Foundation, Inc.
* **Homepage**: [https://gcc.gnu.org/](https://gcc.gnu.org/)

---

## 3. Managed .NET Dependencies (NuGet)

### SSH.NET
* **License**: MIT License
* **Copyright**: Copyright &copy; 2012–2026 Renci
* **Repository**: [https://github.com/sshnet/SSH.NET](https://github.com/sshnet/SSH.NET)

### Microsoft.Web.WebView2
* **License**: Microsoft Software License
* **Copyright**: Copyright &copy; Microsoft Corporation
* **Package**: [https://www.nuget.org/packages/Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2)

### VirtualFileDataObject
* **License**: BSD 2-Clause License
* **Copyright**: Copyright &copy; 2008 David Anson
* **Repository**: [https://dlaa.me/blog/post/9913083](https://dlaa.me/blog/post/9913083)

---

## 4. Frontend & Terminal Assets

### xterm.js
* **License**: MIT License
* **Copyright**: Copyright &copy; 2017–2026 The xterm.js authors
* **Repository**: [https://github.com/xtermjs/xterm.js](https://github.com/xtermjs/xterm.js)

---

## 5. Main RsyncZilla License (MIT)

The original source code of RsyncZilla (excluding third-party binaries listed above) is licensed under the MIT License:

```
MIT License

Copyright (c) 2026 Sumalab

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

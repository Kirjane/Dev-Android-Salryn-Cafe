# I1 Pre-Beta Package Contents Manifest

## Package identity

```text
Source commit: 0f456fd6df459168e3b918d6f1f99f87653d7321
Package folder: C:\Users\kirch\Salryn\artifacts\i1-package\Salryn
Archive: C:\Users\kirch\Salryn\artifacts\i1-package\Salryn-I1-PRE-BETA.zip
Archive SHA-256: 813BFBD78428CF89B9A48224F87FCBDF84D33BDEC0F6D91923632E6412D04B9D
Archive bytes: 37103855
Package files: 71
Package bytes: 77944265
Package tree SHA-256: 93466EE541D872767CA51DD0DE2F7DCC857841A2CD175A717D9FBB7CC66BA883
```

The package-tree checksum is SHA-256 over the sorted UTF-8 lines `relative-path|byte-count|file-sha256`. The archive checksum is the distribution checksum and is also stored beside the ZIP in `Salryn-I1-PRE-BETA.zip.sha256`.

## Layout

| Group | Files | Contents |
| --- | ---: | --- |
| `dist\` | 4 | Production `index.html`, hashed JS/CSS, and Salryn build stamp |
| `runtimes\` | 31 | Published native SQLite runtime assets selected by `dotnet publish` |
| Salryn API entry/output | 7 | API EXE/DLL/PDB, deps/runtime config, static-assets endpoint metadata, `web.config` |
| Salryn product assemblies | 8 | Core, Host.Local, Composition.Local, Infrastructure DLL/PDB pairs |
| Published third-party assemblies | 16 | SQLite, PDFsharp/MigraDoc, and SQLitePCLRaw dependencies |
| Configuration | 2 | `appsettings.json`, `appsettings.Development.json`; no secrets found |
| Package guidance/stamp | 3 | `PACKAGE-RUNBOOK.md`, `KNOWN-LIMITATIONS.md`, `salryn-i1-package-stamp.txt` |
| **Total** | **71** | Framework-dependent browser package |

## Top-level product files

```text
Salryn.Api.Local.exe
Salryn.Api.Local.dll
Salryn.Api.Local.pdb
Salryn.Api.Local.deps.json
Salryn.Api.Local.runtimeconfig.json
Salryn.Api.Local.staticwebassets.endpoints.json
Salryn.Composition.Local.dll
Salryn.Composition.Local.pdb
Salryn.Core.dll
Salryn.Core.pdb
Salryn.Host.Local.dll
Salryn.Host.Local.pdb
Salryn.Infrastructure.dll
Salryn.Infrastructure.pdb
PACKAGE-RUNBOOK.md
KNOWN-LIMITATIONS.md
salryn-i1-package-stamp.txt
appsettings.json
appsettings.Development.json
web.config
```

The remaining top-level DLLs and `runtimes` subtree are the transitive framework publish dependencies. They were produced by `dotnet publish`; no repo source tree is included.

## Static web proof

```text
Web root: dist\
Index: dist\index.html
Title: Salryn POS
JavaScript: dist\assets\index-CJrVIqEk.js
CSS: dist\assets\index-CxnNV5FW.css
Build stamp: dist\salryn-build-stamp.txt
Vite client marker: absent
localhost:5173 marker: absent
```

## Package stamp

```text
Product=Salryn POS
Lane=I1-PACKAGE-REFRESH-PRE-BETA
Configuration=Release
PublishMode=framework-dependent
Entry=Salryn.Api.Local.exe
HostUrl=http://127.0.0.1:5299/
WebDist=dist\
GitSha=0f456fd6df459168e3b918d6f1f99f87653d7321
NotPublicInstaller=true
NotAutoUpdater=true
NotCloudActivation=true
BrowserPackageOnly=true
DesktopAppShell=Deferred
```

The generated UTC staging timestamp is intentionally omitted from this documentation excerpt; the exact value remains in the packaged stamp.

## Forbidden-content scan

The final 71-file package tree was scanned by path/name and text content.

| Forbidden content | Hits |
| --- | ---: |
| `.git` | 0 |
| `.secrets` | 0 |
| Private-key filenames or PEM markers | 0 |
| `node_modules` | 0 |
| `.salryn-dev` | 0 |
| `license.json` | 0 |
| SQLite database files | 0 |
| Proof logs or temporary proof folders | 0 |
| Vite `@vite/client` or `localhost:5173` markers | 0 |
| Customer data | 0 |

Allowed public verification material embedded in compiled application assemblies is not a private key and is not classified as a leak.

## Explicit exclusions

- No WebView2 desktop-shell executable.
- No shortcut or icon resource.
- No installer/MSI/WiX payload.
- No LicensingOps executable, ledger, generated license, or signing secret.
- No local user license or LocalAppData state.
- No development/proof/production database.
- No backup, receipt, export, or audit data from the proof run.
- No customer data.

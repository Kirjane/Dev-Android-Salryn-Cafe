# Salryn POS Beta RC1 — installer lane

This folder contains the Inno Setup 6 script (`SalrynSetup.iss`) for the Beta RC1
Windows installer.

---

## Release-build prerequisites (build machine only)

| Prerequisite | Purpose | End-user needs it? |
|---|---|---|
| .NET SDK (matching `Salryn.sln`) | Build/publish .NET projects | No |
| Node.js / npm | Build React web dist | No |
| Approved branding assets in `assets/branding/` | Icon, banner PNGs, ICO | No |
| **Inno Setup 6** | **Compile `SalrynSetup.iss` into a setup EXE** | **No** |
| WebView2 Evergreen Bootstrapper in `installer/redist/` | Packaged into setup; installs runtime on client | No (installer handles it) |

Download Inno Setup 6: <https://jrsoftware.org/isinfo.php>

After a standard installation, ISCC.exe will be at:

```
C:\Program Files (x86)\Inno Setup 6\ISCC.exe
```

or (per-user):

```
%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
```

The build script discovers it automatically.  You can also override via the
`INNO_SETUP_ISCC` environment variable or the `-InnoSetupCompilerPath` parameter.

If `installer/redist/MicrosoftEdgeWebview2Setup.exe` is missing, the build script
downloads it from the official Microsoft Evergreen Bootstrapper link.

---

## Build commands

```powershell
cd C:\path\to\Salryn

# Regenerate branding assets from the approved local source files (when needed)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\branding\Generate-SalrynBrandingAssets.ps1

# Stage publish output only (no Inno Setup required; defaults to private <KnownFolder:LocalAppData>\SalrynInstallerBuilds\run-<utc>-<id>)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1

# Stage + compile setup EXE (requires Inno Setup 6 and explicit -ExpectedSourceSha)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -ExpectedSourceSha <40-hex-sha>
```

---

## Installer properties

| Property | Value |
|---|---|
| Display name | Salryn POS |
| Publisher | Kirjane Information Technology Services |
| Install path | `C:\Program Files\Kirjane Information Technology Services\Salryn POS` |
| Production data | `C:\ProgramData\Salryn` (never removed on uninstall) |
| Desktop shortcut | Salryn POS |
| Start Menu folder | `Kirjane Information Technology Services\Salryn POS` |
| App / shortcut icon | `assets\branding\salryn-icon.ico` |
| Output EXE | `<output-root>\setup\Salryn-POS-BetaRC1-Setup.exe` (defaults to `<KnownFolder:LocalAppData>\SalrynInstallerBuilds\run-<utc>-<id>\setup\Salryn-POS-BetaRC1-Setup.exe`; override with `-OutputDir <new-private-path>`; exact `Setup EXE:` and `-VerifyStageOnly` command are printed at completion) |
| WebView2 | Evergreen Bootstrapper (`installer/redist/MicrosoftEdgeWebview2Setup.exe`) |

---

## WebView2 Runtime (client)

- Salryn setup **detects** WebView2 Runtime via Microsoft-documented registry `pv` keys.
- If missing, setup runs `MicrosoftEdgeWebview2Setup.exe /silent /install`.
- Clients do **not** install WebView2 manually.
- **Internet may be required** during beta install so the Evergreen Bootstrapper can download the runtime.
- Offline bundling of the larger standalone WebView2 installer is deferred.

---

## Installed layout (under `C:\Program Files\Kirjane Information Technology Services\Salryn POS`)

```
Salryn.Desktop.WebView.exe      ← WebView2 app shell — user-facing entry point
Salryn.Api.Local.exe            ← local API host (started by the app shell)
*.dll / runtimes\               ← .NET dependencies
dist\                           ← React production build served from the API host
assets\branding\                ← icon + banner assets (used by shortcuts)
salryn-beta-rc1-installer-stage.txt
```

---

## End-user experience

- The client receives **`Salryn-POS-BetaRC1-Setup.exe`** only.
- They run the installer (standard Windows wizard).
- Setup installs WebView2 Runtime if needed, then Salryn POS.
- After installation they launch **Salryn POS** from the desktop or Start Menu shortcut.
- They do **not** need PowerShell, a browser, localhost, Inno Setup, or a manual WebView2 install.
- Production data remains under `%ProgramData%\Salryn` and is preserved on upgrade/uninstall.

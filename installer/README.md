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

Download Inno Setup 6: <https://jrsoftware.org/isinfo.php>

After a standard installation, ISCC.exe will be at:

```
C:\Program Files (x86)\Inno Setup 6\ISCC.exe
```

The build script discovers it automatically.  You can also override via the
`INNO_SETUP_ISCC` environment variable or the `-InnoSetupCompilerPath` parameter.

---

## Build commands

```powershell
cd C:\path\to\Salryn

# Regenerate branding assets from the approved local source files (when needed)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\branding\Generate-SalrynBrandingAssets.ps1

# Stage publish output only (no Inno Setup required)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1

# Stage + compile setup EXE (requires Inno Setup 6)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
```

---

## Installer properties

| Property | Value |
|---|---|
| Display name | Salryn POS |
| Publisher | Kirjane Labs |
| Install path | `C:\Program Files\Kirjane Labs\Salryn POS` |
| Production data | `C:\ProgramData\Salryn` (never removed on uninstall) |
| Desktop shortcut | Salryn POS |
| Start Menu folder | `Kirjane Labs\Salryn POS` |
| App / shortcut icon | `assets\branding\salryn-icon.ico` |
| Output EXE | `artifacts\installer\setup\Salryn-POS-BetaRC1-Setup.exe` |

---

## Installed layout (under `C:\Program Files\Kirjane Labs\Salryn POS`)

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
- After installation they launch **Salryn POS** from the desktop or Start Menu shortcut.
- They do **not** need PowerShell, a browser, localhost, or Inno Setup.
- Production data is stored under `C:\ProgramData\Salryn` and is preserved on upgrade/uninstall.

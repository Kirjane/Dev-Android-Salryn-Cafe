# Beta RC1 — Installer + App Shell Report

## Branch

`KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

## Scope

This lane delivers:

1. Approved Beta RC1 branding assets (icon, banners, ICO, favicon sizes).
2. WebView2 app shell (`Salryn.Desktop.WebView`) wired with the Salryn POS icon and title.
3. Inno Setup 6 installer script (`installer/SalrynSetup.iss`) — proper, reusable build path.
4. Hardened build script (`tools/installer/Build-SalrynBetaRc1Installer.ps1`) with robust
   ISCC discovery, clear failure messages, and SHA-256 output.
5. Web manifest and favicon wiring in the React app.

---

## What changed

### Branding assets

| File | Role |
|---|---|
| `assets/branding/salryn-icon.png` | Master icon (all surfaces) |
| `assets/branding/salryn-icon.ico` | Windows ICO (16/32/48/64/128/256) |
| `assets/branding/salryn-icon-{N}x{N}.png` | Resized PNGs: 16/32/48/64/128/192/256/512/1024 |
| `assets/branding/salryn-banner-clear.png` | Light-background banner |
| `assets/branding/salryn-banner-filled.png` | Dark-background banner |

Generation tool: `tools/branding/Generate-SalrynBrandingAssets.ps1`

### App shell (`src/Salryn.Desktop.WebView`)

- `ApplicationIcon` → `assets/branding/salryn-icon.ico`
- Window title → `Salryn POS` (was `Salryn Ledger Console`)
- Error dialog titles → `Salryn POS`

### Web app (`src/Salryn.App.Web`)

- `public/` folder added with: `favicon.ico`, `favicon-16x16.png`, `favicon-32x32.png`,
  `apple-touch-icon.png`, `icon-192.png`, `icon-512.png`, `site.webmanifest`
- `index.html` updated with favicon and manifest `<link>` tags
- `site.webmanifest`: `name: Salryn POS`, `short_name: Salryn`

### Installer

- `installer/SalrynSetup.iss` — Inno Setup 6 script
  - App name: **Salryn POS**, Publisher: **Kirjane Labs**
  - Install path: `C:\Program Files\Kirjane Labs\Salryn POS`
  - Start Menu: `Kirjane Labs\Salryn POS`
  - Desktop shortcut: **Salryn POS**
  - Icon: `assets\branding\salryn-icon.ico`
  - ProgramData preserved on uninstall
  - Output: `Salryn-POS-BetaRC1-Setup.exe`

- `tools/installer/Build-SalrynBetaRc1Installer.ps1` — hardened build script
  - ISCC discovery: parameter → env var → PATH → standard dirs
  - Fails clearly if `-BuildInstaller` requested and ISCC missing
  - Staging-only mode when `-BuildInstaller` not passed
  - SHA-256 file written beside setup EXE

- `tools/Salryn.Tools.BrandingAssets/` — small .NET tool for cross-machine PNG/ICO generation
- `tools/branding/Generate-SalrynBrandingAssets.ps1` — invokes the generation tool

---

## Gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| Installer staging (`Build-SalrynBetaRc1Installer.ps1 -SkipWebBuild`) | PASS |
| Forbidden scan on `artifacts/installer/staging/Salryn` | PASS |
| ISCC compile (`-BuildInstaller`) | BLOCKED — Inno Setup 6 not installed on build machine |

---

## Installer EXE status

`BLOCKED_INSTALLER_EXE_COMPILE_INNO_SETUP_MISSING`

Inno Setup 6 is not installed on this machine.  The staging pipeline, script hardening,
and all source-code changes are complete and gated.  The setup EXE itself cannot be produced
until Inno Setup 6 is installed.

Install from: <https://jrsoftware.org/isinfo.php>

Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
```

---

## Decision

`BETA_RC1_INSTALLER_SCRIPT_HARDENED_INNO_REQUIRED`
`BLOCKED_INSTALLER_EXE_COMPILE_INNO_SETUP_MISSING`

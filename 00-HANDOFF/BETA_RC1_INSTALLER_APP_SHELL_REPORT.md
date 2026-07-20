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
  - Stable AppId: `A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E` (Inno-compatible upgrade recognition)
  - `UsePreviousAppDir=yes` for upgrade path reuse

- `tools/installer/Build-SalrynBetaRc1Installer.ps1` — hardened build script
  - ISCC discovery: parameter → env var → PATH → `%LOCALAPPDATA%\Programs\Inno Setup 6\` → Program Files paths
  - Fails clearly if `-BuildInstaller` requested and ISCC missing
  - Staging-only mode when `-BuildInstaller` not passed
  - SHA-256 file written beside setup EXE

---

## Installer compile fix (2026-07-20)

### Previous compile blocker

```
Error on line 54 in installer\SalrynSetup.iss:
Unrecognized [Setup] section directive "MajorUpgrade"
```

`MajorUpgrade` is a WiX/MSI directive and is not valid in Inno Setup 6.7.3.

### Fix applied

- Removed invalid `MajorUpgrade=no` from `[Setup]`
- Retained stable `AppId` (`A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E`) for Inno-compatible upgrade recognition
- Added `UsePreviousAppDir=yes` so upgrades reuse the prior install directory
- File replacement on upgrade handled by `ignoreversion` on `[Files]` entries
- `C:\ProgramData\Salryn` remains outside `[UninstallDelete]` — client data survives upgrade, uninstall, and reinstall

### ISCC path found

```
C:\Users\kirch\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```

(Inno Setup 6.7.3, per-user install under `%LOCALAPPDATA%\Programs\`)

---

## Installer EXE

| Property | Value |
|---|---|
| Setup EXE path | `artifacts\installer\setup\Salryn-POS-BetaRC1-Setup.exe` |
| SHA-256 | `32c3c4e3d31f5bf8248d1e72d99a5a340f4edf2ffb1a59536e99103db916f92e` |
| SHA file | `artifacts\installer\setup\Salryn-POS-BetaRC1-Setup.exe.sha256.txt` |

---

## Gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| Installer staging (`Build-SalrynBetaRc1Installer.ps1 -SkipWebBuild`) | PASS |
| ISCC compile (`-BuildInstaller`) | PASS |
| Forbidden scan on `artifacts/installer/staging/Salryn` | PASS |

---

## Decision

`BETA_RC1_INSTALLER_EXE_COMPILED`

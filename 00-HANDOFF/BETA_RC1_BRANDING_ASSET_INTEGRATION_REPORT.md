# BETA RC1 Branding Asset Integration Report

## Branch

`KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

## Source asset folder

`C:\Users\kirch\Desktop\Salryn Icon`

Approved Beta RC1 source files:

| Source file | Role |
|-------------|------|
| `icon type.png` | Icon-only master asset |
| `banner type clear.png` | Light-background banner |
| `banner type filled.png` | Dark-background banner |

## Copied asset paths

| Repo path | Source |
|-----------|--------|
| `assets/branding/salryn-icon.png` | `icon type.png` |
| `assets/branding/salryn-banner-clear.png` | `banner type clear.png` |
| `assets/branding/salryn-banner-filled.png` | `banner type filled.png` |

## Generated icon sizes

Generated from `icon type.png`:

| Size | Branding path |
|------|---------------|
| 16×16 | `assets/branding/salryn-icon-16x16.png` |
| 32×32 | `assets/branding/salryn-icon-32x32.png` |
| 48×48 | `assets/branding/salryn-icon-48x48.png` |
| 64×64 | `assets/branding/salryn-icon-64x64.png` |
| 128×128 | `assets/branding/salryn-icon-128x128.png` |
| 192×192 | `assets/branding/salryn-icon-192x192.png` |
| 256×256 | `assets/branding/salryn-icon-256x256.png` |
| 512×512 | `assets/branding/salryn-icon-512x512.png` |
| 1024×1024 | `assets/branding/salryn-icon-1024x1024.png` |

## ICO path

`assets/branding/salryn-icon.ico`

ICO includes sizes: 16, 32, 48, 64, 128, 256.

Regeneration command:

```powershell
powershell -File tools\branding\Generate-SalrynBrandingAssets.ps1
```

## Favicon / web public paths

| Path |
|------|
| `src/Salryn.App.Web/public/favicon.ico` |
| `src/Salryn.App.Web/public/favicon-16x16.png` |
| `src/Salryn.App.Web/public/favicon-32x32.png` |
| `src/Salryn.App.Web/public/apple-touch-icon.png` |
| `src/Salryn.App.Web/public/icon-192.png` |
| `src/Salryn.App.Web/public/icon-512.png` |
| `src/Salryn.App.Web/public/site.webmanifest` |

`src/Salryn.App.Web/index.html` now references all favicon/manifest links.

Web manifest:

- `name`: Salryn POS
- `short_name`: Salryn
- `icons`: `icon-192.png`, `icon-512.png`

## App shell icon path

| Item | Path / value |
|------|----------------|
| Project | `src/Salryn.Desktop.WebView/Salryn.Desktop.WebView.csproj` |
| Application icon | `assets/branding/salryn-icon.ico` |
| Window title | `Salryn POS` |
| Window icon | Extracted from published executable icon |

## Installer icon path

| Item | Path / value |
|------|----------------|
| Installer script | `installer/SalrynSetup.iss` |
| Build script | `tools/installer/Build-SalrynBetaRc1Installer.ps1` |
| Installer display name | Salryn POS |
| Publisher | Kirjane Labs |
| Installer icon | `assets/branding/salryn-icon.ico` |
| Desktop shortcut | Salryn POS |
| Start Menu folder | Kirjane Labs\Salryn POS |
| Shortcut icon | `assets/branding/salryn-icon.ico` (staged under installed `assets/branding/`) |

Installer staging output:

- `artifacts/installer/staging/Salryn/`

## Gates run

| Gate | Result |
|------|--------|
| `dotnet build Salryn.sln` | PASS |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| `git status` | Clean diff check; new branding/installer files present as new/untracked |
| Installer staging (`Build-SalrynBetaRc1Installer.ps1 -SkipWebBuild`) | PASS |

## Forbidden scan result

Target: `artifacts/installer/staging/Salryn`

Result: **PASS** — no forbidden files found.

Scan pattern: `.secrets`, `private`, `.pem`, `.key`, `node_modules`, `.git`, `salryn.db`, `generated`

## Wiring summary

- Approved icon-only asset used for app, installer, shortcut, and favicon surfaces.
- Banner assets copied for README/installer/splash use; not used as shortcut icons.
- WebView2 app shell title updated from engineering-console wording to **Salryn POS**.
- No logo redesign, no new variants, no banner-as-icon substitution.

## Final decision

**BETA_RC1_BRANDING_ASSETS_INTEGRATED**

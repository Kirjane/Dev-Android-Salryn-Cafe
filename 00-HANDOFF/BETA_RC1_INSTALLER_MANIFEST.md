# Beta RC1 — Installer Manifest

## Installer identity

| Property | Value |
|---|---|
| Display name | Salryn POS |
| Publisher | Kirjane Labs |
| Version | 0.1.0-beta-rc1 |
| AppId (Inno Setup GUID) | A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E |
| Output filename | `Salryn-POS-BetaRC1-Setup.exe` |
| Installer script | `installer/SalrynSetup.iss` |
| Build script | `tools/installer/Build-SalrynBetaRc1Installer.ps1` |

## Paths installed

| Path | Description |
|---|---|
| `C:\Program Files\Kirjane Labs\Salryn POS\` | Application install root |
| `C:\Program Files\Kirjane Labs\Salryn POS\Salryn.Desktop.WebView.exe` | WebView2 app shell — user entry point |
| `C:\Program Files\Kirjane Labs\Salryn POS\Salryn.Api.Local.exe` | Local API host |
| `C:\Program Files\Kirjane Labs\Salryn POS\dist\` | React production build |
| `C:\Program Files\Kirjane Labs\Salryn POS\assets\branding\` | Icon and banner assets |
| `C:\ProgramData\Salryn\` | Production data root (preserved on upgrade/uninstall) |
| `C:\ProgramData\Salryn\data\salryn.db` | Production database |
| `%LOCALAPPDATA%\Salryn\Licensing\license.json` | Host license file |

## Shortcuts

| Surface | Name | Target |
|---|---|---|
| Desktop (optional, offered at install) | Salryn POS | `Salryn.Desktop.WebView.exe` |
| Start Menu | `Kirjane Labs\Salryn POS\Salryn POS` | `Salryn.Desktop.WebView.exe` |

Icon for all shortcuts: `assets\branding\salryn-icon.ico`

## WebView2 Runtime (installer-managed)

| Property | Value |
|---|---|
| Strategy | Microsoft Edge WebView2 **Evergreen Bootstrapper** |
| Packaged file | `installer/redist/MicrosoftEdgeWebview2Setup.exe` |
| Official source | `https://go.microsoft.com/fwlink/p/?LinkId=2124703` |
| Silent flags | `/silent /install` |
| Detection | Registry `pv` under EdgeUpdate Clients `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` (HKLM WOW6432Node + HKCU) |
| Client action | None — Salryn setup installs WebView2 when missing |
| Internet | Required during install if runtime must be downloaded |
| Offline standalone bundling | Deferred |

## Staged content (packed into installer)

| Source path | Installed as |
|---|---|
| `Salryn.Desktop.WebView.exe` | `{app}\Salryn.Desktop.WebView.exe` |
| `Salryn.Api.Local.exe` | `{app}\Salryn.Api.Local.exe` |
| `*.dll` / `runtimes\` | `{app}\*.dll` / `{app}\runtimes\` |
| `dist\` | `{app}\dist\` |
| `assets\branding\salryn-icon.ico` | `{app}\assets\branding\salryn-icon.ico` |
| `assets\branding\salryn-icon.png` | `{app}\assets\branding\salryn-icon.png` |
| `assets\branding\salryn-banner-*.png` | `{app}\assets\branding\salryn-banner-*.png` |
| `installer/redist/MicrosoftEdgeWebview2Setup.exe` | `{tmp}` only (deleteafterinstall; not left in `{app}`) |
| `WebView2Loader.dll` | `{app}\WebView2Loader.dll` (required native loader) |
| `runtimes\win-*\native\WebView2Loader.dll` | `{app}\runtimes\win-*\native\WebView2Loader.dll` |

## Build prerequisites (build machine only)

| Prerequisite | Required for | End-user needs? |
|---|---|---|
| .NET SDK | Publish .NET projects | No |
| Node.js / npm | Build React dist | No |
| Branding assets in `assets/branding/` | Icon files | No |
| **Inno Setup 6** | **Compile setup EXE** | **No** |
| WebView2 Evergreen Bootstrapper | Packaged into setup | No (installer handles on client) |

ISCC discovery order:
1. `-InnoSetupCompilerPath` parameter
2. `INNO_SETUP_ISCC` environment variable
3. `ISCC.exe` on PATH
4. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`
5. `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
6. `C:\Program Files\Inno Setup 6\ISCC.exe`

## Staging artefacts (not shipped to clients)

| Path | Description |
|---|---|
| `artifacts/installer/staging/Salryn/` | Published app ready for packaging |
| `artifacts/installer/shell-publish/` | WebView2 publish temp |
| `artifacts/installer/setup/Salryn-POS-BetaRC1-Setup.exe` | Compiled setup EXE |
| `artifacts/installer/setup/Salryn-POS-BetaRC1-Setup.exe.sha256.txt` | SHA-256 of setup EXE |

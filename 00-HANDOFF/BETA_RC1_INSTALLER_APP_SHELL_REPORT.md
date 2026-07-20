# Beta RC1 — Installer + App Shell Report

## Branch

`KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

## Decision

`BETA_RC1_INSTALLER_APP_SHELL_REPAIRED_AND_PASSED_WEBVIEW2_LOADER`

---

## Root cause of WebView2 “missing or damaged” dialog

The system **already had** Microsoft Edge WebView2 Runtime:

| Check | Result |
|---|---|
| Registry `pv` (HKLM WOW6432Node EdgeUpdate Clients `{F3017226-…}`) | `143.0.3650.139` |
| `CoreWebView2Environment.GetAvailableBrowserVersionString()` (probe) | `143.0.3650.139` |

The installed Salryn shell still showed the missing-runtime dialog because **`WebView2Loader.dll` was not packaged** into the installer staging/install folder.

**Root cause:** missing `WebView2Loader.dll` in installed shell files — **not** a missing OS WebView2 runtime.

Cause in `Build-SalrynBetaRc1Installer.ps1`:

1. Shell publish was merged with **file-only** copy (`Get-ChildItem -File`), dropping `runtimes\…\WebView2Loader.dll`.
2. Later recursive directory copy could **nest** `runtimes\runtimes` instead of merging native loaders next to SQLite natives.

Without `WebView2Loader.dll`, `GetAvailableBrowserVersionString()` throws (e.g. `DllNotFoundException`), which the shell treated as “runtime missing.”

### Fix

- Merge shell publish with `Copy-Item (Join-Path $shellTemp '*') … -Recurse`.
- Explicitly stage `WebView2Loader.dll` under `runtimes\win-x64\native\` **and beside** `Salryn.Desktop.WebView.exe`.
- Fail the build if the loader is missing.
- Keep Evergreen Bootstrapper install when registry `pv` is absent.
- Installed-layout API launch: start adjacent `Salryn.Api.Local.exe` (not only `dotnet run` in a repo).
- Installed-layout web URL: `http://127.0.0.1:5299/`.

---

## WebView2 strategy

| Item | Value |
|---|---|
| Strategy | Evergreen Bootstrapper + ship native `WebView2Loader.dll` |
| Bootstrapper path | `installer/redist/MicrosoftEdgeWebview2Setup.exe` |
| Official source | `https://go.microsoft.com/fwlink/p/?LinkId=2124703` |
| Bootstrapper SHA-256 | `23a55fbff920c0f99887848cfc25125f8f915df35638e01beb8f8fa9b5a0bc51` |
| Silent flags | `/silent /install` |
| Runtime detection (installer) | Registry `pv` under EdgeUpdate Clients GUID |
| Loader packaging | `{app}\WebView2Loader.dll` + `{app}\runtimes\win-*\native\WebView2Loader.dll` |
| Internet | Required during install only if runtime must be downloaded |

---

## Installer EXE

| Property | Value |
|---|---|
| Path | `artifacts\installer\setup\Salryn-POS-BetaRC1-Setup.exe` |
| Compiled | Yes |
| SHA-256 | `a764b47fcade1e1465860c00167e93fca670402f7fcfd90ff8a71457769acb1c` |
| ISCC | `C:\Users\kirch\AppData\Local\Programs\Inno Setup 6\ISCC.exe` |

---

## Verified installed-app QA proof

### Install / launch

| Check | Result |
|---|---|
| Setup EXE compiled | PASS |
| Install path | `C:\Program Files\Kirjane Labs\Salryn POS` |
| Desktop shortcut | `C:\Users\Public\Desktop\Salryn POS.lnk` exists |
| Launch | Opens as **Salryn POS** desktop window |
| Browser required | No — Firefox/Chrome not required |
| Localhost typing required | No |
| WebView2 missing dialog | Fixed (no longer shown) |
| Crash during QA | None observed |

### WebView2Loader proof

| Check | Result |
|---|---|
| `WebView2Loader.dll` under install root | Present under `C:\Program Files\Kirjane Labs\Salryn POS` |
| Root cause class | Packaging (loader missing), not OS runtime missing |

### Health proof

| Check | Result |
|---|---|
| Health endpoint | `status: ok`, `service: Salryn.Api.Local` |

### Login / dashboard / checkout

| Check | Result |
|---|---|
| Login | Works from installed app |
| Dashboard | Works from installed app |
| Checkout | Works from installed app |

**Note:** Dashboard totals observed during QA came from the existing local QA database at `C:\ProgramData\Salryn\data\salryn.db`. Those totals are **not** shipped seed data. The installer forbidden scan already passed; `salryn.db` must not be shipped in staging/install package.

### Receipt proof

| Check | Result |
|---|---|
| Receipt file format | Client-facing **SALES RECEIPT** format |
| GUID / developer receipt text | Not exposed |

---

## Gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| ISCC compile (`-BuildInstaller`) | PASS |
| Forbidden scan on staging | PASS (`salryn.db` not shipped) |
| Staging launch + health | PASS |
| Installed-app launch + health | PASS |
| Installed-app login / dashboard / checkout | PASS |
| Installed-app receipt (SALES RECEIPT) | PASS |

---

## Known P2

- Receipt cashier display may still show generic **"Cashier"** instead of the display name on some installed receipt paths. Acceptable for Beta RC1: does not expose a GUID and no longer shows developer receipt text.
- Offline WebView2 **standalone** bundling still deferred (Evergreen Bootstrapper needs internet when runtime absent).
- Setup EXE remains unsigned (SmartScreen).
- Framework-dependent publish still needs .NET 10 on the client machine.

---

## Protected refs / promotion

- Protected refs: none touched.
- Promotion: not performed — push feature branch only.

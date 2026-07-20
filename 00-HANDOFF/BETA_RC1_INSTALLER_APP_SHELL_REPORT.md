# Beta RC1 — Installer + App Shell Report

## Branch

`KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

## Decision

`BETA_RC1_INSTALLER_APP_SHELL_REPAIRED_AND_PASSED`

---

## Root cause of WebView2 “missing or damaged” dialog

The system **already had** Microsoft Edge WebView2 Runtime:

| Check | Result |
|---|---|
| Registry `pv` (HKLM WOW6432Node EdgeUpdate Clients `{F3017226-…}`) | `143.0.3650.139` |
| `CoreWebView2Environment.GetAvailableBrowserVersionString()` (probe) | `143.0.3650.139` |

The installed Salryn shell still showed the missing-runtime dialog because **`WebView2Loader.dll` was not packaged** into the installer staging/install folder.

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
| SHA-256 | `a764b47fcade1e1465860c00167e93fca670402f7fcfd90ff8a71457769acb1c` |
| ISCC | `C:\Users\kirch\AppData\Local\Programs\Inno Setup 6\ISCC.exe` |

---

## Proof

### Install proof (Program Files)

Agent session could not complete an elevated silent reinstall (UAC canceled: "operation was canceled by the user").
**Kirch must run the new `Salryn-POS-BetaRC1-Setup.exe` as administrator** to replace the previous Program Files install that lacked `WebView2Loader.dll`.

### Launch proof (staging layout = packaged payload)

From `artifacts\installer\staging\Salryn` (includes `WebView2Loader.dll`, shell, API, `dist\`):

| Check | Result |
|---|---|
| Launch `Salryn.Desktop.WebView.exe` | Started |
| No early exit / no WebView2 missing dialog path | PASS |
| `http://127.0.0.1:5299/health` | `200 {"status":"ok","service":"Salryn.Api.Local"}` within ~1s |
| `http://127.0.0.1:5299/` | `200` (Salryn UI) |

### Health proof

PASS — see above.

---

## Gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS (run at end of lane) |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| ISCC compile (`-BuildInstaller`) | PASS |
| Forbidden scan on staging | PASS |
| Staging launch + health | PASS |

---

## Known P2

- Program Files reinstall requires admin/UAC; agent could not elevate in this session.
- Offline WebView2 **standalone** bundling still deferred (Evergreen Bootstrapper needs internet when runtime absent).
- Setup EXE remains unsigned (SmartScreen).
- Framework-dependent publish still needs .NET 10 on the client machine.

---

## Protected refs / promotion

- Protected refs: none touched.
- Promotion: not performed — push feature branch only.

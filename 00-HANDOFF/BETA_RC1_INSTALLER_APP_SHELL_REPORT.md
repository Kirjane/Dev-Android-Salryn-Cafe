# Beta RC1 — Installer + App Shell Report

## Branch

`KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

## Decision

`BETA_RC1_CASHIER_FALLBACK_FIXED_AND_INSTALLER_REBUILT`

---

## Cashier display-name fix (build repair)

`HostOperatorCheckoutApiService.SubmitAsync` gained `string? cashierDisplayName`. Call sites in:

- `tests/Salryn.Tests/PreIAuditTrailCoverageChecks.cs` → `"Audit Trail Cashier"`
- `tests/Salryn.Tests/PosT1FullStoreRuntimeTortureChecks.cs` → `"Torture Test Cashier"`

were updated to the new signature. Endpoint already passes session display name / username via `LocalHostCheckoutEndpoints.ResolveCashierDisplayName`.

POS-M1 receipt smoke asserts human cashier name, rejects generic `Cashier   : Cashier` when a display name was supplied, and rejects GUID / developer receipt wording.

---

## Local QA state reset

| Item | Value |
|---|---|
| Script | `tools/qa/Reset-SalrynLocalState.ps1` |
| Default | Stop Salryn processes; backup; reset DB + license/config |
| License path | `%LOCALAPPDATA%\Salryn\Licensing\` (moved into backup, not deleted) |
| DB path | `C:\ProgramData\Salryn\data\salryn.db` |
| Proof backup | `C:\ProgramData\Salryn\qa-state-backups\20260720-233751` |
| License reset | PASS (license.json + bak + installation-id moved) |
| Write test | PASS |
| Live `salryn.db` at run time | Already absent (prior QA backup copies only under `data\`) |

Usage:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\Reset-SalrynLocalState.ps1 -Force
```

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
| Compiled | Yes (rebuilt after cashier + reset script lane) |
| SHA-256 | `9f6e583ca5d3d8a4754b476849ed9590595535fc93aba19054d44ac278834d6c` |
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

**Note:** Dashboard totals observed during earlier QA came from the existing local QA database at `C:\ProgramData\Salryn\data\salryn.db`. Those totals are **not** shipped seed data. The installer forbidden scan already passed; `salryn.db` must not be shipped in staging/install package.

### Receipt proof

| Check | Result |
|---|---|
| Receipt file format | Client-facing **SALES RECEIPT** format |
| GUID / developer receipt text | Not exposed |
| Human cashier name | Proven via POS-M1 CoreSmoke (`POS-M1 Cashier`); session display name wired through host checkout |
| Generic `Cashier   : Cashier` when name supplied | Rejected by smoke asserts |

**Installed-app re-QA after this rebuild:** run elevated upgrade of the new setup EXE, then after `Reset-SalrynLocalState.ps1 -Force`, launch from desktop shortcut and confirm license/bootstrap + checkout receipt name.

---

## Gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt` | PASS |
| `dotnet run --project tools\Salryn.Tools.CoreSmoke` | PASS |
| `npm --prefix src\Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| ISCC compile (`-BuildInstaller`) | PASS |
| Forbidden scan on staging | PASS (`salryn.db` not shipped; `WebView2Loader.dll` present) |
| QA reset script | PASS (license moved; write test PASS) |
| Staging launch + health | PASS (prior) |
| Installed-app launch + health | PASS (prior) |
| Installed-app login / dashboard / checkout | PASS (prior) |
| Installed-app receipt (SALES RECEIPT) | PASS (prior) |

---

## Known P2

- Offline WebView2 **standalone** bundling still deferred (Evergreen Bootstrapper needs internet when runtime absent).
- Setup EXE remains unsigned (SmartScreen).
- Framework-dependent publish still needs .NET 10 on the client machine.
- Elevated reinstall of the newly rebuilt setup EXE on Kirch’s machine is the remaining manual step after this commit.

---

## Protected refs / promotion

- Protected refs: none touched.
- Promotion: not performed — push feature branch only.

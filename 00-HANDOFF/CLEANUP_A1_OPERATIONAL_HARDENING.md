# CLEANUP-A1 — Operational Hardening for Web Dist and Remote Errors

Implementation date: 2026-07-07

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch | `KIRCH-FREEHAND` |
| Pre-implementation HEAD | `121d6a8c2a3e3f3f193472bf422b521d399c640a` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| Compare before CLEANUP-A1 | `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` = `0 2` |
| Prior audits | `FULL_SYSTEM_A1_MASTER_REVIEW_AND_RELEASE_RISK_AUDIT.md`, `BACKEND_A1_ARCHITECTURE_AND_BAD_CODE_AUDIT.md` |
| Scope | Cleanup Sprint A only. No DB/RMO/licensing work. `KIRCH-SANDBOX` untouched. |

## What Was Fixed

1. **Static frontend dist diagnostics** — API startup logs resolved dist path, `index.html` mtime/size, hashed asset names, build stamp, and warnings when dist is missing or stale.
2. **SPA `index.html` cache hardening** — `index.html` and SPA fallback responses send `Cache-Control: no-cache, no-store, must-revalidate`, `Pragma: no-cache`, and `Expires: 0`. Hashed `/assets/*` files are unchanged.
3. **Frontend build marker** — `npm run build` writes `dist/salryn-build-stamp.txt` with UTC ISO timestamp. API reads it at startup and exposes it through diagnostics.
4. **Loopback-only diagnostic endpoint** — `GET /api/system/web-dist` is local-operator only via `LocalApiLocalAccessGuard`. No admin session required. No secrets exposed.
5. **Remote-facing error sanitization** — `RemoteReadOnlyApiService` and `RemotePosApiEndpoints` sanitize public error messages through `SanitizeRemoteClientMessage` / `SanitizePublicText`.
6. **Targeted smoke coverage** — `CleanupA1OperationalHardeningChecks` wired into CoreSmoke.

## How Stale Dist Is Detected

At API startup, `WebDistDiagnostics.Inspect()` reports:

- resolved `dist` path
- whether `index.html` exists
- `index.html` last-write UTC and size
- hashed JS/CSS asset names parsed from `index.html`
- `salryn-build-stamp.txt` UTC value when present
- warnings when dist/index/stamp is missing

Example startup log:

```text
[Salryn Web] Static root: C:\Users\kirch\Salryn\src\Salryn.App.Web\dist
[Salryn Web] index.html: exists=True, mtimeUtc=2026-07-06T17:18:30.4241460+00:00, sizeBytes=418
[Salryn Web] JS assets: /assets/index-Cmalk0Yk.js
[Salryn Web] CSS assets: /assets/index-CoAs8o3B.css
[Salryn Web] Build stamp UTC: 2026-07-06T17:18:30.705Z
```

Loopback operators can also query:

```http
GET http://127.0.0.1:5299/api/system/web-dist
```

Remote/LAN clients with bridge headers receive `403 LocalApi.LocalOperatorRequired`.

## How `index.html` Caching Is Handled

- `UseStaticFiles` applies no-cache headers only when the served file name is `index.html`.
- SPA `MapFallback` applies the same headers before serving `index.html` for client routes.
- Hashed bundles under `/assets/` keep normal static-file caching behavior.

Verified response headers on `http://127.0.0.1:5299/`:

- `Cache-Control: no-cache, no-store, must-revalidate`
- `Pragma: no-cache`
- `Expires: 0`

## Operator Runbook

### Rebuild frontend

```powershell
cd "C:\Users\kirch\Salryn\src\Salryn.App.Web"
npm run build
```

This regenerates `dist/`, including `salryn-build-stamp.txt`.

### Find process on port 5299

```powershell
cd "C:\Users\kirch\Salryn"
Get-NetTCPConnection -LocalPort 5299 -State Listen -ErrorAction SilentlyContinue |
  Select-Object LocalAddress,LocalPort,OwningProcess
```

### Kill old API process on 5299

```powershell
$Pids = Get-NetTCPConnection -LocalPort 5299 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty OwningProcess -Unique

$Pids | ForEach-Object { taskkill /PID $_ /F }
```

### Restart API

```powershell
cd "C:\Users\kirch\Salryn"
dotnet run --project .\src\Salryn.Api.Local\Salryn.Api.Local.csproj -- --development
```

Confirm startup `[Salryn Web]` lines show the expected dist path, mtime, asset names, and build stamp.

### Hard reload browser

At `http://127.0.0.1:5299`, press **Ctrl+F5** after rebuilding frontend or restarting API.

## Verification Commands and Results

Executed on `KIRCH-FREEHAND` during CLEANUP-A1 implementation:

| Command | Result |
| --- | --- |
| `git diff --check` | Passed |
| `dotnet build .\Salryn.sln --no-restore` | Passed — 0 warnings, 0 errors |
| `dotnet test .\Salryn.sln --no-build` | Passed — exit 0 |
| `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj` | Passed — `SALRYN Core smoke checks passed.` |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- lan-m16-rc-b-entry-boundary` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-pairing-gate` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-local-control` | Passed |
| `npm install` in `src\Salryn.App.Web` | Passed — 0 vulnerabilities |
| `npm run build` in `src\Salryn.App.Web` | Passed — stamp `2026-07-06T17:18:30.705Z` |

### Manual HTTP checks

| Check | Result |
| --- | --- |
| Startup log shows served dist path and build stamp | Passed |
| `GET /` returns `200` with no-cache headers | Passed |
| `GET /api/system/web-dist` on loopback returns dist diagnostics JSON | Passed |
| `GET /assets/index-*.js` returns `200` without `no-store` cache header | Passed |
| Host console at `127.0.0.1` | Not re-tested manually this pass; prior FULL-SYSTEM-A1 acceptance still valid |
| LAN Remote Client shell | Not re-tested manually this pass; infrastructure smokes passed |
| Remote Admin rejected | Not re-tested manually this pass; LAN/Remote POS smokes passed |

## Files Changed

| File | Change |
| --- | --- |
| `src/Salryn.Api.Local/WebDistDiagnostics.cs` | New dist inspection, startup logging, no-cache helper, API DTO |
| `src/Salryn.Api.Local/LocalApiApplicationFactory.cs` | Startup diagnostics, index no-cache, `/api/system/web-dist`, SPA fallback headers |
| `src/Salryn.Api.Local/Services/RemoteReadOnlyBridgeDiagnostics.cs` | `SanitizeRemoteClientMessage()` |
| `src/Salryn.Api.Local/Services/RemoteReadOnlyApiService.cs` | Sanitized remote-facing error messages |
| `src/Salryn.Api.Local/RemotePos/RemotePosApiEndpoints.cs` | Sanitized pairing/cart/shift error responses |
| `src/Salryn.App.Web/scripts/write-build-stamp.mjs` | New post-build stamp writer |
| `src/Salryn.App.Web/package.json` | Build script runs stamp writer |
| `tests/Salryn.Tests/CleanupA1OperationalHardeningChecks.cs` | New source/runtime sanitization checks |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Wire CLEANUP-A1 checks |
| `00-HANDOFF/CLEANUP_A1_OPERATIONAL_HARDENING.md` | This runbook |

## Remaining Non-Blockers

- Production installer/service lifecycle automation is still out of scope.
- Manual LAN/browser acceptance was not repeated end-to-end in this pass; existing LAN/Remote POS smokes remain green.
- `dist/` remains gitignored; operators must still rebuild after frontend source changes.
- DB-A1 integrity work, RMO implementation, and licensing remain future phases.

## Decision

| Item | Verdict |
| --- | --- |
| Cleanup Sprint A | **GO** — targeted operational hardening implemented and verified |
| DB-A1 after CLEANUP-A1 | **GO** — no new blockers introduced; proceed when Kirch authorizes |
| Production/RMO certification | **NO-GO** — unchanged |
| `KIRCH-SANDBOX` | **Untouched** |
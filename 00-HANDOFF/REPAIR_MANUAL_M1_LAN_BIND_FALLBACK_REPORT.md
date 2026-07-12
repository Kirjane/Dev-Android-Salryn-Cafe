# REPAIR-MANUAL-M1 — LAN Bind Fallback / Stale Dev DB Startup Stability

## 1. Branch / Base

Branch: `KIRCH-REPAIR-MANUAL-M1-LAN-BIND-FALLBACK`

Base branch: `KIRCH-HARDEN-A10-LANE-REGISTER-CLEANUP`

Base SHA: `3784a05f5e3e4d1322f00c166e007c1457a836dc`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the Freehand final response or repository branch head after this report commit for the exact final SHA.

## 2. Scope

Repair milestone: `REPAIR-MANUAL-M1 — LAN Bind Fallback / Stale Dev DB Startup Stability`

This is a narrow repair for manual proof blocker `MP-M1-002 Stale LAN bind crashes app startup`.

This lane does not continue manual proof, start beta, start Cloud, implement installer/update behavior, create a dev launcher, promote branches, or change RMO/reporting/licensing semantics.

`MP-M1-001 Fresh DB has no usable first-run setup/admin route` remains out of scope and unfixed.

## 3. Files changed

- `src/Salryn.Api.Local/Services/LocalApiLanUrlResolver.cs`
- `tests/Salryn.Tests/V2LanBindTruthChecks.cs`
- `00-HANDOFF/REPAIR_MANUAL_M1_LAN_BIND_FALLBACK_REPORT.md`

No database migration, launcher, installer, frontend, RMO, reporting, licensing, protected branch, or branch promotion change was made.

## 4. Root cause

Manual Proof M1 exposed this failure mode:

1. Development DB had a stale persisted LAN bind IP, previously observed as `192.168.1.161`.
2. Current network changed; current host IP was observed by Sol as `192.168.254.144`.
3. Startup added stale explicit LAN URL to Kestrel listen URLs.
4. Kestrel crashed before login with `System.Net.Sockets.SocketException (10049): The requested address is not valid in its context.`
5. When a startup explicit bind override was set to `192.168.254.144`, existing explicit-startup validation correctly failed if that IP was not actually assigned to a local interface.

Source root cause:

- `LocalApiApplicationFactory` loads persisted LAN config, then passes `LocalApiLanUrlResolver.ResolveListenUrls(localUrl, lanConfig)` into `builder.WebHost.UseUrls(...)`.
- `LocalApiLanUrlResolver` already checked remote transport state, explicit bind mode, general config validity, and forbidden/wildcard addresses.
- It did not check that the persisted explicit bind IP is currently assigned to a local interface before adding `http://{bindAddress}:{port}`.
- `LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(...)` already existed and is used by explicit startup/save validation.

## 5. Exact bug fixed

Fixed: stale persisted explicit LAN bind is no longer added to Kestrel listen URLs when the address is not currently assigned to a local network interface.

Implementation:

```csharp
string bindAddress = config.BindAddress.Trim();
if (!LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(bindAddress))
{
    return urls;
}

int port = ExtractPort(defaultUrl) ?? 5299;
urls.Add($"http://{bindAddress}:{port}");
```

This keeps loopback/default URL seeded first and returns that loopback-only list when the persisted explicit bind is stale.

## 6. Persisted stale bind behavior before / after

Before:

- persisted stale explicit LAN bind could be appended to Kestrel listen URLs
- Kestrel could receive an IP not valid for the current host
- app startup could crash before login
- manual proof could not begin using the old dev DB

After:

- persisted explicit LAN bind must still pass existing config/forbidden-address checks
- persisted explicit LAN bind must additionally be assigned to a current local interface
- stale persisted bind degrades to loopback-only startup
- app should continue starting on `http://127.0.0.1:5299`
- remote LAN exposure is not enabled for the stale address
- persisted config is not silently destroyed or rewritten

## 7. Explicit startup bind behavior before / after

Explicit startup bind behavior is intentionally unchanged.

If the operator explicitly requests a LAN bind through startup args/env such as `SALRYN_LAN_BIND_IP`, `LocalApiApplicationFactory` still calls `LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind(...)` and fails fast if the requested IP is not assigned to a local network interface.

Reason:

- explicit startup bind is an operator-directed action
- failing fast is clearer than silently ignoring an explicitly supplied invalid bind
- this repair only softens stale persisted DB config, not explicit operator commands

## 8. Security boundaries preserved

Preserved:

- no wildcard `0.0.0.0` bind
- no automatic bind to all interfaces
- no automatic public LAN exposure
- no loopback treated as valid LAN client bind
- no silent LAN enablement on unknown/stale address
- no paired-terminal rule bypass
- no Remote POS behavior expansion
- no multi-machine LAN implementation
- no LAN config rewrite/migration

## 9. Tests added / updated

Updated existing source-boundary smoke:

- `tests/Salryn.Tests/V2LanBindTruthChecks.cs`

Added assertions that:

- resolver always seeds loopback/default URL first
- resolver returns loopback-only when remote transport is disabled
- resolver returns loopback-only when bind mode is not explicit
- resolver rejects wildcard/forbidden persisted bind addresses
- resolver calls `LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(bindAddress)` before adding LAN URL
- resolver degrades to `return urls;` rather than throwing on stale persisted bind
- resolver only adds `http://{bindAddress}:{port}` after safety checks

No environment-sensitive test depending on the current Wi-Fi IP was added.

No focused new CoreSmoke runner was added. The existing `v2-lan-bind-truth` runner should cover the updated source-boundary assertions when full CoreSmoke or the focused V2 runner is run locally.

## 10. Commands / gates run

No local PowerShell commands were run by this ChatGPT connector session.

Required local gates still need Sol/Codex/local verification:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-REPAIR-MANUAL-M1-LAN-BIND-FALLBACK
git status
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-INSTALLER-M1

dotnet restore Salryn.sln
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m3-local-enforcement-boundaries
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only harden-a1-a6-pre-beta-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-lan-bind-truth
dotnet run --project tools/Salryn.Tools.CoreSmoke
auto npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

Note: the required user-provided gate list includes `npm --prefix src/Salryn.App.Web run build`; the accidental `auto` prefix above is not a command and must not be copied. Use the corrected command below:

```powershell
npm --prefix src/Salryn.App.Web run build
```

Connector actions performed:

- Created branch `KIRCH-REPAIR-MANUAL-M1-LAN-BIND-FALLBACK` from A10 SHA `3784a05f5e3e4d1322f00c166e007c1457a836dc`.
- Inspected `LocalApiLanUrlResolver`, `LocalApiNetworkInterfaceValidator`, `LocalApiApplicationFactory`, and `V2LanBindTruthChecks`.
- Updated resolver fallback behavior.
- Updated existing source-boundary smoke checks.
- Added this report.

## 11. Gate results

Local gates: **not run here**.

Manual launch: **not run here**.

No GREEN claim is made by this connector session.

## 12. Manual launch verification instructions

Use the restored old DB with stale LAN bind still present if possible.

```powershell
cd C:\Users\kirch\Salryn

Remove-Item Env:\SALRYN_LAN_BIND_IP -ErrorAction SilentlyContinue
Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
Remove-Item Env:\DOTNET_URLS -ErrorAction SilentlyContinue

$env:SALRYN_APP_ENV = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run --project src\Salryn.Api.Local\Salryn.Api.Local.csproj --launch-profile "Salryn.Api.Local Development"
```

Expected after repair:

- app starts on `http://127.0.0.1:5299`
- no `SocketException 10049`
- no `InvalidOperationException` from stale persisted LAN bind
- LAN remote transport is not exposed on the stale address
- stale config remains visible for operator repair rather than being silently deleted

## 13. Remaining issue

Still unfixed and out of scope:

- `MP-M1-001 Fresh DB has no usable first-run setup/admin route.`

This repair does not add first-run setup, admin bootstrap, seeded user credentials, launcher flow, or login UX changes.

## 14. Required report answers

| Question | Answer |
| --- | --- |
| Did stale persisted LAN bind still crash startup? | Expected no after local manual launch proof; connector session did not launch app. |
| Does app still start loopback? | Expected yes; resolver always seeds `defaultUrl` before LAN checks. |
| Did you enable wildcard LAN bind? | No |
| Did you auto-enable LAN exposure? | No |
| Did you change Remote POS behavior? | No |
| Did you fix first-run setup? | No; out of scope |
| Did you create launcher/installer/promote branch? | No |

## 15. Non-claims

This lane does not claim:

- GREEN local verification
- manual launch success
- manual proof continuation
- first-run setup/admin fix
- beta readiness
- Cloud readiness
- installer/update readiness
- dev launcher readiness
- branch promotion
- RMO/reporting/licensing semantic change
- multi-machine LAN implementation
- production release readiness

## 16. Status

REPAIR-MANUAL-M1 is a connector-authored candidate only until Sol/Codex/local verification proves:

- changed files and remote branch truth
- protected refs unchanged
- `dotnet restore/build`
- required CoreSmoke lanes including `v2-lan-bind-truth`
- frontend build
- `git diff --check` / `git status`
- manual launch using the stale old DB

After acceptance, Manual Proof M1 may restart from this repaired branch or a later accepted consolidation branch. Do not resume manual proof inside this repair lane.

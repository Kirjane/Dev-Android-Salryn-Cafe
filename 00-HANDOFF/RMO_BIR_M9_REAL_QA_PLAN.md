# RMO-BIR-M9 Real QA Plan

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`

**Disclaimer:** Engineering QA only. Does not prove BIR compliance or authorize official issuance.

---

## A. Migration QA

| Check | Expectation |
|-------|-------------|
| Clean DB migrate to v14 | Schema includes `official_dry_run_packages`; version = 14 |
| Old DB v12 → v14 | Upgrade applies v13 + v14 without data loss of sales |
| Old DB v13 → v14 | Only dry-run package table added |
| Future DB v15 | Rejected by DB-A2 future probe (`futureVersion = 15`) |
| Backup safety check | Pre-migration backup path still enforced by DB-A2 rules |
| No data loss | Setup/profile/series/dry-run tables intact |

PowerShell (dev machine, adjust paths):

```powershell
cd "C:\Users\kirch\Salryn"
# Prefer automated CoreSmoke + DbA2 gates:
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj -- --only rmo-bir-endgame
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj -- --only rmo-bir-full-closure
```

---

## B. Backup / restore QA

1. Create internal dry-run package (synthetic and, if available, from-sale).  
2. Create host backup.  
3. Restore to a copy path.  
4. Verify: package rows survive; official setup survives; sequence `current_number` unchanged; operational sales unchanged.

Manual notes: use existing host backup UI/service; compare `official_dry_run_packages` count and sequence table before/after.

---

## C. Source-sale read-only QA

1. Complete a normal operational sale (host checkout).  
2. Note: sale id, receipt no, status, sequence current number, EJ count, inventory qty, shift state.  
3. `POST /api/official/dry-run/packages/from-sale` with sale id (loopback + auth).  
4. Verify package `sourceMode = OperationalSaleReadOnly`, flags all non-mutating.  
5. Re-read sale / sequence / EJ / inventory / shift — **unchanged**.

```powershell
# Example shape only — host must be running on loopback with session cookie:
# Invoke-RestMethod -Method POST -Uri "http://127.0.0.1:<port>/api/official/dry-run/packages/from-sale" `
#   -ContentType "application/json" -WebSession $session `
#   -Body (@{ saleId = "<guid>" } | ConvertTo-Json)
```

---

## D. UI QA (Official Setup page)

| Step | Expect |
|------|--------|
| Page loads | No crash |
| Readiness | Loads; live posting false |
| Numbering preview | Loads; no allocate |
| Integrity card | Loads |
| Spine card | Loads |
| Dry-run package card | Loads; build package works |
| Source-sale section | Present if implemented; read-only wording |
| Internal reports | Watermark visible |
| Legal closure status | Unresolved / incomplete messaging |
| Forbidden text | No: compliant, accredited, certified, BIR ready, RMO ready, issue/print official receipt, activate compliance, go live |

---

## E. Installer impact QA

| Note | Detail |
|------|--------|
| ProgramData DB | Host DBs on v13 must migrate to v14 for dry-run packages |
| Installer branch | `KIRCH-INSTALLER-M1` **not tested** in this task |
| Installer code | **No installer code changes** in M9 |

---

## F. Regression gates (automated)

```powershell
cd "C:\Users\kirch\Salryn"
git diff --check

dotnet build .\tests\Salryn.Tests\Salryn.Tests.csproj /p:BuildProjectReferences=false
dotnet build .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj /p:BuildProjectReferences=false

dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-official-readiness
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-official-setup
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-official-numbering
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-official-integrity
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-official-spine
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-endgame
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only rmo-bir-full-closure

dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-lan-bind-truth
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-lan-client-pairing
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-terminal-lifecycle
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-host-lan-control-ux
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-host-shift
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-host-checkout
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-react-checkout

dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build

cd ".\src\Salryn.App.Web"
npm run build
```

---

## G. Source-claim guards

Source gates must fail if docs/web/source introduce forbidden claims: `compliant`, `accredited`, `certified`, `BIR ready`, `RMO ready` (UI must not use RMO token per prior WEB-A4).

---

## Pass criteria for M9

- All listed CoreSmoke RMO + V2 gates pass  
- Full CoreSmoke pass  
- npm build pass  
- Installer lane untouched  
- Sandbox untouched  
- No checkout/LAN official live wiring  

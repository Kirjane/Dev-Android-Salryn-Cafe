# POS-SURFACE3 Stacked Surface Cleanup Report

Date: 2026-07-15

Branch: `KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

Starting SHA: `13e77abeb7cd67f5bddc75cec1e35b0780514745`

Final SHA: populated by final assistant response after this report commit.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Scope

This stack completes the surface cleanup before SOL verification.

The lane changed only frontend/product-surface copy, visibility, route-gating posture, and source guards. It did not implement RMO/BIR behavior, official receipts, official invoices, eSales transmission, Backup UI, licensing final, installer, beta, or PRINTER1 adapter behavior.

---

## Files changed

- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx`
- `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`
- `tests/Salryn.Tests/PosR1OwnerReportsChecks.cs`
- `tests/Salryn.Tests/PosManual1FrontendRepairChecks.cs`
- `tests/Salryn.Tests/PosManual1R3FrontendSafetyUxRepairChecks.cs`
- `00-HANDOFF/POS_SURFACE3_STACKED_SURFACE_CLEANUP_REPORT.md`

No PRINTER1 source file was edited.

No protected ref was touched.

---

## PASS 1 findings

### Dashboard / reports

- `owner-reports` route is stable and should remain stable to avoid unnecessary route churn.
- Dashboard already uses `fetchOwnerReportsSummary` and is the right owner home summary.
- The old `Owner Reports` label made the drilldown page feel like a competing owner dashboard.
- The correct product framing is: Dashboard = owner home summary; Detailed Reports = report drilldown.

### Advanced tab

- `Host LAN Control` is valid host/admin tooling but not client-ready or LAN entitlement-ready.
- `Official Setup` and `Traditional Reporting` are high claim-risk surfaces and should not appear in standard navigation.
- `Production Reports` contains an artifact/export foundation, but the nav/page title sounds production-ready.
- Route implementations can remain because backend work is preserved for future compliance/reporting lanes.

### Permissions

- Cashier remains limited to checkout and sales.
- Manager should keep Dashboard/Detailed Reports operational access, but should not directly access compliance/report artifact lab routes in this stack.
- Admin remains the role that can reach internal host/admin surfaces when permissions allow.

### Tests/source guards

- Existing R1/MANUAL1/R3 source guards expected old `Owner Reports` wording and needed alignment.
- The R3 source guard was the best place to assert standard-nav cleanup without creating a new brittle CoreSmoke target.

---

## Code changes

### Dashboard / Detailed Reports

- Sidebar label changed from `Owner Reports` to `Detailed Reports` while keeping route id `owner-reports`.
- Dashboard summary copy changed to `Owner home summary`.
- Dashboard action changed to `Open Detailed Reports`.
- Dashboard low-stock summary now says detailed tables stay in `Detailed Reports`.
- `OwnerReportsPage.tsx` title changed to `Detailed Reports`.
- Detailed Reports subtitle now states it is read-only sales, stock, cashier, category, and void summaries, and that Dashboard remains the owner home summary.
- Detailed Reports boundary copy remains operational-only, non-official, and read-only.

### Advanced tab cleanup

- `Official Setup` was removed from standard navigation.
- `Traditional Reporting` was removed from standard navigation.
- `Production Reports` was renamed in nav to `Report Artifacts`.
- `ProductionReportsPage.tsx` visible title changed to `Report Artifacts`.
- `ProductionReportsPage.tsx` copy now says internal report dataset/native PDF artifact proof only; not legal submission, not eSales production transmission, and not BIR approval.
- `TraditionalReportingPage.tsx` title changed to `Internal Compliance Reporting Lab`.
- Traditional reporting copy now says internal X/Z/EJ/eSales draft tools only, not client-facing, no external submission, authoritative BIR schema unconfirmed, manual acceptance pending, and compliance validation pending.
- Z-reading button was softened to `Run internal Z-reading finalize test`.
- EJ/eSales buttons were softened to internal build language.

### LAN coming soon preserved

- Host LAN remains visible in Advanced.
- AppShell retains the `Soon` badge on Host LAN Control.
- Advanced copy keeps `Host LAN Control is Coming Soon / internal preview only; not client-ready`.
- Dashboard LAN card still says `Coming Soon / internal preview` and states LAN sync, multi-terminal production use, licensing entitlement, and client deployment remain deferred.

### Permission posture

- `managerPages` no longer includes `traditional-reporting` or `production-reports`.
- `official-setup`, `traditional-reporting`, and `production-reports` remain implemented routes with permission checks, but they are not normal manager navigation surfaces.
- Cashier pages remain limited to checkout and sales.

---

## PASS 2 adversarial findings

### A. Broken nav/icon mapping

- `Detailed Reports` has an explicit `BarChart3` icon mapping.
- `Report Artifacts` has an explicit `BarChart3` icon mapping.
- Old label mappings are retained as harmless compatibility fallback, but standard nav no longer emits those labels.

### B. Broken route/page id

- `owner-reports`, `official-setup`, `traditional-reporting`, and `production-reports` route IDs remain unchanged.
- `AppPage` and `implementedPages` remain valid.
- No route implementation was deleted.

### C. Broken permission gating

- Cashier is still limited to checkout and sales.
- Manager keeps operational pages and `owner-reports` only.
- Compliance/report-artifact lab routes are not included in manager direct page allowlist.

### D. False claims

- Standard navigation no longer contains `Official Setup`, `Traditional Reporting`, or `Production Reports` labels.
- LAN is marked Coming Soon/internal preview.
- Report Artifacts copy denies BIR approval, eSales production transmission, and legal submission.
- Traditional reporting copy denies client-facing/external submission posture and marks the surface as internal/compliance foundation.

### E. RMO/BIR leak

- Backend routes are preserved, but the normal UI navigation no longer presents Official Setup or Traditional Reporting as ordinary production features.
- Report artifact UI remains visible only as an internal artifact proof surface with explicit no-claim copy.

### F. Dashboard duplication

- Dashboard is now owner home summary.
- Detailed Reports is now drilldown only.
- The two surfaces no longer compete as owner dashboards.

### G. PRINTER1 untouched

- No receipt printer adapter, printer service, printer settings, print/reprint endpoint, or PRINTER1 test was edited.

---

## Repairs after PASS 2

- Removed trailing whitespace introduced during the first Traditional Reporting copy pass to reduce `git diff --check` risk.
- Updated R1/MANUAL1/R3 source guards to require `Detailed Reports` and reject the old `Owner Reports` nav label.
- Updated R3 source guard to require Official Setup and Traditional Reporting hidden from standard nav, require Report Artifacts nav label, preserve LAN coming-soon copy, and reject false readiness claims.

---

## RMO/BIR/reporting boundary

Safe now:

- Dashboard owner summary and Detailed Reports operational drilldown.
- Host LAN marked Coming Soon / internal preview.
- Report Artifacts framed as internal export proof.
- Compliance/reporting foundations preserved for future work.

Still not claimable:

- BIR ready
- official receipt ready
- official invoice ready
- eSales production ready
- compliance guaranteed
- beta ready
- installer ready
- licensing final
- LAN production ready
- multi-terminal production ready

---

## Deferred items

- Backup & Restore UI
- LIC-FINAL offline license and LAN entitlements
- LAN production/client acceptance
- ESC/POS, cash drawer, cutter, and thermal profiles
- official/BIR output
- eSales production transmission
- installer/I1 proof
- private beta

---

## Gates

Local gates were not run from this chat.

Required SOL/local gate block:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY
git reset --hard origin/KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY

git rev-parse HEAD
git status

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-printer1-windows-printer-wiring
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-r3-frontend-safety-ux-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-r1-owner-reports
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-surface1-role-page-lock-store-nav
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gate status:

- build: `NOT RUN / NEEDS SOL`
- PRINTER1: `NOT RUN / NEEDS SOL`
- R3: `NOT RUN / NEEDS SOL`
- R1: `NOT RUN / NEEDS SOL`
- SURFACE1: `NOT RUN / NEEDS SOL`
- new surface target: `NOT ADDED`
- web build: `NOT RUN / NEEDS SOL`
- diff check: `NOT RUN / NEEDS SOL`
- git status: `NOT RUN / NEEDS SOL`

No `CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING` claim is made.

---

## Protected refs

- `KIRCH-SANDBOX` was not touched.
- `KIRCH-INSTALLER-M1` was not touched.

---

## Branch pushed

Yes. Connector writes were made to:

`KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

---

## Final status

`READY_FOR_SOL_VERIFICATION`

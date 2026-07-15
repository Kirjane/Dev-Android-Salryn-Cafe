# POS-SURFACE4 Hardware Scope Ban Report

Date: 2026-07-15

Branch: `KIRCH-POS-SURFACE4-HARDWARE-SCOPE-BAN`

Base branch: `KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

Base SHA: `e8db40854a32c1e247862f01828e40ebd23f2b43`

Final SHA: populated by final assistant response after this report commit.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Mission

Lock hardware extras out of beta/release scope.

This is not a future-feature planning lane. It bans active beta-roadmap drift for cash drawer, drawer kick, cutter, ESC/POS, thermal printer profile, hardware receipt width/profile support, thermal receipt formatting, and hardware bundle/support claims.

Allowed current printer scope remains Windows print queue internal receipt printing only.

---

## Files changed

- `src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx`
- `tests/Salryn.Tests/PosPrinter1WindowsPrinterWiringChecks.cs`
- `tests/Salryn.Tests/PosManual1FrontendRepairChecks.cs`
- `01-FEATURE-LOCK/FEATURE_LOCK.md`
- `02-ARCHITECTURE/ARCHITECTURE_LOCK.md`
- `06-HARDWARE/HARDWARE_LOCK.md`
- `09-DEPLOYMENT/DEPLOYMENT_LOCK.md`
- `10-CLIENT-CUSTOMIZATION/CUSTOMIZATION_LOCK.md`
- `START-HERE.txt`
- `00-HANDOFF/RMO_BIR_REPORTING_ENGINE_DECISION_MATRIX.md`
- `00-HANDOFF/POS_SURFACE3_STACKED_SURFACE_CLEANUP_REPORT.md`
- `00-HANDOFF/POS_SURFACE4_HARDWARE_SCOPE_BAN_REPORT.md`

No PRINTER1 behavior file was changed.

No RMO/BIR implementation was changed.

No eSales implementation was changed.

No Backup UI, LIC-FINAL, I1, installer, USER-M1 repair, or protected ref was touched.

---

## PASS 1 findings

### Where hardware drift wording existed

- `StoreSettingsPage.tsx` exposed receipt width as 58/80 and described thermal-device/accessory controls as deferred. Risk: HIGH because it looked like hardware profiles and accessories were future beta/release work.
- `PosPrinter1WindowsPrinterWiringChecks.cs` guarded the old deferred wording instead of the new scope-ban policy. Risk: HIGH because the test would preserve drift.
- `01-FEATURE-LOCK/FEATURE_LOCK.md` described final build direction as ESC/POS and listed receipt width settings and ESC/POS/LAN printer support as must-have. Risk: HIGH as an old planning lock could be misread as active release scope.
- `02-ARCHITECTURE/ARCHITECTURE_LOCK.md` described printer architecture as ESC/POS / LAN-first and hardware-aware. Risk: HIGH for release scope interpretation.
- `06-HARDWARE/HARDWARE_LOCK.md` listed ESC/POS printer, cash drawer future pulse command, and 58mm/80mm printer setup. Risk: HIGH.
- `09-DEPLOYMENT/DEPLOYMENT_LOCK.md` listed ESC/POS thermal receipt printer, receipt width setup, and printer tests as client deployment assumptions. Risk: HIGH.
- `10-CLIENT-CUSTOMIZATION/CUSTOMIZATION_LOCK.md` listed receipt width customization. Risk: MEDIUM/HIGH.
- `START-HERE.txt` described the hardware folder as containing printer/scanner/cash drawer notes. Risk: MEDIUM.
- `RMO_BIR_REPORTING_ENGINE_DECISION_MATRIX.md` treated ESC/POS/cash drawer/cutter/thermal profiles as hide/defer, not out-of-beta-scope. Risk: MEDIUM.
- `POS_SURFACE3_STACKED_SURFACE_CLEANUP_REPORT.md` listed ESC/POS, cash drawer, cutter, and thermal profiles as deferred items. Risk: MEDIUM.

### Where hardware was already correctly bounded

- `WindowsQueueReceiptPrinterAdapter.cs` is Windows print queue only and uses `notepad.exe /pt`; no ESC/POS, cutter, cash drawer, or hardware profile behavior was found in that adapter.
- `ReceiptPrinterAdapterFactory.cs` selects only Disabled or WindowsPrinter modes.
- `PosReceiptPrintService.cs` prints persisted internal receipt snapshots/test samples through the adapter boundary and does not know about cash drawer, cutter, or ESC/POS.
- `ProductionReportsPage.tsx` already denied printer integration for report artifacts.
- Surface3 Dashboard/AppShell LAN copy did not introduce hardware peripheral claims.

### Risk level

Overall risk before Surface4: HIGH, because old planning docs and Store Settings copy could reintroduce hardware-roadmap drift even though core PRINTER1 behavior was Windows-queue-only.

---

## Implementation

### Store Settings

Enforced policy:

- `Windows print queue only`.
- `Hardware peripherals beyond the Windows print queue are outside the beta scope.`
- `No ESC/POS, no cutter, no cash drawer, no thermal receipt profile, and no hardware bundle/support claim.`
- Receipt hardware width/profile support is outside beta scope.

Removed beta-facing 58/80 receipt-width selector from Store Settings UI. The existing internal field remains preserved in the settings object for compatibility, but it is not exposed as a hardware profile release feature.

### Tests/source guards

Updated `PosPrinter1WindowsPrinterWiringChecks.cs` to guard:

- Windows print queue only scope.
- hardware peripherals outside beta scope.
- explicit no ESC/POS/cutter/cash drawer/thermal profile copy.
- no supported hardware-claim strings.

Updated `PosManual1FrontendRepairChecks.cs` to guard:

- inactive logo copy after Store Settings copy change.
- hardware scope ban copy.
- no `hardware update later` or `next hardware update` drift in operator pages.

No new CoreSmoke target was added. Existing targeted PRINTER1 and MANUAL1/R3 gates are the lower-risk verification path because they already run frontend/source guards.

### Docs/reports

Replaced old planning lock language in:

- feature lock
- architecture lock
- hardware lock
- deployment lock
- customization lock
- start-here
- RMO/BIR decision matrix
- Surface3 stacked report

The updated docs classify hardware extras as outside beta/release scope, not future beta work.

---

## PASS 2 findings

### Drift risk

- No active roadmap should list cash drawer, cutter, ESC/POS, thermal profile, receipt hardware width/profile, or hardware bundle/support as future beta/release steps.
- Updated lock docs now say these are outside beta scope or not active roadmap items.
- Store Settings no longer exposes hardware receipt width/profile as an operator setting.

### Printer boundary

- PRINTER1 adapter behavior remains Windows queue only.
- No receipt printer adapter/service behavior was changed.
- No official receipt/BIR output claim was added.

### Beta boundary

- Hardware extras are not release blockers.
- Hardware extras are not beta features.
- Hardware extras are not next-stack items.

### Copy quality

- Operator-facing Store Settings copy is short and explicit.
- Planning docs are stricter than previous “deferred” wording.
- The policy is not phrased as coming soon, later, next, planned, or hardware update.

### Tests

- PRINTER1 source guard now enforces the hardware-scope ban without requiring a new brittle smoke target.
- MANUAL1 guard now rejects hardware-roadmap drift in operator pages.
- Existing PRINTER1/R3/R1/SURFACE1 gates still need SOL/local execution.

---

## Policy now enforced

Required policy:

`Hardware peripherals beyond the Windows print queue are outside the beta scope.`

Allowed current claim:

`Windows print queue internal receipt printing.`

Required exclusions:

- no cash drawer
- no cutter
- no ESC/POS
- no thermal width/profile support
- no hardware bundle/support claim
- no official/BIR output

---

## Current allowed printer scope

- Windows print queue only.
- Internal receipt only.
- Persisted receipt snapshot source.
- Valid Windows printer/manual test proof may prove only Windows print queue paper output on that host.
- Sales remain saved and unchanged on disabled/unavailable/failing printer paths.
- Reprint must not create duplicate sale, payment, stock movement, or receipt snapshot mutation.

---

## Banned from beta/release scope

- cash drawer
- drawer kick
- receipt cutter
- cut receipt command
- ESC/POS
- thermal printer profile
- hardware receipt width/profile support
- thermal receipt formatting
- hardware bundle/support claims

These are not active beta/release roadmap items and are not release blockers.

---

## Not touched

- PRINTER1 behavior
- RMO/BIR implementation
- eSales implementation/transmission
- Backup UI implementation
- LIC-FINAL
- I1 installer
- USER-M1 repair branch
- protected refs
- official receipt/invoice generation

---

## Gates

Local gates were not run from this chat.

Required SOL/local gate block:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-SURFACE4-HARDWARE-SCOPE-BAN
git reset --hard origin/KIRCH-POS-SURFACE4-HARDWARE-SCOPE-BAN

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
- surface4 guard: `NOT ADDED`; covered by updated PRINTER1/MANUAL1 source guards
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

Yes. Connector writes were made only to:

`KIRCH-POS-SURFACE4-HARDWARE-SCOPE-BAN`

---

## Final status

`READY_FOR_SOL_VERIFICATION`

# Salryn Scope Summary

Maintainer: Kirch Ivan Balite

## Current PRE-I stack truth

Current Salryn PRE-I candidate:

```text
Repository: Kirch-Nairu/Salryn
Branch: KIRCH-PRE-I-LAN-REACHABILITY-AND-TERMINAL-COPY-REPAIR
SHA: 373280da909a77277315364a7fa02d814af87291
```

Current LicensingOps candidate:

```text
Repository: Kirch-Nairu/Salryn.LicensingOps
Branch: KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF
SHA: f73bf74a6e68f835640d0ea3942f979f8fc823d9
```

Current next step:

```text
stale-doc repair + full local gates + final stack seal
```

This file supersedes older current-state wording that pointed to `KIRCH-SANDBOX`, `89e55b61748309d0d68d8d05627a01ef101649f2`, or `SANDBOX-RMO-FOUNDATION-09D` as the active lane. Those references are historical only when found in archived handoffs or older reports.

No package/I1, desktop shortcut/WebView2, installer, production activation, BIR/eSales work, or branch promotion is part of the current next step.

## Documentation policy going forward

Salryn should not create a new handoff document for every small move.

Documentation is reserved for certainty points:

- architecture boundaries that are considered stable,
- completed compliance-readiness milestones,
- release or pilot checkpoints,
- deployment decisions,
- externally visible behavior or client-facing claims.

Normal code phases should be represented by commits, tests, smokes, and this summary when the phase changes the stable scope.

## Product direction

Salryn is a local-first Windows POS and inventory system for small retail operations. The PRE-I stack is preparing the candidate for final review, stale-artifact cleanup, local gate verification, and final stack seal.

Current target shape:

```text
Single PC POS
-> local signed Host license
-> per-terminal signed LAN Terminal license
-> controlled Store Host / LAN operation
-> evidence-backed beta seal
```

Daily operation must not depend on the cloud. Cloud, VPN, remote mirror behavior, and production activation remain future options, not current requirements.

## Current repo structure

```text
src/Salryn.Core             domain models, contracts, services, business rules
src/Salryn.Infrastructure    SQLite, migrations, repositories, persistence services
src/Salryn.Api.Local         local HTTP/API host surface
src/Salryn.App.Web           React operator UI
src/Salryn.App.WinForms      legacy/native shell surface
src/Salryn.Desktop.WebView   desktop wrapper project, not part of this repair/seal task
tests/Salryn.Tests           architecture and behavior checks
tools/Salryn.Tools.*         smoke verification tools
00-COMPLIANCE                compliance-readiness notes and historical planning
00-HANDOFF                   historical handoffs plus current summaries and seal reports
```

## Completed scope summary

### 1. Core POS foundation

Implemented foundations include:

- product catalog and product search,
- product SKU/barcode contract repair,
- inventory read/write paths,
- stock movement tracking,
- cashier sessions,
- host checkout path,
- sales persistence,
- sales history and receipt preview foundations,
- reprint and void/reversal coverage where present,
- store settings,
- authentication and first-run bootstrap,
- operator role/page guards,
- audit trail and CSV export coverage,
- backup/restore surface and safety coverage,
- React operator shell and PRE-I usability repairs.

The UI is not treated as the source of truth. Business rules belong in Core, Infrastructure, API, and composition boundaries.

### 2. Local-first database foundation

Implemented foundations include:

- SQLite-backed repository layer,
- database initialization orchestration,
- schema migrations,
- development and production path separation,
- smoke tools for database and repository behavior.

Known dependency hygiene item:

- `SQLitePCLRaw.lib.e_sqlite3 2.1.11` NU1903 advisory remains intentionally untouched until a separate dependency hygiene slice.

### 3. Licensing and activation foundation

Implemented/accepted PRE-I licensing truth:

- current Salryn canonical license activation proof candidate is in the Salryn PRE-I stack,
- current LicensingOps cross-repo activation proof candidate is `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF` at `f73bf74a6e68f835640d0ea3942f979f8fc823d9`,
- canonical public-key fingerprint is current,
- old-key fixtures are historical only,
- one Host license and per-terminal Terminal licenses are the supported activation shape,
- no cloud activation requirement is claimed.

### 4. Store Host / LAN foundation

Implemented or scaffolded foundations include:

- `Salryn.Host.Local` project,
- local API host surface,
- host/client contract models,
- local host operation descriptors,
- product lookup and checkout paths routed through host-aware boundaries,
- terminal management and activation UX repairs,
- LAN reachability and terminal-copy repair.

Current boundary:

- each LAN terminal needs its own Terminal license,
- terminal link readiness comes from confirmed runtime listener truth,
- clients must not open shared SQLite over LAN,
- no installer or real installed-app proof is claimed by this summary.

### 5. RMO/BIR-certification-readiness backend foundation

Implemented foundations include:

- BIR machine profile schema,
- official document sequence schema,
- official checkout readiness gate,
- trusted product re-read before official completion,
- atomic official checkout persistence,
- official document snapshot evidence,
- electronic journal entries,
- ORIGINAL journal event,
- electronic journal hardening triggers,
- official evidence export,
- X-reading data foundation,
- Z-reading data foundation,
- half-open reading ranges `[start, end)`,
- void/cancel/correction audit events,
- official document lifecycle read model,
- official output source contract,
- reprint/copy audit rules,
- official printable document DTO foundation.

Current compliance wording:

```text
Salryn has a BIR/RMO certification-readiness backend foundation.
Salryn is not yet BIR-certified.
Salryn must not be described as fully RMO-compliant or eSales-ready until remaining report, rendering, backup, deployment, external review, and official submission work is completed outside this PRE-I seal task.
```

## What is intentionally not part of this task

The stale-doc repair and final-stack-seal lane must not add or claim:

- package/I1 completion,
- desktop shortcut/WebView2 installed-app proof,
- installer proof,
- production activation,
- BIR certification or legal accreditation,
- eSales submission/readiness,
- ESC/POS printing support,
- cash drawer/cutter/thermal hardware support,
- official hardware bundle or warranty,
- cloud activation requirement,
- unlimited terminals,
- branch promotion.

## Current best next step

```text
1. Repair stale current-state documentation.
2. Run the full local Salryn gates against the repaired branch.
3. Run the full local LicensingOps gates against the accepted LicensingOps candidate.
4. If clean, create KIRCH-PRE-I-FINAL-STACK-SEAL.
5. Add final stack seal report and final capability matrix.
```

## Required local gate baseline for seal

Salryn gate baseline:

```powershell
git status --short
git diff --check
dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-lan-reachability-and-terminal-copy-repair
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-shell-sidebar-collapse-pos-usability
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-product-sku-barcode-contract
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-canonical-license-activation-proof
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-license-key-rotation-canonical-beta
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-audit-csv-export
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-audit-trail-coverage
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-client-facing-product-polish
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-action-notification-system
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m4-fast-add-unknown-barcode-inventory
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m5-product-write-management
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m6-stock-in-stock-adjustment
dotnet run --project tools\Salryn.Tools.CoreSmoke
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

LicensingOps gate baseline:

```powershell
git status --short
git diff --check
npm run build
npm test
npm run test:licops-m3
npm run test:licops-m4
npm run test:licops-m5
npm run test:licops-m6
git diff --check
git status
```

## Stability rule

Do not create the final stack seal unless stale-doc repair is committed, local gates are clean, protected refs are unchanged, and P0/P1 blockers are absent.

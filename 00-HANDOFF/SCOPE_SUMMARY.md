# Salryn Scope Summary

Maintainer: Kirch Ivan Balite
Repository branch: `KIRCH-SANDBOX`
Current stable checkpoint: `89e55b61748309d0d68d8d05627a01ef101649f2`
Checkpoint label: Official printable document DTO foundation

This file is the current scope summary for Salryn. Older per-phase handoff files remain as archive history, but they are no longer the primary way to understand the project state.

## Documentation policy going forward

Salryn should not create a new handoff document for every small move.

Documentation is now reserved for certainty points:

- architecture boundaries that are considered stable,
- completed compliance-readiness milestones,
- release or pilot checkpoints,
- deployment decisions,
- externally visible behavior or client-facing claims.

Normal code phases should be represented by commits, tests, smokes, and this summary when the phase changes the stable scope.

## Product direction

Salryn is a local-first Windows POS and inventory system for small retail operations. The system starts as a single-machine desktop deployment and is being engineered toward a controlled Store Host model for future LAN clients.

Current target shape:

```text
Single PC POS
-> official evidence and reporting readiness
-> production hardening
-> Store Host / LAN
-> read-only mobile dashboard
```

Daily operation must not depend on the cloud. Cloud, VPN, and remote mirror behavior remain future options, not current requirements.

## Current repo structure

```text
src/Salryn.Core             domain models, contracts, services, business rules
src/Salryn.Infrastructure    SQLite, migrations, repositories, persistence services
src/Salryn.App.WinForms     desktop shell, composition root, WinForms UI
src/Salryn.Host.Local       local host contracts and passive host composition
 tests/Salryn.Tests         architecture and behavior checks
 tools/Salryn.Tools.*       smoke verification tools
00-COMPLIANCE               compliance-readiness notes and historical planning
00-HANDOFF                  historical handoffs plus this current summary
```

## Completed scope summary

### 1. Core POS foundation

Implemented foundations include:

- product catalog and product search,
- inventory read/write paths,
- stock movement tracking,
- cashier sessions,
- standard checkout path,
- sales persistence,
- sales history and receipt preview foundations,
- store settings,
- authentication and first-run bootstrap,
- WinForms shell and MVP screens.

The UI is not treated as the source of truth. Business rules belong in Core and Infrastructure boundaries.

### 2. Local-first database foundation

Implemented foundations include:

- SQLite-backed repository layer,
- database initialization orchestration,
- schema migrations,
- development and production path separation,
- smoke tools for database and repository behavior.

Known dependency hygiene item:

- `SQLitePCLRaw.lib.e_sqlite3 2.1.11` NU1903 advisory remains intentionally untouched until a separate dependency hygiene slice.

### 3. Store Host / LAN foundation

Implemented or scaffolded foundations include:

- `Salryn.Host.Local` project,
- host/client contract models,
- local host operation descriptors,
- passive runtime and composition holder,
- product lookup handler/gateway path,
- checkout product lookup routed through the local-host-aware boundary.

Current restriction:

- Local Host complete-sale is not implemented.
- No LAN transport is implemented.
- No socket or HTTP server is implemented.
- No client should open the SQLite file over LAN.
- The future Store Host must own SQLite writes.

### 4. RMO/BIR-certification-readiness backend foundation

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
Salryn must not be described as fully RMO-compliant until the remaining report, rendering, backup, deployment, and external review work is completed.
```

### 5. Official output pipeline

Current backend chain:

```text
Official checkout
-> trusted product re-read
-> official sequence allocation
-> sale / stock / journal transaction
-> official snapshot payload and hash
-> electronic journal entry and ORIGINAL event
-> evidence export
-> X-reading / Z-reading
-> lifecycle read model
-> output source contract
-> reprint/copy audit event
-> printable DTO
```

Current official output rule:

- Future receipts, invoices, reports, and Crystal Reports templates must consume controlled backend DTOs.
- They must not read raw SQLite tables directly.
- They must not interpret raw journal events directly.
- They must not use UI preview text as official evidence.
- They must not use `sales.receipt_snapshot` as the official output source.

## What is intentionally not done yet

The following are not current capabilities:

- BIR certification or legal accreditation,
- Crystal Reports package integration,
- `.rpt` templates,
- receipt/invoice rendering,
- ESC/POS printing,
- official X/Z printed report layouts,
- audit trail printed report layouts,
- post-Z adjustment policy,
- electronic journal preservation/export package,
- backup/restore proof,
- Store Host complete-sale,
- LAN transport,
- terminal pairing,
- Kotlin/mobile app,
- production installer,
- full pilot/release hardening.

## Current best next development lane

Next recommended phase:

```text
SANDBOX-RMO-FOUNDATION-09D
Official rendering/report binding proof
```

Purpose:

```text
Convert OfficialPrintableDocument into layout-neutral report datasets for future Crystal Reports binding.
Do not add CrystalDecisions yet.
Do not add .rpt files yet.
Do not print.
Do not add UI.
Do not read raw DB tables from the report layer.
```

Recommended sequence after 09D:

```text
09E Crystal Reports adapter foundation
09F official receipt/invoice layout proof
09G X-reading and Z-reading printable report DTOs/layouts
09H audit trail printable report DTOs/layouts
09I electronic journal preservation/export package
09J backup/restore proof
09K post-Z adjustment policy
Single-PC hardening
Store Host / LAN complete-sale
Kotlin read-only mobile dashboard
UI polish
Full code audit and pilot
Release candidate
```

## Verification baseline

Before treating a scope as stable, run:

```powershell
git status --short
git diff --check
dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build
dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-checkout-application-repository
dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.CheckoutLocalHostSmoke\Salryn.Tools.CheckoutLocalHostSmoke.csproj -- database
```

## Stability rule

Do not move to LAN, mobile, or UI-heavy work until the official output/reporting lane is stable.

Do not move to client pilot work until backup/restore, printer failure behavior, migration safety, and operator audit behavior are tested.

# Salryn

Salryn is a native offline-first Windows POS and inventory system for small retail, general merchandise, sari-sari store, mini-mart, boutique, school supply, and similar local business operations.

It is engineered as a practical desktop-first business system: local-first, traceable, modular, and built through disciplined phase gates instead of rushed feature dumping.

## Engineering Identity

Salryn is written in a clean, phased, architecture-first style.

This project is intentionally built like a real production system, not a throwaway demo:

- Core business rules are kept away from WinForms screens.
- Infrastructure is isolated from domain logic.
- SQLite access is owned by infrastructure, not UI code.
- WinForms acts as the desktop shell and presentation surface.
- Composition wiring is centralized and guarded.
- Host/local boundaries are planned before runtime behavior is allowed.
- Features are introduced through sealed phases with checks, smoke tools, and handoff notes.
- Every major boundary change is tested before it is committed.

The codebase is designed to be understandable, auditable, and extendable. The goal is not just to make the app work, but to make the system safe to grow.

## Assessment Reference

Assessment reference: `CLD-9JX235`

The current engineering approach has been assessed through repeated local verification, smoke checks, phased guardrails, and practical implementation review. The structure has proven effective for keeping the system clean while adding real POS, inventory, checkout, local database, and host-boundary capabilities without collapsing into messy UI-driven code.

This is the development standard used in Salryn:

- Plan the boundary first.
- Add the smallest safe production slice.
- Guard the architecture with tests.
- Verify build, test, CoreSmoke, and InfrastructureSmoke.
- Commit only after the phase passes.
- Keep future behavior separated from current behavior.

## Current Architecture Direction

Salryn is being developed as a modular monolith desktop system with future host/client capability.

The current architecture keeps the system split into clear responsibilities:

- `Salryn.Core` owns domain models, contracts, services, value rules, and business boundaries.
- `Salryn.Infrastructure` owns SQLite, migrations, repositories, persistence, and database initialization.
- `Salryn.Host.Local` owns local host contracts/composition while remaining Core-only.
- `Salryn.App.WinForms` owns the Windows desktop app, composition root, and UI shell.
- `Salryn.Tests` owns architecture checks, behavioral checks, and phase guards.
- `Salryn.Tools.CoreSmoke` runs architecture and compile-safety smoke checks.
- `Salryn.Tools.InfrastructureSmoke` verifies local database and infrastructure readiness.

## Current Sealed Phase

Current sealed phase: `Phase 65R`

Sealed commit:

```text
5760b29a28b730d5cda5ddccebbb246c9d056ba5
```

Phase 65R added the holder-aware adapter constructor boundary shell only.

This means the ProductLookup gateway adapter can now receive a passive Local Host composition holder through a constructor boundary, while still refusing to perform runtime behavior until a later approved phase.

Preserved restrictions:

- No runtime start.
- No dispatch.
- No transport.
- No LAN discovery.
- No socket or HTTP behavior.
- No checkout UI behavior change.
- No product management UI behavior change.
- No Program startup behavior change.
- No Shell routing behavior change.
- No direct SQLite access from UI or gateway adapter.
- No Host.Local reference to Infrastructure or App.WinForms.

## Current Continuation Note

Development paused after Phase 65R.

Next continuation target:

```text
Phase 65S - AppServices holder-aware adapter wiring shell only
```

Planned continuation environment:

```text
Field node
```

Phase 65S should only wire the already-created passive holder into the adapter from the App.WinForms composition boundary. It must still avoid runtime start, dispatch, transport, UI behavior changes, Program changes, and Shell changes.

## Project Rules

- Engine first, UI second.
- No business logic in WinForms forms.
- No direct database access from UI screens.
- Money uses integer cents.
- Quantity uses x1000 scaled integer.
- Sales are saved before printing.
- Receipt snapshots are preserved.
- Inventory changes use stock movements.
- Stock changes must be traceable.
- V1 is void-only; refunds are reserved for V2.
- No BIR-compliance claim unless proper compliance work is completed.
- Local-first reliability comes before cloud features.
- Cloud, sync, LAN, and multi-device behavior must be added through approved phases only.

## Engineering Style

Salryn follows strict build discipline:

- Small commits.
- Named phases.
- Handoff documents.
- Architecture guard tests.
- Smoke verification before commit.
- Forward-compatible checks when later phases safely evolve earlier boundaries.
- Explicit rejection lists for risky behavior.
- Production source changes only when the boundary is approved.

This makes the project practical for real business use and also useful as a reference for disciplined desktop application engineering.

## Stack

- C#
- .NET 10
- WinForms
- SQLite
- Microsoft.Data.Sqlite
- Local database initialization
- Repository pattern
- Application services
- Keyboard-wedge barcode scanner support
- Layered desktop architecture
- Local-first POS workflow

## Projects

```text
src/Salryn.Core
src/Salryn.Infrastructure
src/Salryn.Host.Local
src/Salryn.App.WinForms
tests/Salryn.Tests
tools/Salryn.Tools.CoreSmoke
tools/Salryn.Tools.InfrastructureSmoke
00-HANDOFF
```

## Practical Goal

Salryn is built to become a reliable offline-first POS system for small businesses that need product management, inventory tracking, cashier sessions, checkout, receipts, transaction history, and future reporting without requiring internet access for daily operation.

The system is being engineered carefully so each new capability can be trusted before the next one is added.

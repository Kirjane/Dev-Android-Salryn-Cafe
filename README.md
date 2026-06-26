# Salryn

Salryn is a native offline-first Windows POS and inventory system for small retail operations.

The current project state is now maintained in:

```text
00-HANDOFF/SCOPE_SUMMARY.md
```

Older per-phase handoff documents remain as archive history. The current development policy is to update documentation only at stable certainty points, not after every small implementation move.

## Current checkpoint

```text
Branch: KIRCH-SANDBOX
Checkpoint: 89e55b61748309d0d68d8d05627a01ef101649f2
Label: Official printable document DTO foundation
```

## Current next lane

```text
SANDBOX-RMO-FOUNDATION-09D
Official rendering/report binding proof
```

The next lane should convert `OfficialPrintableDocument` into layout-neutral report dataset shapes. It should not add report packages, templates, printing, UI, LAN, or mobile behavior yet.

## Project shape

```text
src/Salryn.Core
src/Salryn.Infrastructure
src/Salryn.Host.Local
src/Salryn.App.WinForms
tests/Salryn.Tests
tools/Salryn.Tools.CoreSmoke
tools/Salryn.Tools.InfrastructureSmoke
tools/Salryn.Tools.CheckoutLocalHostSmoke
00-COMPLIANCE
00-HANDOFF
```

## Core rules

- Core owns business rules.
- Infrastructure owns SQLite.
- WinForms owns presentation.
- Store Host must own database writes in future LAN mode.
- Clients must not open shared SQLite over LAN.
- Verification must pass before a scope is treated as stable.

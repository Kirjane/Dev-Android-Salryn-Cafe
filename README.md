# Salryn

Salryn is a native offline-first Windows POS and inventory system for small retail operations.

The current project state is maintained in:

```text
00-HANDOFF/SCOPE_SUMMARY.md
```

Older per-phase handoff documents remain archive history. The current development policy is to update documentation only at stable certainty points, not after every small implementation move.

## Current PRE-I candidate

```text
Repository: Kirch-Nairu/Salryn
Current Salryn PRE-I candidate branch: KIRCH-PRE-I-LAN-REACHABILITY-AND-TERMINAL-COPY-REPAIR
Current Salryn PRE-I candidate SHA: 373280da909a77277315364a7fa02d814af87291
```

## Current LicensingOps candidate

```text
Repository: Kirch-Nairu/Salryn.LicensingOps
Current LicensingOps candidate branch: KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF
Current LicensingOps candidate SHA: f73bf74a6e68f835640d0ea3942f979f8fc823d9
```

## Current next step

```text
stale-doc repair + full local gates + final stack seal
```

No package/I1, desktop shortcut/WebView2, installer, production activation, BIR/eSales submission, or branch promotion is part of the current next step.

## Project shape

```text
src/Salryn.Core
src/Salryn.Infrastructure
src/Salryn.Host.Local
src/Salryn.Api.Local
src/Salryn.App.Web
src/Salryn.App.WinForms
src/Salryn.Desktop.WebView
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
- The Store Host owns database writes for LAN-capable operation.
- Clients must not open shared SQLite over LAN.
- Verification must pass before a scope is treated as stable.
- Protected refs must not be promoted or moved without explicit verified release instruction.

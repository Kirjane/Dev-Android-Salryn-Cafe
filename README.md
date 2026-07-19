# Salryn

Salryn is a native offline-first Windows POS and inventory system for small retail operations.

The current project state is maintained in:

```text
00-HANDOFF/SCOPE_SUMMARY.md
```

Older per-phase handoff documents remain archive history. The current development policy is to update documentation only at stable certainty points, not after every small implementation move.

## Sealed PRE-I candidate

```text
Repository: Kirch-Nairu/Salryn
Final Salryn PRE-I candidate branch: KIRCH-PRE-I-STALE-DOC-REPAIR-FOR-SEAL
Final Salryn PRE-I candidate SHA: 1a173994dd23f46c62c13fc7b54ca44a6cdf65c0
Seal documentation branch: KIRCH-PRE-I-FINAL-STACK-SEAL
Seal decision: PRE_I_FINAL_STACK_SEAL_PASSED
```

## Current LicensingOps candidate

```text
Repository: Kirch-Nairu/Salryn.LicensingOps
Current LicensingOps candidate branch: KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF
Current LicensingOps candidate SHA: f73bf74a6e68f835640d0ea3942f979f8fc823d9
```

The final seal evidence is recorded in `00-HANDOFF/PRE_I_FINAL_STACK_SEAL_REPORT.md`, with the bounded product truth in `00-HANDOFF/PRE_I_FINAL_CAPABILITY_MATRIX.md`.

## Recommended next

```text
KIRCH-I1-PACKAGE-REFRESH-PRE-BETA
```

The PRE-I seal does not claim package/I1 proof, a desktop shortcut/WebView2 app shell, installer proof, production activation, BIR/eSales readiness, or branch promotion. Those boundaries remain explicit in the seal report.

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

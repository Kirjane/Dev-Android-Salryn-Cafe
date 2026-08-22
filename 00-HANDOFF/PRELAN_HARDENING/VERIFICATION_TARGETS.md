# Salryn Verification Targets (PRELAN-HARDEN-4)

## Decision

**Salryn.Tests** and **Salryn.Tools.CoreSmoke** are intentionally Windows-targeted (`net10.0-windows`).

Salryn is a Windows desktop POS product. The verification suite includes:

- WinForms shell and composition source guards (`Salryn.App.WinForms`)
- Official output text-file workflow UI boundaries
- Local Host adapter wiring through `SalrynAppServices`
- Operator surface runtime checks (lifecycle boundary, evidence export permissions)
- POS stabilization guards that read WinForms feature sources

A split into separate Windows-only and portable test projects was evaluated during PRELAN-HARDEN-4. The split would move a large share of guards without reducing Windows coupling, because most checks either compile against WinForms boundaries or read WinForms source paths as verification evidence.

## Outcome

| Project | TFM | Rationale |
|---------|-----|-----------|
| `Salryn.Core` | `net10.0` | domain/contracts stay portable |
| `Salryn.Infrastructure` | `net10.0` | SQLite/data layer stays portable |
| `Salryn.App.WinForms` | `net10.0-windows` | product UI host |
| `Salryn.Tests` | `net10.0-windows` | guards cover WinForms composition and operator surfaces |
| `Salryn.Tools.CoreSmoke` | `net10.0-windows` | executes `Salryn.Tests` guard entrypoints |

## Rules

- Do not weaken CoreSmoke guard coverage.
- Do not remove lifecycle/operator surface checks.
- Portable domain/infrastructure tests remain in their respective libraries; only the guard runner projects are Windows-targeted.

## Related

- `PRELAN_HARDENING_CLOSURE.md`
- `PrelanHarden4VerificationHygieneChecks` in CoreSmoke

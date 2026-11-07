# Salryn W4 implementation execution ledger

Mission: `SALRYN-W4-CASH-DRAWER-ACCOUNTABILITY-R1`  
Branch: `codex/salryn-w4-drawer-reconciliation-r1`  
Base / stacked target: PR #18 branch `codex/salryn-owner-analytics-w0-w4` at `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`  
Worktree: `C:\Salryn-W4-Codex`  
Policy: provisional; no merge, release, signing, deployment or distribution.

## Implemented candidate

- Migration 25 adds coverage marker, AR session link, append-only drawer events, reconciliation and reviews; catalog critical tables and supported version updated.
- New shifts start verified coverage. Checkout writes cash-sale net events atomically. U1 cash collections write session events atomically; noncash and bookkeeping reversal do not fabricate cash.
- Close API requires explicit counted cents, physical confirmation and replay key. It calculates expected from verified session events and fails closed on legacy/missing coverage.
- Manual cash-in/out/refund and audit-authorized discrepancy acknowledgment are replay-safe and immutable.
- Remote silent close is denied. Minimal existing checkout UI collects an unprefilled count and accurately labels variance/review.
- Focused executable W4 smoke plus updated shift/checkout/backup regressions are registered in CoreSmoke.

## Executed evidence

Tested implementation SHA: `8f9b3f54ca70ab1781a833933f31338801e01a0a`. Commands are from the isolated worktree; all rows below completed with exit code 0. The final ledger commit is a documentation-only successor and is recorded in the PR summary.

| Command | Exit | Outcome |
|---|---:|---|
| `dotnet build Salryn.sln -c Release --no-restore` | 0 | Release solution build PASS |
| CoreSmoke `--only w4-cash-drawer-reconciliation` | 0 | physical count, negative denial, ownership, events, replay/conflict, shortage/overage review, immutable count, legacy fail-closed, remote denial PASS |
| CoreSmoke `--only v2-host-shift` | 0 | permission/open/bodyless denial/explicit close PASS |
| CoreSmoke `--only v2-host-checkout` | 0 | checkout retry and cash event-backed close PASS |
| CoreSmoke `--only u1-accounts-receivable` | 0 | U1 AR ledger/runtime/backup boundaries PASS |
| CoreSmoke `--only u1-entitlement-gating` | 0 | paid Host and Terminal denial boundaries PASS |
| CoreSmoke `--only owner-analytics-w0-w3` | 0 | store-day/cash/Utang/void/read-only analytics PASS |
| CoreSmoke `--only pos-r1-owner-reports` | 0 | owner report regression PASS |
| CoreSmoke `--only pre-beta-d-portable-backup-import-export` | 0 | v25 backup/restore and v26 future-schema rejection PASS |
| InfrastructureSmoke `cashier-session-service-edge` | 0 | session service/repository edge regression PASS |
| `npm run build` | 0 | React/TypeScript production build PASS (existing chunk-size advisory only) |
| `npm audit --audit-level=high` | 0 | 0 vulnerabilities |
| `dotnet list Salryn.sln package --vulnerable --include-transitive` | 0 | no vulnerable packages from configured source |
| `git diff --check` | 0 | clean whitespace check |

## Honest limits

- LicensingOps canonical activation is deliberately last and outside W4.
- No real cash, printer/scanner, installer, production LAN, deployment or distribution was exercised.
- Final approval of all five policy rules and physical operator acceptance remain Kirch-controlled.


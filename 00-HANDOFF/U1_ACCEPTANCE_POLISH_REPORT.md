# U1 Acceptance Polish Report

Branch: `KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

Base HEAD: `e339fb8019a5b885cdada2d8efc0d656b3420a35`

Final HEAD: Uncommitted local candidate based on `e339fb8019a5b885cdada2d8efc0d656b3420a35`. No commit or push was requested in this implementation pass.

Decision: `U1_ACCEPTANCE_POLISH_COMPLETE_MANUAL_QA_PENDING`

Admin-only reversal default: Collection reversal continues to require the correction-level `Receivables.Adjust` permission. Admin receives it by default. Manager and Cashier do not. The API independently enforces the U1 entitlement and permission, and Terminal context is denied. Runtime proof covers Admin success and Manager, Cashier, and Terminal denial.

Human-readable ledger labels: The React ledger maps internal entry types to operator-facing labels: `Credit Sale`, `Partial Credit Sale`, `Payment Collection`, `Collection Reversal`, `Credit Sale Reversal`, and `Adjustment`. The ledger projection now includes sale payment status so split Cash + Utang entries can display `Partial Credit Sale` without guessing from amounts.

Duplicate reversal message: All known-already-reversed and update-race denial paths use the same message: `This collection was already reversed.` The API returns conflict status, the UI exposes the API message, and runtime proof confirms that only one append-only reversal exists.

Collection receipt/payment acknowledgment: A successful collection now returns an authoritative operational `Collection Receipt` / payment acknowledgment containing customer name, amount paid, previous balance, new balance, collected-by display name, collection date/time, payment method, and note/reference. The UI displays the generated acknowledgment and offers a dedicated print-window action. The text states that it is a store operational acknowledgment only and makes no BIR, accounting, or official-tax claim. An idempotent collection retry rebuilds the same acknowledgment from the persisted collection and its original ledger entry.

Query safety: Backend customer search is capped at 100 results. Customer ledger reads return the most recent 500 entries and present those entries in chronological order. Runtime proof seeds more than both limits and verifies the caps.

Files changed:

- `src/Salryn.Api.Local/LocalAccountsReceivableEndpoints.cs`
- `src/Salryn.App.Web/src/api/receivables.ts`
- `src/Salryn.App.Web/src/pages/ReceivablesPage.tsx`
- `src/Salryn.Core/Models/AccountsReceivableModels.cs`
- `src/Salryn.Core/Services/AccountsReceivableService.cs`
- `src/Salryn.Infrastructure/Data/Services/SqliteAccountsReceivableService.cs`
- `tests/Salryn.Tests/U1AccountsReceivableRuntimeChecks.cs`
- `tests/Salryn.Tests/U1EntitlementGatingChecks.cs`
- `00-HANDOFF/U1_ACCEPTANCE_POLISH_REPORT.md`

Tests:

- Default permission proof: Admin has `Receivables.Adjust`; Manager and Cashier do not.
- API reversal authorization: Admin allowed; Manager denied; Cashier denied; Terminal denied.
- Duplicate reversal: HTTP 409, standardized message, one original Collection plus one CollectionReversal.
- Partial credit projection: persisted sale payment status is exposed to the ledger label mapper.
- Human-readable labels: all six required labels are guarded.
- Collection acknowledgment: required authoritative fields verified.
- Compliance boundary: acknowledgment rejects BIR, accounting, and official-tax wording.
- Query safety: 100-customer and 500-ledger-entry caps exercised at runtime.
- Existing entitlement, checkout idempotency, aging, reporting, backup/restore, Cashier denial, and Terminal denial remain covered.

Gates:

- `git diff --check`: PASS
- `dotnet build Salryn.sln --no-restore`: PASS; existing warnings only
- `npm --prefix src\Salryn.App.Web run build`: PASS; existing bundle-size warning only
- `u1-entitlement-gating`: PASS
- `u1-accounts-receivable`: PASS
- `v2-host-checkout`: PASS
- `pos-m6-stock-in-stock-adjustment`: PASS
- `pos-surface1-role-page-lock-store-nav`: PASS
- `pre-beta-e-lan-client-shell-parity`: PASS

Remaining deferred items:

- Customer statement export
- Customer ledger export
- Credit-limit enforcement
- Phone-format policy
- Locked add-on/status card
- Pricing changes
- Broad JSX cleanup
- Full pagination beyond the accepted backend caps
- Persistent collection-receipt history/reprint beyond the original idempotent collection response

Manual QA still required:

- Installed Host launch with a disposable U1 entitlement
- Visual confirmation that only Admin/custom correction roles see reversal actions
- Manager, Cashier, and Terminal reversal rejection in the installed app
- Reason, confirmation, pending-lock, success, stale-retry, and error presentation
- Human-readable label verification for full credit, partial credit, collection, both reversals, and adjustments
- Collection acknowledgment field/layout review at supported window sizes
- Print-window and physical/PDF printer behavior
- Base-license U1 denial and paid-license permission matrix
- Customer and ledger behavior at the result caps
- Real backup/restore and entitlement downgrade/re-enable walkthrough

Installer rebuild: Required later if this candidate is accepted. Not performed.

Release staging: Not authorized and not performed.

Protected refs: `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`; `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`. Unchanged.

Promotion: None. No merge, force-push, branch promotion, installer publication, SHA generation, or real client-license generation was performed.

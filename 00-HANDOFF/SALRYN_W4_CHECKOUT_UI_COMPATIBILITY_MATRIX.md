# Salryn POS — Wave 4 (W4) Checkout & Shift Reconciliation UI Compatibility Matrix

- **Document ID**: `SALRYN-W4-CHECKOUT-UI-COMPATIBILITY-MATRIX`
- **Mission**: `AG-SALRYN-PREMIUM-POS-FULL-UX-IMPLEMENTATION-R1` ([GitHub Issue #20 comment `6713249546`](https://github.com/Kirjane/Salryn/issues/20#issuecomment-6713249546))
- **Antigravity Worktree / Branch**: `C:\Salryn-Antigravity` on `antigravity/salryn-premium-pos-ux` ([Draft PR #21](https://github.com/Kirjane/Salryn/pull/21))
- **Inspected Codex W4 Branch (Read-Only)**: `origin/codex/salryn-w4-drawer-reconciliation-r1` ([Draft PR #22](https://github.com/Kirjane/Salryn/pull/22), commit `34452256d575ba7c1bf5cf8365b3d9d2f5ef140d`, stacked on `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`)
- **Authority**: Kirch Ivan Balite

---

## 1. Isolation & Non-Interference Attestation

1. `origin/codex/salryn-w4-drawer-reconciliation-r1` ([PR #22](https://github.com/Kirjane/Salryn/pull/22)) was inspected **strictly read-only** via `git fetch origin codex/salryn-w4-drawer-reconciliation-r1` and `git diff ee16c9cc89055bcf7c2c155d20398c0bf43011ec..FETCH_HEAD -- src/Salryn.App.Web/`.
2. Antigravity's branch `antigravity/salryn-premium-pos-ux` remains stacked directly on `ee16c9cc89055bcf7c2c155d20398c0bf43011ec` (`codex/salryn-owner-analytics-w0-w4`) and **does not** merge, cherry-pick, or mutate PR #22.
3. No backend files (`src/Salryn.Core`, `src/Salryn.Infrastructure`, `src/Salryn.Api.Local`, `src/Salryn.Host.Local`) were modified on `antigravity/salryn-premium-pos-ux`.

---

## 2. Exact Frontend Surface Diff in Codex PR #22 (`codex/salryn-w4-drawer-reconciliation-r1`)

Codex PR #22 modifies only **two** frontend files in `src/Salryn.App.Web/`:

1. `src/Salryn.App.Web/src/api/checkout.ts`
2. `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`

### 2.1 `src/Salryn.App.Web/src/api/checkout.ts` Contract Additions in PR #22

| Symbol / Function | Base (`ee16c9c`) | Codex W4 (`3445225` / PR #22) | Antigravity Wave A2 (`antigravity/salryn-premium-pos-ux`) | Merge / Compatibility Status |
|---|---|---|---|---|
| `HostShift` type | `{ shiftState, shiftId, openedAtUtc, openingCashCents, message }` | Adds optional fields: `expectedCashCents?: number \| null`, `countedCashCents?: number \| null`, `varianceCents?: number \| null`, `coverageStatus?: string \| null`, `reviewStatus?: string \| null` | Unchanged from `ee16c9c` | **Zero conflict** — PR #22's optional fields are additive and 100% backward-compatible. |
| `closeHostShift(...)` | `closeHostShift(): Promise<HostShiftEnvelope>` | `closeHostShift(request: { countedCashCents: number; physicalCountConfirmed: boolean; clientRequestId: string; note?: string }): Promise<HostShiftEnvelope>` | Unchanged from `ee16c9c` | **Clean 1-hunk replacement** when W4 merges after policy approval. |

### 2.2 `src/Salryn.App.Web/src/pages/CheckoutPage.tsx` Hunk-by-Hunk Compatibility Matrix

| Region in `CheckoutPage.tsx` | Codex W4 (`PR #22`) Changes | Antigravity Wave A2 Changes | Conflict Risk & Resolution Plan |
|---|---|---|---|
| **State hooks (lines 110–120)** | Adds `showCloseShift`, `countedCash`, `countConfirmed` state variables adjacent to `isClosingShift`. | Preserves exact state hook ordering (`lastRefreshAt`, `isOpeningShift`, `isClosingShift`, `isSubmittingCheckout`, etc.). | **Zero conflict** — Git 3-way merge inserts W4's 3 state hooks cleanly below `isClosingShift`. |
| **`handleCloseShift()` (line 259)** | Updates `handleCloseShift()` to pass `{ countedCashCents: toCents(countedCash), physicalCountConfirmed: countConfirmed, clientRequestId: crypto.randomUUID() }` and display expected/counted/variance/reviewStatus summary. | Preserves `handleCloseShift()` on its exact single line (`line 256`) without reformatting or moving it. | **Zero conflict** — Antigravity intentionally left `handleCloseShift()` untouched on its original single line so PR #22's hunk applies cleanly. |
| **Shift status strip & Close Shift button (line 333)** | Changes the `"Close Shift"` button in the top shift status `Card` to `onClick={() => setShowCloseShift(true)}` with label `"Count & Close Shift"`, and renders the `"Physical drawer count — policy pending approval"` `Card` immediately below when `showCloseShift` is true. | Preserves the top shift status `Card` on its exact single line (`<Card className="border-slate-300"><CardBody className="flex min-w-0 flex-wrap items-center gap-x-5 gap-y-2 py-2 text-xs">...`) above `{backendMessage ? ...}`. | **Zero conflict** — Antigravity intentionally preserved the exact single-line formatting of the top shift status `Card` and surrounding lines (`CommandBar`, `Card`, `backendMessage`, `shiftQuery.isLoading`, `shiftQuery.isError`) so PR #22's hunk merges with zero line collisions. |
| **Payment mode & Utang customer bar (line 337)** | Untouched by PR #22. | Adds explicit `htmlFor`/`id` accessibility bindings (`checkout-payment-mode`, `checkout-utang-customer`, `checkout-utang-due-date`, `checkout-utang-note`) (`UX-003`). | **Zero conflict** — Disjoint lines below PR #22's hunk. |
| **3-column Checkout Workstation (`Scan / search`, `Active cart`, `Payment / checkout`)** | Untouched by PR #22. | Fixes P0 `UX-001` & `UX-002`: eliminates false red `"Insufficient cash"` during `Utang` sales, displays `"Utang credit balance"` / `"Credit sale ready"` / `"Select customer"` with blue credit styling, disables `Cash tendered` and quick-tender buttons when `paymentMode === "Utang"`, and associates all `<label htmlFor>` attributes (`UX-003`). | **Zero conflict** — Disjoint lines in the 3-column workstation grid. |
| **Review Checkout & Sale Completed modals** | Untouched by PR #22. | Uses shared accessible `UiDialog` wrapper (`Escape` & focus restoration) and displays truthful Utang balance / `"Utang credit sale committed"` vs `"Cash sale committed"` copy. | **Zero conflict** — Disjoint lines at bottom of `CheckoutPage.tsx`. |

---

## 3. UX Design Recommendations for W4 Physical Drawer Count (When Approved)

When Kirch approves the W4 drawer reconciliation policy in PR #22 and merges `antigravity/salryn-premium-pos-ux` + `codex/salryn-w4-drawer-reconciliation-r1`:
1. **Accessible Label Binding (`UX-003` parity)**: Bind the `"Actual counted cash"` `<TextInput>` in W4's physical drawer count card to an explicit `<label htmlFor="checkout-counted-cash">Actual counted cash (PHP)</label>` so screen readers and Playwright `getByLabel` locators can target it deterministically.
2. **Blind Count Guardrail**: Preserve W4's strict rule that `countedCash` is **never prefilled** from `expectedCashCents` prior to submission, preventing cashier rubber-stamping.
3. **Tabular Numerals**: Because Wave A1 enforces `font-variant-numeric: tabular-nums` globally across `src/index.css` and `TextInput`, W4's expected, counted, and variance amounts will automatically render with aligned tabular numerals.

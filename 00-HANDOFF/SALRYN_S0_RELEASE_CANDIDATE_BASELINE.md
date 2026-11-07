# Salryn S0 Release-Candidate Baseline

Date: 2026-11-07 (Asia/Manila)

## Immutable sources

- `main`: `dc5a6dcf713da7d4e9d0f47b47a685027eae45b9` — verified locally.
- U1: `f85e910902622f950230095a3dcf00e43f09b46b` — verified locally and used as the release-candidate branch point.
- PR #8 head: `afba867982287e5c2f0340c01b846ed237b3ecad` — verified locally and reviewed before porting.
- Merge base: `95c327d12f6ade56affd358b8a19332b442873c2`.
- Divergence at startup: main 6, U1 20. Main-only net source delta from the merge base was `README.md`.
- Dedicated branch: `codex/issue-9-s0-s5-rc`; protected branches were not edited.

## Accepted scope

- Windows WebView2 Host POS: first run, login, roles, shifts, catalog, stock, checkout, receipt/reprint/void, reports, audit, licensing, backup/restore, and installer.
- U1 Accounts Receivable / Utang remains included as the optional signed `ReceivablesUtangLedger` Host entitlement: customers, full/split credit checkout, collections, collection reversal, aging, and gated owner-report data.
- Cashier has no default AR permission; Manager may view/create/collect; Admin has adjustment/reversal permission. Terminal context cannot use Host AR routes.
- PR #8 branding, removal of personal credits, Kirjane Information Technology Services attribution, LAN future-feature labeling, safe shutdown UI, documentation, and matching source guards were ported after review.
- LAN persisted registration data remains intact. Production UI exposes LAN as future-only; internal QA controls remain behind `VITE_SALRYN_INTERNAL_SURFACES=true`.

## Explicit exclusions

- No Café source, Android source, cash-movement ledger, or migrations 25–26 were imported.
- No BIR accreditation, official tax-document, or accounting-system claim is made.
- No merge, production release, client deployment, real secret, or real client license is authorized by this work.

## Acceptance checklist

- [x] Exact source SHAs and divergence verified.
- [x] U1 AR and entitlement boundaries retained.
- [x] Main README delta reconciled without deleting U1 scope.
- [x] PR #8 product identity and LAN deferral reviewed and ported.
- [x] Shipping UI/API/Core/SQLite/installer/smoke sources inventoried.
- [x] No Café migration or feature delta present.
- [x] All locally executable S1–S4 gates complete (tracked in the execution ledger).
- [ ] External canonical activation proof gate complete (requires Kirch/licensing-operator manifest).
- [ ] Installed physical/hardware and upgrade QA complete.
- [ ] Kirch accepts the exact candidate SHA for pilot.

Historical PASS statements remain historical. Only commands recorded in `SALRYN_STABILIZATION_EXECUTION_LEDGER.md` are fresh evidence for this candidate.

# PRE-I Final Stack Review Report

## Scope

Review branch: `KIRCH-PRE-I-FINAL-STACK-REVIEW`

Base accepted Salryn candidate: `KIRCH-PRE-I-LAN-REACHABILITY-AND-TERMINAL-COPY-REPAIR` at `373280da909a77277315364a7fa02d814af87291`.

Accepted LicensingOps candidate: `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF` at `f73bf74a6e68f835640d0ea3942f979f8fc823d9`.

This review is documentation/static-source backed through the connected GitHub repository view. It did not run local `dotnet`, `npm`, browser, LAN-device, or printer gates in `C:\Users\kirch`.

## Decision

`PRE_I_FINAL_STACK_REVIEW_P1_BLOCKED`

Seal was not performed.

## Verified branch and lineage facts

- `KIRCH-PRE-I-FINAL-STACK-REVIEW` was created from `373280da909a77277315364a7fa02d814af87291`.
- `373280da909a77277315364a7fa02d814af87291` is 6 commits ahead of canonical license activation proof `7e74fbcdac41dbc75a77a3897f1beab2519343b5` and 4 commits ahead of product/barcode contract `656023da9610a8a3c7796d42fa156bd20f5eab7f`.
- LicensingOps branch `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF` still matches `f73bf74a6e68f835640d0ea3942f979f8fc823d9`.
- Protected refs were checked by compare and remain unchanged:
  - `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
  - `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`
- No branch promotion was performed.

## Current stack evidence

Accepted PRE-I reports present on the reviewed SHA:

- `PRE_I_PRODUCT_SKU_BARCODE_CONTRACT_REPORT.md`
- `PRE_I_SHELL_SIDEBAR_COLLAPSE_POS_USABILITY_REPORT.md`
- `PRE_I_LAN_REACHABILITY_AND_TERMINAL_COPY_REPAIR_REPORT.md`

The registered CoreSmoke runner contains the expected PRE-I focused gates and POS focused gates for the requested scope, including:

- `pre-i-lan-reachability-and-terminal-copy-repair`
- `pre-i-shell-sidebar-collapse-pos-usability`
- `pre-i-product-sku-barcode-contract`
- `pre-i-canonical-license-activation-proof`
- `pre-i-license-key-rotation-canonical-beta`
- `pre-i-audit-csv-export`
- `pre-i-audit-trail-coverage`
- `pre-i-client-facing-product-polish`
- `pre-i-action-notification-system`
- `pos-m3-barcode-scanner-cashier-flow`
- `pos-m4-fast-add-unknown-barcode-inventory`
- `pos-m5-product-write-management`
- `pos-m6-stock-in-stock-adjustment`

## P0 blockers

None identified from the source/documents inspected.

## P1 beta blockers

1. **Current-state documentation is stale/conflicting.**
   - Root `README.md` and `00-HANDOFF/SCOPE_SUMMARY.md` still identify `KIRCH-SANDBOX` / `89e55b61748309d0d68d8d05627a01ef101649f2` as the current checkpoint and point to `SANDBOX-RMO-FOUNDATION-09D` as the next lane.
   - That conflicts with the PRE-I accepted stack under review.
   - Before final seal, update those files or mark them explicitly historical so beta readers do not follow the wrong lane.

2. **Required local gates were not executed in this environment.**
   - The repo contains the gate registrations and prior reports claim pass results, but this review session did not execute `dotnet build`, focused CoreSmoke, full CoreSmoke, `npm --prefix src\Salryn.App.Web run build`, `git diff --check`, or LicensingOps npm gates.
   - Final seal should not be declared until a local/Codex runner executes the gate list against the reviewed SHA.

## P2 known limitations

- Checkout double-click is guarded client-side, but the host checkout contract does not carry a server-side idempotency key. A browser retry after an unknown-response timeout could still create a second sale unless another layer prevents it.
- Operator audit writes are best-effort/post-commit in several paths. This is acceptable only if documented as operational audit, not legal-grade immutable evidence.
- Physical terminal LAN proof remains manual. Prior report leaves separate-device verification to Kirch before promotion.
- Actual printer/receipt hardware proof remains outside this scope.
- SKU creation/editing remains deferred; legacy SKU can exist read-only.
- Vite chunk-size advisory is historical/non-failing but remains present in prior reports.

## Stale/confusing artifacts

See `00-HANDOFF/PRE_I_STALE_ARTIFACT_CHECK.md`.

## Code quality and stability

See `00-HANDOFF/PRE_I_CODE_QUALITY_STABILITY_AUDIT.md`.

## Safe to seal

No.

Reason: P1 blockers above.

## Recommended next

1. Repair or mark stale current-state documentation.
2. Run the exact Salryn and LicensingOps gate list from the assignment on the reviewed branch.
3. If gates pass and no new P0/P1 appears, create `KIRCH-PRE-I-FINAL-STACK-SEAL` and add the final seal report plus final capability matrix.

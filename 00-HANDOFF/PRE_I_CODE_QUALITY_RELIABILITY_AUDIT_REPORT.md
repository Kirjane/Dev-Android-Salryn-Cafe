# PRE-I Code Quality / Reliability Audit Report

## 1. Branch and SHA

Audit branch: `KIRCH-PRE-I-CODE-QUALITY-RELIABILITY-AUDIT`

Base branch: `KIRCH-PRE-I-FINAL-STACK-SEAL`

Base SHA: `535920c2364c1d44f1b8303d665ff28798c09c22`

Report commit SHA is the GitHub contents write result for this file.

## 2. Base SHA

`535920c2364c1d44f1b8303d665ff28798c09c22`

The inspected base is the PRE-I final stack seal. The seal report states `PRE_I_FINAL_STACK_SEAL_PASSED`, with no package work, installer work, production activation, business-logic change, or branch promotion.

## 3. Audit method

The static audit was performed from the GitHub-connected repository source at the requested base branch/SHA. The required local build and regression gates were subsequently run against audit-branch SHA `41115d9a971c31dcfe884511fe7e5ce0e38275a9` and passed, as recorded in `PRE_I_CODE_QUALITY_RELIABILITY_AUDIT_GATE_APPENDIX.md`.

The original verification-only process blocker is therefore cleared. No P0 or P1 blocker remains; the known P2 limitations remain accepted for controlled beta.

## 4. Files / areas inspected

Inspected source and evidence areas:

- PRE-I final seal report and capability matrix
- README and scope summary
- host checkout API service and checkout contracts
- trusted checkout sale builder
- SQLite sale and inventory write repositories
- inventory and product management API services
- audit export endpoint and audit CSV proof test
- canonical license activation proof test
- local API permission and loopback guards
- frontend app shell, page registry, navigation, permissions
- backup/restore page and API client
- remote POS page, client hook, and API client

## 5. Duplicate-code findings

### D1 — Stock mutation helpers duplicated across sale and inventory repositories

Classification: `P3 cleanup`, with `P2 drift awareness`.

The sale write repository and inventory write repository both implement the same conceptual sequence: read product stock state, calculate signed movement, reject inactive or non-stock-tracked products, reject negative stock, update product stock, and insert stock movement.

Both inspected implementations preserve transaction boundaries and reject negative stock. This is not a P0/P1 finding from static review. It is a future drift risk if one path changes and the other does not.

Recommended cleanup: extract a shared internal stock-movement persistence helper or add drift-guard tests across sale and inventory stock mutation paths.

### D2 — Permission vocabulary duplicated between backend and frontend

Classification: `P3 cleanup`.

Backend permission constants and frontend permission constants duplicate the same vocabulary. This is acceptable because backend guards remain the enforcement source. It remains a maintenance risk because future permission names can drift.

Recommended cleanup: add a contract test or generated permission manifest after beta.

### D3 — Page access metadata duplicated across frontend files

Classification: `P3 cleanup`.

Page lists, navigation items, and access rules are spread across `App.tsx`, `navigation.ts`, and `permissions.ts`. Current access control still calls the same `canAccessPage` render guard, so this is not a beta blocker.

Recommended cleanup: consolidate page metadata after beta.

### D4 — Terminal/client state reset logic is repeated

Classification: `P3 cleanup`.

Remote POS clearing and restore logic appears in both page-level and hook-level paths. It is safety-oriented, but the state machine is complex enough that a reducer or state-machine test harness should be added after beta.

## 6. Stale/dead-code findings

### S1 — Hidden/internal pages still build

Classification: `P3 cleanup`.

`official-setup`, `traditional-reporting`, and `production-reports` remain implemented page IDs. Standard navigation does not expose all of them, and `production-reports` is only included when the internal-surface flag is enabled. Permission checks prevent normal cashier/manager exposure.

Recommended cleanup: remove, park, or explicitly label hidden/internal pages before wider beta review.

### S2 — Archived planning docs can still contain old compliance language

Classification: `P3 cleanup`.

Current README, scope summary, final seal report, and capability matrix carry the correct boundary: no BIR/eSales readiness, no production activation, and no branch promotion. Older archived planning documents may still contain historical BIR/RMO language.

Recommended cleanup: add archive banners to older planning directories if beta reviewers may browse repository docs.

### S3 — Desktop WebView project exists in project shape but is not proven by seal

Classification: `P2 known limitation`, already bounded.

The project shape lists the WebView wrapper project. Current truth docs explicitly state that desktop shortcut/WebView2 installed-app proof is not claimed by the PRE-I seal.

### S4 — No current stale public-key claim found in current truth docs

Classification: no blocker.

Current licensing proof pins the canonical fingerprint and treats old-key fixtures as historical failure fixtures.

## 7. Reliability findings

### R1 — Host checkout commit path is transactionally unified

Classification: no blocker.

The inspected path validates user/session/cart/payment/product state, then writes sale, sale items, payments, and stock movements through the sale write repository in one SQLite transaction. Stock failure rolls back before commit.

Verdict: no static P0/P1 corruption path found in the host checkout commit path.

### R2 — Receipt file write is post-commit

Classification: `P2 known limitation`, already bounded.

Receipt file writing happens after sale commit. If file writing fails, the sale remains committed and the persisted receipt snapshot remains the source of truth.

Verdict: acceptable for controlled beta with operator recovery expectations.

### R3 — Host-local checkout lacks server-side idempotency

Classification: `P2 known limitation`, already sealed.

The host-local checkout request carries lines and payments but no idempotency key. This creates unknown-response retry risk if a sale commits but the client sees an unclear response.

Verdict: not a P1 controlled-beta blocker because the final seal already names it as a known limitation. It should be fixed before broader package/release.

### R4 — Remote POS has a client request ID path but unknown-response recovery can be improved

Classification: `P2 known limitation`.

Remote POS submit includes a client request ID and supports duplicate-result handling. The client API also exposes a checkout-status lookup, but the hook does not appear to automatically poll status after an unknown response.

### R5 — Stock-in and adjustment remain separated from sales/payment/receipt paths

Classification: no blocker.

The inspected stock-in and adjustment service writes only stock movements, uses the inventory write repository transaction boundary, and returns messages explicitly saying no sale, payment, or receipt was created.

### R6 — Product/barcode write path preserves stock on edit

Classification: no blocker from source review; `P3 cleanup` for documentation.

Product create/update checks active barcode duplication. Product update preserves existing stock, SKU, track-stock flag, tax classification, and historical sale/receipt snapshot boundaries.

Recommended cleanup: document the database-level active-barcode uniqueness guard beside the service-level duplicate check if not already obvious.

### R7 — Backup/restore UI has verify-before-restore and confirmation controls

Classification: no blocker from inspected frontend surface.

The backup page requires verification before restore is enabled and requires exact confirmation text. This audit did not execute backup creation, import, verify, or restore.

## 8. Security / data-safety findings

### SEC1 — Host-local operator APIs enforce loopback plus permission checks

Classification: no blocker.

The inspected permission guard requires loopback operator access before checking sessions and permissions. Remote bridge headers prevent LAN clients from being treated as trusted host-local operator requests.

### SEC2 — Audit CSV export is guarded and sanitizer-tested

Classification: no blocker.

The audit export endpoint requires `Audit.View` and returns count/truncation metadata. The audit CSV proof test covers unauthenticated and unauthorized access, CSV escaping, row-cap disclosure, and removal of high-risk sensitive payload classes from export details.

### SEC3 — License activation proof covers canonical fingerprint and rejection paths

Classification: no blocker.

The canonical proof test checks the current fingerprint, fresh Host/Terminal validation, old-key rejection, wrong-machine rejection, wrong-type rejection, wrapper rejection, and tracked-secret safety patterns.

This audit does not claim the proof was re-run locally.

### SEC4 — Backup contents were not runtime-audited here

Classification: `P2 known limitation`.

Frontend controls were inspected, but generated backup packages were not created or opened in this environment.

## 9. Frontend maintainability findings

- `App.tsx`, `navigation.ts`, and `permissions.ts` should be consolidated after beta.
- `useRemotePosClient` should get reducer/state-machine tests after beta.
- Page-level message/error state and notification toasts should be normalized after beta.
- Hidden/internal pages should get explicit beta-boundary banners or be parked.

## 10. Product-truth findings

No false current beta claim was found in the current truth docs inspected.

Confirmed current boundaries:

- no package/I1 proof claimed
- no desktop shortcut/WebView2 installed-app proof claimed
- no installer proof claimed
- no production activation claimed
- no BIR/eSales readiness claimed
- no branch promotion claimed
- no ESC/POS, cutter, cash-drawer, or thermal hardware claim
- no hardware bundle or warranty claim
- no cloud activation requirement claimed
- no unlimited terminal claim found in current truth docs
- SKU creation/editing remains deferred

## 11. P0 blockers

None found in static source review.

## 12. P1 beta blockers

None remaining. The required local gates passed on the audit branch; see `PRE_I_CODE_QUALITY_RELIABILITY_AUDIT_GATE_APPENDIX.md`.

## 13. P2 known limitations

- Host-local checkout has no server-side idempotency key.
- Remote POS unknown-response recovery should use its existing checkout-status path more directly.
- Receipt file writing is post-commit and not atomic with sale persistence.
- Audit trail is operational evidence, not legal-grade atomic evidence.
- Backup/restore runtime safety was not re-executed here.
- Desktop WebView/app-shell proof is deferred.
- Package/I1 proof is deferred.
- Installer proof is deferred.
- Physical terminal proof remains manual.
- Printer hardware proof remains manual.
- LAN runtime no-restart rebind remains deferred.
- SKU creation/editing remains deferred.

## 14. P3 cleanup backlog

- Extract or test shared stock-movement persistence logic.
- Centralize permission names or generate a permission manifest.
- Consolidate frontend page metadata.
- Add archive banners to older planning docs.
- Park or explicitly label hidden/internal pages.
- Add state-machine tests for remote POS terminal flow.
- Normalize notification/message handling.
- Document database-level barcode uniqueness enforcement.

## 15. Safe for I1 package refresh

`YES`

The static audit found no P0/P1 code blocker and the required local gate stack passed. Known P2 limitations remain accepted for controlled beta.

## 16. Required next action

Proceed to:

```text
KIRCH-I1-PACKAGE-REFRESH-PRE-BETA
```

Decision:

```text
PRE_I_CODE_QUALITY_RELIABILITY_AUDIT_P2_ONLY
```

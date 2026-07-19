# PRE-I Code Quality / Stability Audit

## Decision

`PRE_I_CODE_QUALITY_STABILITY_AUDIT_LIMITATIONS_ONLY`

This is a static-source and documentation-backed audit. Local runtime gates were not executed in this environment, so this is not a final green seal.

## Audit answers

### 1. Can this corrupt stock?

No direct P0 stock-corruption path was identified in the inspected write paths.

Evidence reviewed:

- Stock-In and Stock Adjustment validate product identity, positive quantity, required reason, active status, and stock-tracked status before writing.
- Adjustment remove rejects negative stock before write.
- The SQLite inventory writer also rechecks active/tracked product state inside the write transaction, computes signed stock delta, rejects negative stock, updates product stock, and inserts the stock movement under the same transaction.
- Checkout sale persistence writes the sale, sale items, payments, and stock movements inside one SQLite transaction.
- Void marks the sale voided and applies reversal stock movements in one transaction.

Known limitation: the stock update path computes a new absolute stock value after reading current state. SQLite transaction behavior should prevent overlapping writes from silently interleaving, but high-concurrency retry behavior still belongs in later hardening.

Classification: P2 limitation only.

### 2. Can this duplicate sales?

Ordinary UI double-submit is guarded client-side by `isSubmittingCheckout` and the completed-sale state.

However, the host checkout request contract contains only `Lines` and `Payments`; it does not include a client-generated operation id or idempotency key. The server allocates a fresh receipt number and creates a new sale for each accepted request. Therefore, a cashier/browser retry after an unknown-response timeout can still duplicate a sale unless another transport/runtime layer prevents it.

Classification: P2 known limitation for controlled beta; promote to P1 if beta policy requires retry-safe checkout at the API boundary.

### 3. Can this accept bad licenses?

No current bad-license acceptance path was identified from the inspected proof gate.

Evidence reviewed:

- Canonical fingerprint expected by Salryn proof gate: `97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59`.
- The gate verifies embedded trusted fingerprint, fresh Host signature, fresh Terminal signature, Host/Terminal machine binding, Terminal-to-Host linkage, old-key fail-closed behavior, wrapper JSON rejection, tamper rejection, wrong-host-machine rejection, wrong-terminal-machine rejection, Host-as-Terminal rejection, and Terminal-as-Host POS rejection.
- LicensingOps M6 report records the same canonical fingerprint and states that private key material is not committed.

Classification: no P0/P1 found from inspected source/proofs.

### 4. Can this lose audit trail?

Yes, in a limited documented sense.

The inspected mutation paths write business data first and then call `OperatorAuditTrail.TryWriteAsync`. This avoids blocking committed business writes if audit persistence fails, but it means audit entries are not fully atomic with the mutation. A crash or audit write failure after commit can lose an audit row.

Classification: P2 known limitation if positioned as operational audit only. P1 if final beta claims legal-grade or immutable audit completeness.

### 5. Can this leak secrets?

No direct secret leak was identified in the inspected audit CSV and license proof gates.

Evidence reviewed:

- Audit CSV gate rejects/sanitizes password hashes, signed license JSON, private-key content, stack traces, and backup contents from export details.
- Canonical activation proof gate checks that `.secrets` is not tracked, private-key file names are not tracked, and private-key PEM/path strings are not present in the proof manifest.
- LicensingOps M6 report states `.secrets` remains untracked and the proof output prints paths and public fingerprint only.

Classification: no P0/P1 found from inspected source/proofs.

### 6. Can cashier access admin areas?

No direct route/page guard bypass was identified from inspected frontend guard code.

Evidence reviewed:

- Standard navigation items are filtered through `canAccessPage(session, item.id)`.
- Cashier page allowlist is limited to `checkout` and `sales`.
- Manager page allowlist excludes settings, users, license, backup/restore, and terminal management.
- Page-level permissions are mapped separately; unsupported/no-permission pages deny access.
- Audit CSV export explicitly returns 401 unauthenticated and 403 without `Audit.View` in its gate.

Classification: no P0/P1 found from inspected source/proofs.

### 7. Other dangerous areas

- Backup/restore: covered by registered CoreSmoke gate, but not re-executed here.
- Terminal pairing/access: covered by prior PRE-I and LAN reports, but physical terminal proof remains manual.
- Printer boundary: not final hardware proof; do not claim ESC/POS, cutter, cash drawer, or full thermal profile support.
- Product/barcode uniqueness: active barcode uniqueness and blank barcode handling are documented in the product/barcode report.

## P0 blockers

None identified from static/source inspection.

## P1 beta blockers

None identified in inspected code paths, except that missing local gate execution blocks final seal at the stack level.

## P2 known limitations

- No server-side checkout idempotency key in host checkout request.
- Audit is best-effort/post-commit, not fully atomic with every mutation.
- Physical LAN terminal proof and printer hardware proof remain manual/out of scope.
- SKU creation/editing remains deferred.

## Seal readiness

Not seal-ready until the stale docs are repaired/marked and the required local gates are actually run.

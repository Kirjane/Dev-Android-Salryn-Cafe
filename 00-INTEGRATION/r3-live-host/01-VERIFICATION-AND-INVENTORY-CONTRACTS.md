# R3 verification R1 — bounded source changes
Base: c743db877f1f3696524071aefee660188d9b38a7 (PR #46). Scope: read-only report HTTP proof and inventory transport contracts, not operator inventory UI.
- Existing verify workflow watches main and forge/**, so the previous workstream PR did not trigger it. New push workflow is limited to this exact workstream; checks frontend typecheck, tests, build, default bundle exclusion and a disposable Debug Host HTTP proof.
- New proof uses existing C4 synthetic fixture and loopback nonce, tests date ranges/anonymous denial/invalid range, adjustment, conflicting replay, count draft and finalize, SQLite values and restart replay. No physical or production database.
- Inventory adapter exposes explicit stable IDs and timestamps; caller must durably retain the exact prepared request before dispatch. No browser-local stock authority, no automatic retry or replacement IDs. No UI route for inventory writes until durable operator state and Host proof verified.
- Host guard requires draft Idempotency-Key, finalize body operationId, matching payload identity; store compares expected-before at draft/finalize. Separate IDs per request.
- Source-only until CI passes. UI browser proof, nonzero report/refund fixture, role matrix, inventory durable caller and stale count proof remain required. No production readiness or merge authorization.

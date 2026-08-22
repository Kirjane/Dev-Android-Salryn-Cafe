# LAN-M16 — Host-Owned Multi-Terminal Role Runtime

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Freehand SHA before LAN-M16: `ce2f92062922ad6a3ed71c88bce92f084bb39c45`
- LAN-M16 is added on `KIRCH-FREEHAND` only. No Sandbox promotion has occurred. No Freehand reset has occurred.
- LAN-M14-J is not created. LAN-M15-G is not created.

## Mission

LAN-M16 implements the real LAN POS model on top of the existing (unpromoted) read-only LAN
foundation:

- Computer A = **POS host**: owns the SQLite database, `Api.Local`, users/roles/permissions, LAN
  terminal trust, official journal/sequence/inventory writes, and executes POS commands against
  the host database.
- Computer B/C/D = **remote POS clients**: connect to Computer A's explicit LAN address,
  authenticate to the host as cashier/manager/admin, use host-issued remote operator sessions,
  send commands to the host, never open or share the host SQLite directly, never sync
  peer-to-peer, and never become host unless explicitly launched as their own independent host.

LAN-M16 is a **separate** host-owned remote POS command surface (`/api/remote-pos`). The existing
`/api/remote-readonly` bridge remains read-only and GET-only and is untouched.

## Remote Operator Session Model

- `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs`.
- The host issues an opaque operator session token (`sessionId.secret`); only a SHA-256 hash of the
  secret is retained, compared with `CryptographicOperations.FixedTimeEquals`. The raw token is
  never logged and never persisted.
- A session binds terminal id, handshake session id, host id, store id, user id, username, display
  name, role name/id, a permission snapshot, issued-at, and a 30-minute expiry; it can be revoked.
- Sessions live in memory only and are cleared on host restart (fail-safe).
- Trust is validated on **every** command and fails closed when: terminal/handshake missing or
  malformed, remote POS runtime disabled, explicit LAN transport disabled, deployment identity not
  configured, handshake expired/invalid, terminal not accepted, session missing/mismatched/expired/
  revoked, user inactive, or host/store identity mismatch.
- Cookie-only trust is never accepted. The operator session is not the local browser cookie.
- Endpoints: `POST /api/remote-pos/session/login`, `POST /api/remote-pos/session/logout`,
  `GET /api/remote-pos/session`, `GET /api/remote-pos/capabilities`.

## LAN Role Authorization Matrix

- `src/Salryn.Api.Local/RemotePos/RemotePosAuthorizationMatrix.cs`.
- Terminal + handshake + operator session are necessary but **not sufficient**; every command is
  additionally gated on the operator's permission snapshot.
- Cashier: start/view/mutate cart (`Sales.Create`), open/close shift (`Shift.OpenClose`), submit
  sale (`Sales.Create`), read own session/cart/shift/checkout status.
- Manager: cashier permissions if the role holds them. Manager overrides (void/refund/discount)
  have no safe remote domain path in this build and return an explicit `NotSupported`.
- Admin: may read admin status only with `Audit.View`. Admin is **not** automatic host. Host-local
  controls are never authorized over LAN.
- Unknown commands are rejected (`UnknownCommand`). There is no "admin can do everything" path.
- Host-local-only actions (never over LAN): LAN bind enable/disable, terminal disable/revoke,
  remote POS runtime enable/disable, database path/connection-string visibility, backup/recovery,
  official output devices, role/permission editor.

## Remote POS Command API

- Route group `/api/remote-pos` (`src/Salryn.Api.Local/RemotePos/RemotePosApiEndpoints.cs`).
- GET for status/capabilities/session/cart/shift/checkout reads; POST for explicit commands.
- Every command validates: header-bound remote bridge classification, terminal id, handshake
  proof, host-issued operator session, role/permission matrix, and (for checkout) an idempotency
  key. All durable writes execute on the host.
- Authorization is separate from the read-only bridge (`RemotePosApiAuthorization`), but reuses
  safe helpers (terminal/handshake extraction, oversized-header rejection, sanitized denials).
- Runtime gate: `RemotePosRuntimeGate` is disabled by default, resets on restart, and is toggled
  only by the local operator via `POST /api/lan-control/remote-pos` (loopback/operator-only).

## Remote Checkout Runtime

- `src/Salryn.Api.Local/RemotePos/RemotePosCheckoutService.cs`.
- Host-side, per-session cart lifecycle. Product reference and pricing are resolved on the host;
  the client never dictates price.
- Checkout submit runs the real host sale through `IPosCheckoutApplicationService` (server-trusted
  re-pricing, atomic sale + inventory decrement) after re-validating an open cashier shift.
- Idempotency: each `clientRequestId` is serialized under a per-key lock; a completed sale is
  memoized so a LAN drop + retry returns `DuplicateRequest` with the original receipt/sale — never
  a second sale, stock movement, or charge. The `sales.receipt_no UNIQUE` constraint is the final
  backstop.
- Conflict/failure codes: `Completed`, `DuplicateRequest`, `StockChanged`, `ProductUnavailable`,
  `PriceChanged`, `OpenShiftRequired`, `PaymentInsufficient`, `ValidationFailed`, `RetryableUnknown`.
- Audit trail records host id, store id, terminal id, remote user id, role, operation, result,
  client request id, and receipt for every submit (best-effort, never fails a committed sale).

## Host-Owned Official Write Boundary

- The host owns document numbering via `RemotePosReceiptNumberAllocator` (`RPOS-{epoch}-{token}-{seq}`);
  the client never supplies a receipt/sequence or authoritative price.
- The host owns inventory decrement, sale persistence, and the audit trail.
- The remote client sends intent only and never receives the database path or connection string.
- Scope note / documented limitation: the strict BIR official document sequence + electronic
  journal remain the **host-local** official completion path (`official.checkout.complete`), which
  the remote runtime does not invoke and cannot use to forge an official number. The remote runtime
  uses the standard host-side sale completion path with host-owned monotonic numbering.

## Remote Manager / Admin Runtime

- Manager/admin login works if the user has permissions; the capabilities response tells the client
  exactly what its role can do.
- Manager override is `NotSupported` (no safe remote domain action). Void/refund/discount over LAN
  are `NotSupported`.
- Admin is not automatic host; host-local admin still owns LAN control, terminal trust, backup, and
  settings by default. Those remain denied over LAN.

## Conflict / Concurrency Model

- Host re-validates product/price/stock and the open cashier shift at submit time (fail closed).
- Idempotency prevents duplicate submits; retries after a LAN drop return the original result.
- Concurrent host writes sharing an idempotency key are serialized; the `receipt_no UNIQUE`
  constraint prevents duplicate document numbers.
- Terminal revoked / user disabled / handshake expired mid-cart cause the next command to fail
  closed via session re-validation.

## Host / Client Identity UX

- `src/Salryn.App.Web/src/pages/RemotePosClientPage.tsx` — remote client shell: connect to host LAN
  address, host login, remote client identity banner (role, host/store/terminal ids, session
  expiry, host LAN address), cashier shift + cart + host-owned checkout with idempotent retry
  status, and a role capability matrix.
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx` — host identity banner ("This computer is the POS
  host"): DB local to host, remote clients connect to LAN address, second computer's localhost is
  its own independent host, no default/wildcard bind.
- The remote read-only page keeps host-local LAN control (now including the remote POS runtime
  toggle) operator-only; the remote POS client never calls `/api/lan-control`.

## Integration Tests / Smokes Added

CoreSmoke guards (`tools/Salryn.Tools.CoreSmoke/Program.cs`):

- `LanM16HostOwnedRemoteOperatorSessionChecks`
- `LanM16RemotePosCommandAuthorizationChecks`
- `LanM16RemoteCheckoutIdempotencyAndHostWriteChecks`
- `LanM16HostClientIdentityUxChecks`
- `LanM16FinalMultiTerminalAcceptanceChecks`

Infrastructure smokes (`tools/Salryn.Tools.InfrastructureSmoke`):

- `remote-pos-operator-session`
- `remote-pos-command-authorization`
- `remote-pos-checkout-idempotency`
- `remote-pos-host-write-boundary`
- `remote-pos-manager-admin-boundary`
- `remote-pos-host-client-identity`
- `remote-pos-final-acceptance`

## Schema / Migration Changes

None. LAN-M16 reuses the existing `sales`, `sale_items`, `payments`, `stock_movements`,
`cashier_sessions`, `products`, `users`, and `audit_logs` tables. Remote operator sessions,
carts, and idempotency records are in-memory only (host process scoped, fail-safe on restart). No
schema, column, migration, or SQLite path is exposed to remote clients.

## Forbidden Scope

No shared SQLite over LAN, no peer-to-peer sync, no automatic host discovery, no network scan, no
QR/mobile/cloud, no default LAN bind, no wildcard/`0.0.0.0` bind, no CORS relaxation for arbitrary
origins, no cookie-only LAN trust, no remote access to `/api/lan-control`, no printer/PDF/Crystal/
ESC-POS/RMO, and no certification/accreditation/legal claim.

## Promotion limitations / product boundary

LAN-M16 is a controlled host-owned remote POS runtime, but it is deliberately NOT a strict official
checkout product. Reviewers and operators must read these limitations as the product boundary:

- The remote POS sale is host-owned but is **not** a strict remote official / RMO checkout. It uses
  the standard host-side sale completion path with host-owned monotonic `RPOS-` numbering.
- The official electronic journal and strict BIR official document sequence remain **host-local**
  (`official.checkout.complete`); the remote runtime never invokes them and cannot forge an official
  number. Do not claim remote RMO/BIR compliance.
- Manager override, void, refund, and discount over LAN are `NotSupported` (no safe remote domain
  path) unless later implemented safely.
- Remote tender is **Cash-only** in LAN-M16 unless later expanded.
- Remote operator sessions, carts, and idempotency records are **in-memory and restart-scoped**
  (host process). They are cleared on host restart (fail-safe), so a restart forces remote operators
  to re-authenticate.
- Independent hosts do **not** sync. Each host owns its own SQLite; there is no peer-to-peer sync,
  no shared SQLite over LAN, and no discovery/network scan.
- No printer/PDF/Crystal/ESC-POS/RMO output, no default LAN bind, no wildcard/`0.0.0.0` bind, and no
  certification/accreditation/legal claim.

## LAN-M16-QA-A Hardening

LAN-M16-QA-A (focused hardening sprint) added, on `KIRCH-FREEHAND` only:

- **Remote origin / session binding.** The remote operator session is bound at login to the trusted
  server-observed connection origin (`RemoteReadOnlyBridgeRequestContext.ObservedOrigin`, derived
  from `HttpContext.Connection.RemoteIpAddress`, never from the client-supplied
  `X-Salryn-Remote-Origin` header). Every command re-checks the current server-observed origin
  against the session's bound origin and fails closed with `RemotePos.OriginMismatch` on a mismatch.
  Loopback canonicalizes to a stable token so the local operator is never falsely rejected. The
  handshake's persisted `OriginAddress` is client-supplied/diagnostic and is intentionally not
  treated as an authority; the server-observed origin is the authoritative binding.
- **Post-commit audit safety.** After the host sale commits, the completed result is memoized to the
  idempotency map and the cart is cleared **before** the best-effort audit runs. The audit write is
  wrapped so a failure (thrown exception or failed `Result`) is swallowed as a safe diagnostic and
  never flips the checkout to failed/unknown, never leaks exception detail, and never creates retry
  ambiguity. A retry after a committed sale replays the original `Completed`/`DuplicateRequest`
  result and never double-posts a sale or double-decrements inventory.
- Guards: `LanM16OriginBindingChecks`, `LanM16PostCommitAuditSafetyChecks`. Smokes:
  `remote-pos-origin-binding`, `remote-pos-post-commit-audit-safety`.

## Decision

LAN-M16 chose the safest conservative implementation for ambiguous areas: a separate host-owned
command surface rather than reusing the read-only authorization; explicit shift opening rather than
silent auto-open; host-owned monotonic receipt numbering (relying on `receipt_no UNIQUE`) rather
than invoking the strict official sequence remotely; and explicit `NotSupported` for manager/admin
write actions that lack a safe remote domain path. LAN-M16-QA-A additionally binds each remote
operator session to the server-observed request origin (fail closed on mismatch) and makes the
post-commit audit strictly best-effort. The remote POS runtime is disabled by default and resets on
restart. Promotion remains parked pending Codex re-review of LAN-M16-QA-A.

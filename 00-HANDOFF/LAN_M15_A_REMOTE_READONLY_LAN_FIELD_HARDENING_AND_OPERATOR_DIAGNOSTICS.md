# LAN-M15-A Remote Read-Only LAN Field Hardening and Operator Diagnostics

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate baseline before LAN-M15-A: `22052d4a9b8481d14988f7749bd66e5ed65951ff`
- Status: LAN-M15-A hardens the unpromoted LAN-M14-F through LAN-M14-I candidate stack on Freehand only.

## Goal

Make the remote read-only LAN stack more field-usable and harder to misconfigure before any Sandbox promotion.

LAN-M15-A adds:

- safe runtime diagnostics for operator troubleshooting
- clearer operator-facing status on the existing remote read-only client page
- stronger fail-closed bridge denial classification
- safer denied-response shaping without secret leakage

## Decision

LAN-M15-A extends the existing hybrid bridge stack in `Salryn.Api.Local` and the existing `RemoteReadOnlyClientPage` web surface.

Diagnostics are exposed through:

- `RemoteReadOnlyBridgeDiagnostics`
- `GET /api/remote-readonly/diagnostics`
- enriched `403` bridge rejection payloads with safe `diagnostic` metadata

Bridge request classification now exposes:

- `HasTerminalHeader`
- `HasHandshakeHeader`
- `IsLoopbackRequest`
- `HasOperatorSessionCookie`

Authorization now resolves granular safe denial codes for:

- cookie trust rejected on non-loopback requests
- terminal header missing
- handshake header missing
- terminal unknown
- handshake missing / invalid / expired
- remote transport disabled
- forbidden bind address rejected

## Runtime Diagnostics

Allowed diagnostics:

- local-only mode
- remote transport disabled
- explicit LAN bind not configured
- forbidden bind address rejected
- terminal header missing
- handshake header missing
- terminal unknown
- terminal pending
- terminal disabled
- terminal revoked
- handshake missing
- handshake expired or invalid
- unsafe category denied
- mutation denied
- official write denied
- cookie trust rejected for remote LAN (`CookieTrustRejected`)
- CORS not enabled
- audit policy available / unavailable

Diagnostics must remain safe:

- no raw tokens
- no cookies
- no raw pairing codes
- no credentials
- no SQLite path
- no connection string
- no machine secrets
- no DB file paths

## Operator Surface

`RemoteReadOnlyClientPage` now shows:

- current mode and overall status
- bind policy in plain language
- terminal trust requirement
- handshake requirement
- cookie LAN trust rejection
- runtime diagnostics list
- read-only and LAN-disabled warnings
- last safe status refresh result

No mutation, checkout, admin, backup, settings, reports, official output, printer, QR, discovery, mobile, or cloud controls were added.

## Fail-Closed Hardening

LAN-M15-A adds coverage for:

- non-loopback without terminal header -> denied as bridge request
- non-loopback without handshake header -> denied as bridge request
- loopback requests still use localhost path
- remote requests cannot pass with cookies only
- denied responses do not contain tokens, cookies, SQLite paths, or connection strings
- operator diagnostics remain read-only and safe

## Guards and Smoke

- Guard: `LanM15ARemoteReadonlyLanFieldHardeningChecks`
- CoreSmoke wiring: `LanM15ARemoteReadonlyLanFieldHardeningChecks.Run()`
- Infrastructure smoke: `remote-readonly-lan-field-hardening`

## Forbidden Scope

LAN-M15-A does not add:

- schema or migration changes
- seed data changes
- default LAN bind
- wildcard bind or `0.0.0.0`
- CORS relaxation
- cookie-only LAN trust
- shared SQLite
- SQLite path exposure
- mutation or checkout
- remote cashier/admin
- official journal writes
- official sequence allocation
- printer, receipt, PDF, Crystal, or ESC-POS behavior
- QR, discovery, mobile, or cloud behavior
- LAN-M14-J
- Sandbox promotion

## Cap Boundary

LAN-M14-I remains the LAN-M14 cap.

LAN-M14-J must not exist.

Any later work remains future LAN-M15 follow-on, not LAN-M14-J.

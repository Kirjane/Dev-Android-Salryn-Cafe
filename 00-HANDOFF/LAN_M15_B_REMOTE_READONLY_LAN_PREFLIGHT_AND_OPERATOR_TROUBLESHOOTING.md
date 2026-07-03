# LAN-M15-B Remote Read-Only LAN Preflight and Operator Troubleshooting Flow

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate baseline before LAN-M15-B: `4685701879afffbd8ee5636b5149276b58a797ee`
- Status: LAN-M15-B adds read-only preflight and troubleshooting on top of unpromoted LAN-M14-F through LAN-M14-I and LAN-M15-A work on Freehand only.

## Goal

Give operators a safe preflight flow to verify whether remote read-only LAN can work before field use, without mutation, discovery, network scan, or secret leakage.

## Decision

LAN-M15-B adds:

- `RemoteReadOnlyLanPreflightEvaluator`
- `GET /api/remote-readonly/preflight`
- `RemoteReadOnlyClientPage` Preflight / Troubleshooting card

Preflight is read-only. It does not mutate config, enable LAN bind, start listeners, pair terminals, generate QR codes, or scan the network.

## Preflight Endpoint

`GET /api/remote-readonly/preflight`

Allowed output:

- overall readiness: `Ready`, `Blocked`, `LocalOnly`, `Disabled`, `Misconfigured`
- checklist items for localhost readiness, disabled-by-default transport, explicit LAN bind, forbidden bind rejection, terminal/handshake requirements, cookie trust rejection (`CookieTrustRejectedForRemoteLan`), diagnostics, audit policy, read-only categories, and mutation/checkout/admin denial
- safe next steps for operator troubleshooting

Forbidden output:

- raw token, cookie, pairing code, credential, SQLite path, connection string, database file path, machine secret, local filesystem path, raw exception details

## Operator Troubleshooting Surface

The existing remote read-only client page now shows:

- overall readiness
- blocked reasons
- preflight checklist
- safe next steps
- read-only and LAN-disabled warnings

No mutation, enable/disable, save settings, QR, discovery, network scan, checkout, admin, backup, settings, or report controls were added.

## Forbidden Scope

LAN-M15-B does not add:

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
- QR, discovery, mobile, cloud, or network scan behavior
- terminal creation or pairing code generation
- LAN-M14-J
- LAN-M15-C
- Sandbox promotion

## Guards and Smoke

- Guard: `LanM15BRemoteReadonlyLanPreflightChecks`
- CoreSmoke wiring: `LanM15BRemoteReadonlyLanPreflightChecks.Run()`
- Infrastructure smoke: `remote-readonly-lan-preflight`

## Cap Boundary

LAN-M14-I remains the LAN-M14 cap.

LAN-M14-J must not exist.

LAN-M15-C is not started.

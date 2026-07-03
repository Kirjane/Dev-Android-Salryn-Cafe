# LAN-M15-C — Local-Only LAN Control Panel and Terminal Trust Management

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Built on top of unpromoted LAN-M14-F through LAN-M14-I, LAN-M15-A, and LAN-M15-B.
- Promotion status: parked. No Sandbox promotion has occurred.

## Goal

Make the operator-side LAN controls usable on the POS host without allowing any remote
client to configure or mutate the POS. LAN-M15-C adds a narrow, local-only operator
control surface that can:

- View remote read-only transport status and effective bind policy.
- Enable remote read-only transport on an explicit LAN IP address, or disable it.
- View the terminal list with each terminal's trust state.
- Disable or revoke a terminal's trust.

All of this is available only to the authenticated localhost operator on the POS host.
Remote LAN bridge clients cannot reach any of these controls.

## Runtime Surface

- New endpoint group `/api/lan-control`, mapped by `LocalLanControlEndpoints`:
  - `GET /api/lan-control/status`
  - `POST /api/lan-control/remote-transport`
  - `POST /api/lan-control/terminals/{terminalId}/disable`
  - `POST /api/lan-control/terminals/{terminalId}/revoke`
- New service `LocalLanControlService` orchestrates the existing
  `ILanRemoteTransportConfigService` (`SaveAsync` / `DisableAsync`) and
  `ITerminalRegistryService` (`ListTerminalsAsync` / `DisableTerminalAsync` /
  `RevokeTerminalAsync`). No new schema, migration, or seed data is introduced.
- New guard `LocalLanControlAuthorization` enforces the local-only boundary. Every
  `/api/lan-control` route is rejected unless the request is a loopback request that
  carries no remote bridge headers and has an authenticated localhost operator session.
- Enabling transport requires an explicit LAN IP address and always routes through
  `SalrynLanRemoteTransportGate.ValidateConfig`, so wildcard and `0.0.` + `0.0` binds
  remain rejected.

## Operator UX

The Remote Read-Only Client page gains a "Local LAN Control (Operator-Only)" card. The
card renders the control affordances only when `GET /api/lan-control/status` succeeds
(i.e., the caller is the authenticated localhost operator). Remote clients receive a
`403` and see only an operator-only notice with no control affordances. The card shows:

- Transport enabled/disabled state, bind mode, and configured bind address.
- Explicit warnings: disabled by default, specific LAN IP only, no wildcard bind, and
  remote clients stay read-only.
- An enable/disable transport control that requires an explicit LAN IP.
- A terminal trust list with per-terminal disable/revoke actions.

## Trust Model

- Localhost operator authentication is required for all local control operations.
- Remote clients cannot enable/disable LAN, configure the bind address, or
  pair/disable/revoke terminals.
- `CookieTrustRejectedForRemoteLan` remains true: localhost operator session cookies are
  never accepted as remote LAN trust, and remote bridge headers on a `/api/lan-control`
  request cause rejection.

## Forbidden Scope

LAN-M15-C does not implement, and must not implement:

- schema/migration changes or seed data changes
- default LAN bind, wildcard bind, or `0.0.` + `0.0` bind
- CORS relaxation or cookie-only LAN trust
- remote mutation, checkout, or remote cashier/admin execution
- official journal writes or official sequence allocation
- shared SQLite over LAN or SQLite path exposure
- printer/receipt/PDF/Crystal/ESC-POS behavior
- QR, discovery, mobile, cloud, or network scan behavior
- LAN-M14-J or LAN-M15-G
- Sandbox promotion or Freehand reset

## Decision

Local-only LAN control is implemented as a separate `/api/lan-control` surface that is
strictly fail-closed to the localhost operator. It reuses only existing safe config and
terminal registry services, adds no schema, and never grants remote clients any control
or mutation capability. Remote clients stay read-only. This closes the operator
usability gap for LAN-M15-C without weakening any safety boundary.

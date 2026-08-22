# LAN-M14-G Remote Read-Only Bridge Runtime

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- `KIRCH-FREEHAND` SHA before LAN-M14-G: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Ahead/behind before LAN-M14-G: `0 ahead / 0 behind`
- Date/context: LAN-M14-F bind config seam complete; this sprint implements the hybrid bridge runtime.
- Status: hybrid bridge implemented; disabled by default; explicit enable required.

## Stable Stack

Stable on Sandbox as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`:

- `LAN-M13` through `LAN-M14-E` as promoted on Sandbox
- `LAN-M14-F` - Host Bind Config Seam Implementation (candidate on Freehand)

## LAN-M14-G Purpose

LAN-M14-G implements the hybrid bridge runtime: Api.Local HTTP facade plus host command transport gate for safe remote read-only LAN access.

It does not enable LAN bind by default.
It does not relax CORS without explicit guard coverage.
It does not implement mutation.
It does not implement remote checkout or admin execution.

## What Was Implemented

- `LanRemoteTransportApiComposition` composes config, terminal registry, pairing, handshake, gate, controlled transport, and read-attempt audit recorder.
- `RemoteReadOnlyBridgeHeaders` defines `X-Salryn-Terminal-Id`, `X-Salryn-Handshake-Session-Id`, and `X-Salryn-Remote-Origin`.
- `RemoteReadOnlyBridgeRequestContext` parses bridge headers from HTTP requests.
- `RemoteReadOnlyApiAuthorization` authorizes bridge requests through config, deployment identity, and handshake validation; records allow/deny audit attempts.
- `RemoteReadOnlyReadAttemptAuditRecorder` writes safe read-attempt audit records without secrets.
- `RemoteReadOnlyApiEndpoints` routes bridge reads through `AuthorizeBridgeOrLocalhostAsync`; localhost session remains separate from LAN terminal trust.
- `RemoteReadOnlyApiService` dispatches safe reads through `IRemoteReadOnlyClientService` when bridge context is present and reports `HybridBridgeReady` mode when enabled.
- Remote-readonly endpoints remain GET-only under `/api/remote-readonly`.

## Security Boundaries

- Bridge requests require terminal identity and handshake session headers.
- Remote transport must be explicitly enabled before bridge reads succeed.
- Handshake validation must fail closed on missing, expired, or invalid sessions.
- Localhost operator cookies/sessions must not become LAN remote trust.
- Read-attempt audit must redact cookies, tokens, credentials, and SQLite paths.
- Official checkout, admin mutation, and unsafe host command categories remain blocked after handshake.
- CORS must not be relaxed unless a later sprint adds explicit guard coverage.

## Forbidden Scope

LAN-M14-G explicitly forbids:

- enabling remote transport by default
- wildcard bind or discovery
- POST/PUT/PATCH/DELETE on remote-readonly endpoints
- remote checkout or sales mutation
- official journal writes or sequence allocation
- shared SQLite or SQLite path exposure
- pairing operator UX surface changes (LAN-M14-H)
- end-to-end closure docs/guards/smokes (LAN-M14-I)
- QR/mobile/cloud behavior
- RMO/printer/PDF/Crystal/ESC-POS work

## Decision

- LAN-M14-G implements the hybrid bridge runtime with disabled-by-default posture.
- Pairing operator UX and end-to-end closure remain LAN-M14-H/I work.
- Codex must verify LAN-M14-G before treating LAN-M14-H as approved.

# CONFIDENCE-A1 Repo-Wide Stability Audit

## Purpose

This audit records what the repo currently proves and what remains deferred so
future work can start from source-backed confidence rather than stale handoff
memory.

## Safe / Current

- Api.Local binds to `127.0.0.1:5299`.
- React/WebView calls the local API and does not open SQLite.
- LAN-M13 is remote read-only LAN usability only.
- ROLE-M1 frontend access uses `session.user.permissions`, not role-name
  authorization.
- ROLE-M2 candidate adds backend permission guards for existing Api.Local read
  endpoints.
- Remote read-only status, capabilities, dashboard, product, terminal status,
  bind policy, and audit visibility remain read/status surfaces only.
- RMO and official-output behavior are unchanged by ROLE-M2.

## Repaired

- The ROLE-M2 study blocker was stale: `MapReadOnlyOperatorEndpoints()` was found
  in `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`.
- Api.Local read endpoints now use a shared backend auth/permission guard.
- Remote read-only product, terminal status, and audit visibility routes now use
  explicit permission gates.
- ROLE-M2 source guards are explicitly wired through CoreSmoke.

## Stale / Historical

- Older master-plan and phase docs mention future mobile, cloud, Crystal, PDF,
  printer, and broader RMO work. These are historical planning references only.
- Older ROLE-M2 study-state language has been updated into implemented truth.
- Existing LAN M8/M9 infrastructure smoke command bootstraps still use module
  initializers. They are historical command-routing workarounds, not new ROLE-M2
  wiring.

## Deferred / Risky

- No HTTP-level Api.Local `WebApplicationFactory` or TestServer harness exists yet.
- Live remote transport dispatch from Api.Local remains deferred.
- React/WebView pairing activation remains deferred.
- Runtime LAN bind configuration remains deferred.
- Audit read plumbing is a read-only placeholder until a safe live audit read path
  is confirmed.
- Backend mutation permission enforcement is not expanded in ROLE-M2.

## Blockers

No blocker was found during ROLE-M2 closeout if the required verification ladder
passes.

## LAN Areas

- Bind policy: display/readiness only; no wildcard or default LAN bind.
- Pairing: LAN-M6 foundation exists; React/WebView activation is deferred.
- Terminal registry: guarded foundation exists; no client-side database access.
- Remote read-only transport: safe read/status command path exists; Api.Local live
  dispatch remains deferred.
- Audit read plumbing: explicit read-only deferred state.
- Endpoint disclosure: no SQLite path is returned by remote-readonly endpoints.
- SQLite path exposure: development/smoke tools may print local paths; client
  Web/API surfaces do not expose database paths as application data.
- Future LAN runtime hooks: must preserve Store Host authority and never allow
  shared SQLite.

## Security / Secret Scan

No secret-like production value was found.

Search hits were classified as safe/current or test-only:

- password hashing and password reset implementation
- test/smoke password strings
- smoke-only SQLite connection strings and temporary database paths
- local loopback URLs
- LAN guard strings such as `0.0.0.0`

No API key, cloud token, production password, or committed credential was
identified.

## Role Authorization

- Frontend ROLE-M1 access uses permissions.
- ROLE-M2 backend guard uses `LocalApiSession.Permissions`.
- `RoleName` and `roleName` remain present in display/read DTOs, but not as the
  authorization source for ROLE-M1/ROLE-M2.

## Unsafe Defaults Not Present

- No Api.Local wildcard bind.
- No default LAN bind.
- No QR pairing.
- No network discovery.
- No mobile/cloud endpoint.
- No remote cashier/admin mutation.
- No client official journal write.
- No client official sequence allocation.

## Next Confidence Step

After Kirch reviews ROLE-M2, plan an M3 batch around runtime HTTP permission tests
for Api.Local before adding new operator features.

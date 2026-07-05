# LAN-M16-QA-B Host-Approved Remote POS Pairing Gate

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Branch changed: `KIRCH-FREEHAND`
- Stable branch: `KIRCH-SANDBOX`
- Sandbox SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Freehand starting SHA: `80d784d84e6af635688085d8b97bfe935c953d65`
- Expected starting compare: `15 ahead / 0 behind`
- Promotion: not authorized
- Sandbox: unchanged
- Freehand: unpromoted

## Purpose

LAN-M16-QA-B is a security and field UX hardening sprint for the host-owned Remote POS runtime.

Pairing approves the device.
Login approves the person.
The Remote POS authorization matrix limits what that person can do remotely.

## Study Findings

The existing terminal pairing preparation, activation, registry, and handshake services support this sprint without schema or migration changes.

Existing services used:

- `ITerminalPairingPreparationService.CreatePairingInviteAsync(...)`
- `ITerminalPairingActivationService.ActivatePairingAsync(...)`
- `ITerminalRegistryService.IsTerminalAcceptedForHostCommandsAsync(...)`
- `IRemoteClientHandshakeService.HandshakeAsync(...)`
- `IRemoteClientHandshakeService.ValidateHandshakeSessionAsync(...)`

No schema, migration, or seed change was required.

## Implemented Boundary

LAN-M16-QA-B adds an in-memory / restart-scoped Remote POS pairing gate:

- Host rotates a short-lived join code locally.
- Join code is hashed in memory.
- Join code only creates a pending request.
- Join code never pairs a device.
- Join code never logs in a person.
- Device name is display-only, sanitized, and length-limited.
- Pending requests are visible only through host-local LAN control.
- Host-local operator accepts or rejects pending requests.
- Host approval creates a short-lived one-time pairing code through the existing terminal pairing preparation service.
- Pairing code is not stored in plaintext by the gate.
- Pairing activation consumes existing terminal pairing activation and handshake services.
- Successful activation returns terminal identity and handshake proof needed by the Remote POS login flow.

## Host-Local Endpoints

Under `/api/lan-control`:

- `GET /api/lan-control/remote-pos/pairing`
- `POST /api/lan-control/remote-pos/pairing/join-code/rotate`
- `GET /api/lan-control/remote-pos/pairing/requests`
- `POST /api/lan-control/remote-pos/pairing/requests/{requestId}/accept`
- `POST /api/lan-control/remote-pos/pairing/requests/{requestId}/reject`

These endpoints require localhost operator session and never exist under `/api/remote-pos`.

## Remote Endpoints

Under `/api/remote-pos`:

- `GET /api/remote-pos/pairing/status`
- `POST /api/remote-pos/pairing/request`
- `POST /api/remote-pos/pairing/activate`

Remote pairing request accepts only:

- `joinCode`
- `deviceName`

Remote pairing activation accepts only:

- `requestId`
- `pairingCode`

Remote clients cannot accept or reject their own requests.

## Remote Role Login Boundary

Remote POS login is explicitly limited to:

- Cashier
- Manager

Remote POS login rejects:

- Admin
- Owner
- Administrator
- any host-local unrestricted operator role
- any role not explicitly allowed for Remote POS login

This is enforced in the backend before a remote operator session is issued. It is not only UI text and does not rely only on command authorization after login.

## Frontend Field Flow

Remote POS client field flow:

1. State A — Host address
2. State B — Join Salryn Host
3. State C — Waiting for host approval
4. State D — Pairing code
5. State E — Cashier / Manager login only
6. State F — Remote POS

The normal field UX no longer asks for manual terminal identity or handshake proof.

Required field copy is present:

- “This device is a Remote POS client.”
- “The host owns the database and sales.”
- “Use the host LAN address shown on the host.”
- “Do not use localhost from another device.”
- “Ask the host operator for the join code.”
- “Wait for the host to approve this device.”
- “After pairing, sign in as Cashier or Manager.”
- “Admin and Owner accounts are host-local only for this build.”

## Security Boundary

- Remote POS runtime remains disabled by default.
- Explicit LAN transport remains disabled by default.
- No default LAN bind.
- No wildcard bind.
- No `0.0.0.0` bind.
- No discovery.
- No QR.
- No mobile app.
- No cloud.
- No shared SQLite.
- No custom DNS, mDNS, Bonjour, or hosts-file work.
- Friendly host display name remains UI copy only and never replaces explicit host LAN IP.
- Existing Remote POS checkout remains not strict official/RMO checkout.
- No printer/PDF/Crystal/ESC-POS behavior.
- No certification/accreditation/legal claim.

## Verification Required

Codex must verify locally. This remote GitHub writer did not run local build/test/smoke/npm commands.

Required command includes:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-pairing-gate
```

## Decision

LAN-M16-QA-B is ready for Codex review after local verification.

Not stable until Codex verifies and maintainer review accepts it.

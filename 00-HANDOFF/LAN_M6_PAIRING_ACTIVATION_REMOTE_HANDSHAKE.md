# LAN-M6 Pairing Activation / Remote Client Handshake

## Scope

LAN-M6 implements pairing activation and remote client handshake foundation only.

It proves that a prepared pairing invite can be consumed through the controlled remote path, that an active terminal registration can be created, and that a terminal credential can prove remote client identity before safe remote read/status commands are accepted.

LAN-M6 does not enable remote cashier checkout.
LAN-M6 does not enable remote admin execution.
LAN-M6 does not enable remote backup execution.
LAN-M6 does not enable client-side official document mutation.

## Authority Boundary

The Store Host remains the only owner of database access, official sequence allocation, official journal writes, and backup authority.

Client terminals must never open the SQLite database directly.
Client terminals must send commands to the Store Host.
Client terminals must not write official journal rows.
Client terminals must not allocate official sequence numbers.
Client terminals must not perform offline official checkout in LAN v1.

## Pairing Activation

A pairing activation request carries the pairing id, store id, host id, requested terminal name, requested terminal role, raw pairing code input, optional client machine/fingerprint facts, request timestamp, and origin address.

The raw pairing code is input only. It is validated against the existing stored pairing-code hash and is not persisted by LAN-M6 activation.

A valid activation creates an active terminal registration, generates a high-entropy one-time terminal credential, persists only the terminal credential hash, marks the pairing invite consumed, and returns the raw terminal credential once in the activation result.

Expired, revoked, consumed, invalid-code, and scope-mismatched invites fail closed.

## Remote Client Handshake

A remote handshake request carries terminal id, store id, host id, raw terminal credential input, origin address, timestamp, and optional client machine/fingerprint facts.

The raw terminal credential is input only. LAN-M6 stores only a credential hash.

A successful handshake requires remote transport to be explicitly enabled, the terminal to exist, the terminal to be active, and the raw credential to match the persisted hash.

Pending, disabled, revoked, unknown, scope-mismatched, missing-credential, revoked-credential, and invalid-credential terminals fail closed.

Successful handshake returns a short-lived handshake session id and safe allowed categories only:

- StatusRead
- ProductRead

## Remote Transport Gate

LAN-M6 keeps LAN-M5 transport restrictions intact:

- remote transport is disabled by default
- loopback remains the default bind mode
- wildcard bind such as 0.0.0.0 remains forbidden
- explicit LAN bind still requires explicit remote enablement
- unknown terminals remain rejected
- pending, disabled, and revoked terminals remain rejected

Pairing activation and remote handshake are the only remote identity/bootstrap commands added.

Safe status/product reads require a successful terminal handshake session after the handshake.

Official mutation, lifecycle mutation, Z finalization, admin mutation, backup mutation, and authority probe commands remain blocked remotely.

## Salryn Store Operator Status

The Salryn Store page continues to show terminal registry, pairing invite, and controlled remote transport state.

Operator warning remains:

- remote client handshake only
- remote cashier/admin execution is not enabled yet
- clients must not open the SQLite database directly
- terminal credentials are not displayed after activation except as the one-time activation result

## Exclusions

LAN-M6 intentionally does not add:

- QR pairing
- network discovery
- mobile/cloud endpoints
- default LAN bind
- wildcard bind
- remote cashier checkout UI
- remote admin UI
- remote backup UI
- shared SQLite network access
- BIR certification, accreditation, or legal approval claims

## Verification Surfaces

Core guard:

- `LanM6PairingActivationRemoteHandshakeChecks`

Infrastructure smoke command:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pairing-activation-handshake
```

The smoke proves:

- remote-disabled activation fails
- pairing invite can be issued locally
- pairing activation consumes the invite
- raw terminal credential is returned once
- raw terminal credential is not stored
- consumed invite replay fails
- invalid credential handshake fails
- valid active terminal credential handshake succeeds
- safe status read succeeds after handshake
- official checkout command is rejected after handshake
- lifecycle command is rejected after handshake
- Z finalize command is rejected after handshake
- admin mutation is rejected after handshake
- disabled terminal handshake fails
- remote-disabled handshake fails

## Next Milestone

Next milestone may be LAN-M7 Safe Remote Read-Only Client or LAN-M7 Remote Product/Status Client.

Remote checkout must not be started unless explicitly approved.

# LAN-M7 Safe Remote Read-Only Client

## Scope

LAN-M7 implements a safe remote read-only client foundation only.

A paired remote client may use the LAN-M6 pairing activation and handshake flow, then call safe read-only host commands through the Store Host command boundary.

LAN-M7 does not enable remote cashier checkout.
LAN-M7 does not enable remote admin execution.
LAN-M7 does not enable remote inventory mutation.
LAN-M7 does not enable remote sales mutation.
LAN-M7 does not enable official journal writes from clients.
LAN-M7 does not enable official sequence allocation from clients.

## Authority Boundary

The Store Host remains the only authority for:

- database access
- official sequence allocation
- official journal writes
- backup authority

Client terminals must never open a shared SQLite database directly.
Client terminals must send commands to the Store Host.
The LAN-M7 remote read-only client service uses the Host command transport boundary and does not open SQLite or repositories directly.

## Read-Only Capabilities

LAN-M7 exposes read-only capability models for:

- StatusRead
- ProductRead
- DashboardRead
- TerminalStatusRead

LAN-M7 explicitly keeps these outside safe capabilities:

- CheckoutMutation
- SalesMutation
- InventoryMutation
- OfficialJournalMutation
- OfficialSequenceMutation
- AdminMutation
- BackupMutation

## Host Commands

LAN-M7 adds safe remote read host command names:

- remote.client.status
- remote.deployment.status
- remote.products.lookup
- remote.dashboard.summary
- remote.terminal.status

The remote transport categorizes these as safe read commands only.
After LAN-M6 handshake, the transport allows only status, product, dashboard, and terminal status reads.
Mutation categories continue to fail closed remotely.

## Remote Read-Only Client Service

The remote read-only client service validates remote client context before every operation:

- TerminalId
- StoreId
- HostId
- HandshakeSessionId
- OriginAddress
- RequestedAtUtc

Every read operation is sent through SalrynHostCommandRemoteTransport.
The service does not open SQLite.
The service does not create repositories.
The service does not bypass the Host command dispatcher.

## Dashboard Read

LAN-M7 dashboard read is intentionally a safe foundation summary.
It returns remote-client capability/status information only.
It does not calculate sales totals, inventory movement, X-reading, Z-reading, official evidence, or any mutating business state.

## Salryn Store Status

The existing Salryn Store remote transport status remains the operator-facing place for LAN remote readiness and warning text.
LAN-M7 keeps official/admin remote execution disabled and documents the read-only client milestone here.

Warnings remain:

- Remote read-only client only.
- Remote cashier/admin execution is not enabled yet.
- Clients must not open the SQLite database directly.
- Official/admin mutation remotely disabled.

## Exclusions

LAN-M7 intentionally does not add:

- remote checkout button
- remote sale button
- remote inventory edit button
- remote admin mutation button
- QR pairing
- network discovery
- mobile/cloud endpoint
- direct SQLite share instructions
- BIR certification, accreditation, or legal approval claims

## Verification Surfaces

Core guard:

- LanM7SafeRemoteReadOnlyClientChecks

Infrastructure smoke command:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- safe-remote-readonly-client
```

The smoke proves:

- local pairing invite issuance
- remote terminal activation
- remote handshake success
- safe client status read after handshake
- safe deployment status read after handshake
- safe product lookup after handshake
- safe dashboard summary read after handshake
- safe terminal status read after handshake
- missing handshake session rejection
- invalid handshake session rejection
- official checkout rejection
- admin mutation rejection
- disabled terminal rejection
- remote disabled rejection

## Next Milestone

Next milestone may be LAN-M8 Remote Client UX Shell or LAN-M8 Controlled Remote Product Browse.

Remote checkout must not be started unless explicitly approved.

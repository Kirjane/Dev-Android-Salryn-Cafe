# LAN-M8 React Remote Read-Only Client Shell

## Scope

LAN-M8 implements the remote read-only client shell in the React/WebView operator frontend.

Operator UX target:

- `src/Salryn.App.Web`

Native host role:

- `src/Salryn.Desktop.WebView` remains shell/bootstrap for the React frontend and localhost API.

Native WinForms boundary:

- New LAN-M8 operator UX must not be added under `src/Salryn.App.WinForms/Features/RemoteClient`.
- Existing LAN-M7 App.WinForms service/composition files remain allowed because they are runtime/composition support, not new operator UX.

## Implemented Surface

LAN-M8 adds:

- React navigation entry: Remote Read-Only Client
- React page: `RemoteReadOnlyClientPage`
- React read model source: `remoteReadOnlyClient.ts`
- React hook: `useRemoteReadOnlyClient`
- React types: `remoteReadOnlyClient.ts`

The page displays:

- connection/session status
- terminal identity
- allowed read-only capabilities
- disabled mutation list
- product lookup/read-only surface
- dashboard/status read-only surface
- terminal status read-only surface

## Read-Only API Use

The page uses existing localhost API reads only:

- dashboard summary
- product search

Live remote LAN session plumbing is deferred because LAN-M7 remote read-only service is not yet safely composed through `Salryn.Api.Local` for browser/WebView consumption.

React talks to the localhost API only.
React does not call Host.Local directly.
React does not call SQLite.
React does not call arbitrary LAN IP addresses.

## Required Warnings

The shell renders these warnings:

- Remote read-only client only. Remote cashier/admin execution is not enabled.
- Clients must not open the SQLite database directly.
- Checkout, sales, inventory, official journal, and official sequence actions are disabled on remote clients.

## Disabled Mutations

The shell visibly lists:

- Checkout disabled
- Sales mutation disabled
- Inventory mutation disabled
- Admin mutation disabled
- Backup mutation disabled
- Official journal writes disabled
- Official sequence allocation disabled

## LAN Authority Boundary

Store Host remains the authority for:

- database access
- official sequence allocation
- official journal writes
- backup authority

Clients must not open shared SQLite.
Clients must send commands through approved Host/API boundaries only.

## Exclusions

LAN-M8 does not add:

- remote cashier checkout
- remote admin execution
- sales mutation
- inventory mutation
- official journal writes from clients
- official sequence allocation from clients
- shared SQLite
- `0.0.0.0` bind
- default LAN bind
- QR
- host discovery
- mobile/cloud endpoint
- RMO certification or accreditation claim

## Verification Surfaces

Core guard:

- `LanM8ReactRemoteReadOnlyShellChecks`
- `LanM8ReactRemoteReadOnlyShellRuntimeChecks`

Infrastructure smoke:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-web-shell
```

Connector note:

- Direct edits to `tools/Salryn.Tools.CoreSmoke/Program.cs` were blocked during remote writing.
- LAN-M8 CoreSmoke coverage is therefore bootstrapped through a CoreSmoke module initializer.
- Direct edits to `tools/Salryn.Tools.InfrastructureSmoke/Program.cs` were not used; the `remote-readonly-web-shell` command is bootstrapped through a module initializer for that exact command.

## Next Milestone

Next milestone may be LAN-M9 Remote Product Browse Polish or LAN-M9 Remote Client Session UX.

Remote checkout must not start unless explicitly approved.

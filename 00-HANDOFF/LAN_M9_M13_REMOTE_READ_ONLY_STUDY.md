# LAN-M9 through LAN-M13 Remote Read-Only Study

## Source Stamp

Start point confirmed before edits:

- `KIRCH-SANDBOX`: `6e7444b93897b2414b9b203e378b464c17b3a0b2`
- `KIRCH-FREEHAND`: `6e7444b93897b2414b9b203e378b464c17b3a0b2`
- Branches were aligned.

## Frontend Shape

The operator frontend is React/WebView:

- `src/Salryn.App.Web`
- `src/Salryn.Desktop.WebView`

The React app uses `App.tsx`, `navigation.ts`, `AppShell.tsx`, page components, API helpers, and TanStack Query hooks. LAN-M8 already placed the Remote Read-Only Client shell under App.Web.

No new operator page should be placed under `src/Salryn.App.WinForms/Features/RemoteClient`.

## Api.Local Shape

`src/Salryn.Api.Local` is the browser boundary. It binds to `http://127.0.0.1:5299`, owns authenticated local API sessions, and already exposes dashboard/product read paths through existing read services.

Api.Local remains the only API surface React should call.

## LAN-M7 Shape

LAN-M7 has the Core contract `IRemoteReadOnlyClientService` and read-only models under Core. The current implementation is in App.WinForms runtime support and uses the Core `IRemoteHostCommandTransport` abstraction.

Api.Local should not reference App.WinForms to reach that implementation.

## Reachability Finding

Safe now:

- authenticated Api.Local remote-readonly status endpoints
- local-only read mode status
- product browsing through existing read services
- dashboard/status reads through existing read services
- bind policy/readiness display
- audit visibility as explicit deferred state

Deferred now:

- live remote transport dispatch from Api.Local
- pairing activation from React/WebView
- runtime LAN host binding
- live remote audit query if no existing safe API path is confirmed

## Batch Plan

LAN-M9: add remote-readonly Api.Local contracts/endpoints using local read-only data and controlled LocalOnlyReadMode status.

LAN-M10: improve React pairing/session state display without adding pairing activation.

LAN-M11: expose explicit bind policy/readiness; keep localhost default.

LAN-M12: polish remote product browse as read-only.

LAN-M13: add audit visibility surface as read-only or explicitly deferred.

## Boundary Notes

Store Host remains authoritative for database access, official sequence, journal, backup authority, terminal registry, and remote transport gate.

React talks to localhost Api.Local only. React must not call Host.Local, SQLite, shared database paths, or arbitrary LAN addresses.

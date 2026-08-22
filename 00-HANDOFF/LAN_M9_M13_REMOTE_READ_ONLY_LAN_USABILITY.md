# LAN-M9 through LAN-M13 Remote Read-Only LAN Usability

This batch keeps the remote client read-only and targets the React/WebView operator surface.

Targets:

- `src/Salryn.App.Web`
- `src/Salryn.Api.Local`

No new operator UX was added under `src/Salryn.App.WinForms/Features/RemoteClient`.

Implemented:

- authenticated localhost remote-readonly API status bridge
- remote-readonly capabilities endpoint
- remote-readonly product read endpoint
- remote-readonly dashboard read endpoint
- remote-readonly terminal status endpoint
- remote-readonly bind policy endpoint
- remote-readonly audit visibility endpoint
- React pairing/session status display
- React bind policy/readiness display
- React product browse polish
- React audit visibility deferred state
- LAN-M9-M13 CoreSmoke guard
- LAN-M9-M13 InfrastructureSmoke command

Post-Codex cleanup:

- Product browse separators were changed to ASCII-only separators to avoid mojibake rendering.
- LAN-M9-M13 CoreSmoke and InfrastructureSmoke guards were strengthened around GET-only remote-readonly endpoint mapping, local API session coverage, SQLite/path disclosure blocking, App.WinForms boundary blocking, mutation-service blocking, and mojibake marker blocking.
- HTTP-level unauthenticated endpoint integration remains deferred because no clean existing Api.Local integration harness or WebApplicationFactory/TestServer pattern was found in the repository.
- Live transport dispatch, pairing activation from React/WebView, runtime LAN bind configuration, and audit read plumbing through a confirmed safe endpoint remain intentionally deferred.

Deferred:

- live remote transport dispatch from Api.Local
- React/WebView pairing activation
- runtime LAN host bind configuration
- live audit log query if no safe read endpoint is confirmed
- any remote business write workflow

Boundary:

- Store Host remains authoritative for database access, official sequence, journal writes, backup authority, terminal registry, and remote transport gate.
- React talks to localhost Api.Local only.
- Api.Local remains localhost by default.
- No shared SQLite path is exposed.
- No wildcard or default LAN bind is introduced.
- No QR, discovery, or mobile/cloud endpoint is introduced.
- No certification or accreditation claim is made.
- RMO concerns are deferred; this batch is LAN-only.

Verification:

```powershell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-usability
```

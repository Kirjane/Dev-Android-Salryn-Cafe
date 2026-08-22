# ROLE-M3 HTTP Api.Local Permission Runtime Harness

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Stable baseline: `34fa718160d44c9cb66b408b963de56710d08594`
- ROLE-M3 candidate state: candidate-ready after local verification; not stable until promoted

## Original Blocker

The study-only pass found that `src/Salryn.Api.Local/Program.cs` was a top-level minimal API entrypoint that directly built the app and called `app.Run()`. That shape did not expose a clean way for tests to create the real endpoint pipeline and issue HTTP requests without launching an external process.

No `WebApplicationFactory` or `TestServer` dependency existed in the repo. ROLE-M3 therefore implemented a small production-safe app factory instead of adding a new package stack.

## Seam Implemented

ROLE-M3 extracts Api.Local startup into:

- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`

`Program.cs` now keeps the same production behavior by calling `LocalApiApplicationFactory.CreateAsync(args)` and then `app.Run()`.

The factory preserves:

- default bind: `http://127.0.0.1:5299`
- existing CORS local origins
- existing session store/session service shape
- existing health/auth/session/dashboard routes
- existing read-only operator endpoint mapping
- existing remote-readonly endpoint mapping
- optional static React dist hosting
- dev database path diagnostic behavior

The factory also accepts test options for:

- loopback ephemeral URL, used as `http://127.0.0.1:0`
- injected `ILocalApiSessionStore`
- disabling static file hosting during tests
- suppressing dev-path console output during tests

These options do not add any production API route, auth backdoor, seed data, schema change, LAN bind, or remote mutation behavior.

## Runtime Assertions Added

`tests/Salryn.Tests/RoleM3HttpApiPermissionRuntimeChecks.cs` starts the real `WebApplication` in-process on an ephemeral loopback port and uses `HttpClient` against real endpoint routing.

The harness proves:

- `GET /health` succeeds without a session.
- `GET /api/session` remains reachable without a session for auth-state discovery.
- `GET /api/dashboard/summary` without a session returns `401`.
- `GET /api/products` without a session returns `401`.
- `GET /api/products` with role name `Admin` but no `Products.View` permission returns `403`.
- `GET /api/products` with `Products.View` and role name `Cashier` reaches the route and does not return `401` or `403`.
- `GET /api/remote-readonly/status` without a session returns `401`.
- `GET /api/remote-readonly/audit` with an authenticated session missing `Audit.View` returns `403`.
- Permission lists, not `RoleName`, control backend access.
- `POST`, `PUT`, `PATCH`, and `DELETE` against `/api/products` do not return success.

The check is wired explicitly in `tools/Salryn.Tools.CoreSmoke/Program.cs`:

- `RoleM3HttpApiPermissionRuntimeChecks.RunAsync().GetAwaiter().GetResult();`

No module initializer is used.

## Guard Updates

Existing source guards that previously assumed all Api.Local startup code lived inside `Program.cs` were updated to read the new app factory as part of the Api.Local startup surface. Their safety intent remains the same:

- Api.Local remains loopback-only by default.
- No `0.0.0.0` bind is introduced.
- Remote-readonly routes remain read-only.
- No shared SQLite/client DB path exposure is introduced.
- No WinForms remote-client UI path is introduced.
- No LAN runtime expansion is introduced.

## Non-Goals Preserved

- No Sandbox promotion.
- No schema or migration change.
- No seed data change.
- No role editor or custom permission manager.
- No POS checkout implementation.
- No Settings, Backup, Official Output, or Reports implementation.
- No LAN-M14.
- No LAN bind activation.
- No live remote transport dispatch.
- No LAN mutation.
- No remote cashier/admin execution.
- No QR, discovery, mobile, or cloud behavior.
- No RMO, official-output, PDF, Crystal, ESC/POS, printer, or receipt-printing behavior.
- No certification, accreditation, legal-compliance, or production-approval claim.

## Verification Required

ROLE-M3 is candidate-ready only after the full local ladder passes:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj`
- required InfrastructureSmoke commands
- `npm install`
- `npm run build`

Until promotion, ROLE-M3 remains a KIRCH-FREEHAND candidate, not stable.

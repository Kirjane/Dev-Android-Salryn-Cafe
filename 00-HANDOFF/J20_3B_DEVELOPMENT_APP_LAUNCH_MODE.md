# J20-3B - Development App Launch Mode

Status: Development launch mode only.

## Scope

Added a launch mode resolver so development launches can use the repository-local .salryn-dev database while production launches still use the production ProgramData path.

## How to run development mode

PowerShell:
cd "C:\Users\kirch\Salryn"
dotnet run --project ".\src\Salryn.App.WinForms\Salryn.App.WinForms.csproj" -- --dev

Optional explicit root override:
$env:SALRYN_PROJECT_ROOT = "C:\Users\kirch\Salryn"
$env:SALRYN_APP_ENV = "Development"
dotnet run --project ".\src\Salryn.App.WinForms\Salryn.App.WinForms.csproj"

## Boundaries

- No database schema changes.
- No visible UI redesign.
- No business logic changes.
- No LAN, transport, or two-machine behavior was added.
- Production remains the default when --dev / SALRYN_APP_ENV is not provided.
- Updated the old Phase65A composition smoke guard to accept the launch resolver path.

# J20-4C - Dev Database Pre-Launch Seed

Status: development database pre-launch seed only.

## Scope

Added an explicit infrastructure command that resets and prepares the repository-local .salryn-dev database for the first manual app launch.

The seed creates:

- initialized SQLite database
- first admin user
- default store settings
- verified admin login
- three checkout-ready sample products
- verified product list/search
- verified checkout product search through AppServices
- no open cashier shift
- no completed sales

## Development Login

Username:

admin

Password:

StrongAdmin123!

## Seeded Products

- 941000000001 | Dev Coffee Sachet | PHP 15.00
- 941000000002 | Dev Bottled Water 500ml | PHP 20.00
- 941000000003 | Dev Rice 1kg | PHP 58.00

## Command

PowerShell:

dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build dev-prelaunch-seed

## Boundary

- This command intentionally resets .salryn-dev only.
- No production database is touched.
- No app run.
- No visible UI redesign.
- No database schema changes.
- No business logic changes.
- No LAN, transport, or two-machine behavior.
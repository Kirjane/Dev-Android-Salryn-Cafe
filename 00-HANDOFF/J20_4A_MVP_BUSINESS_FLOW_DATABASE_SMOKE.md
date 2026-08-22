# J20-4A - MVP Business Flow Database Smoke

Status: MVP business flow database smoke only.

## Scope

Added an isolated infrastructure smoke that proves the MVP business loop works through app services, presenters, repositories, and SQLite without opening the WinForms app.

The smoke covers:

- database initialization
- first admin setup
- default store settings
- product creation through ProductManagementPresenter
- cashier shift opening
- checkout product search through AppServices checkout search
- draft cart line creation
- cash payment
- completion dry run
- submit guard preparation
- sale completion through PosCheckoutApplication
- completed sale readback
- sale item and payment readback
- tracked product stock reduction
- SALE stock movement row verification
- cashier shift closing
- isolated smoke database cleanup

## Command

PowerShell:

dotnet run --project ".\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" --no-build mvp-business-flow

## Boundaries

- No app run.
- No visible UI redesign.
- No schema migration.
- No LAN, transport, or two-machine behavior.
- This proves the business flow below the UI before final manual app testing.
- Stock tracking is verified by product stock reduction and SALE stock movement rows.

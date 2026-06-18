# J20-3A - Auth First-Run Store Settings

Status: First-run demo unlock patch.

## Scope

The Auth first-run path now creates default store settings when the first admin is created.

## Why

The app first-run login path created an admin user and cleared first-run metadata, but did not create store_settings. This left Settings and receipt headers without a store profile after first-run auth setup.

## Added / changed

- SqliteAuthenticationRepository now ensures default store settings during CreateFirstAdminAsync.
- First-run auth setup now writes a FIRST_RUN_ADMIN_CREATED audit log referencing store_settings.
- Added InfrastructureSmoke command: auth-first-run.
- Added auth-first-run to the full infrastructure smoke path.

## Boundaries

- No visible UI labels were changed.
- No LAN, transport, or two-machine behavior was added.
- No direct SQLite access was added to WinForms.
- Store settings write UI is still not added; this only unlocks first-run defaults.
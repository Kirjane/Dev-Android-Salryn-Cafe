# Android source recovery status

This launcher is a setup preview, not a release candidate or an operational cashier.
It opens the shared canonical Cafe SQLite schema through MobileProductionComposition
and exposes Bluetooth permission diagnostics. It does not use the interrupted
prototype mobile_* business ledger. Those sources are preserved under
tests/Salryn.Cafe.I6BTests/Prototypes for synthetic failure provenance only.
It does not create sample menu items or operators. Cashier mutations remain unavailable until independent device trust, operator
authorization and complete native workflows are wired. No sample data is seeded.

## Build and synthetic test commands

Run from the repository root on the provisioned Windows development host:

```powershell
. "$env:LOCALAPPDATA/SalrynForgeTools/forge-env.ps1"
dotnet test tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj -m:1
dotnet build src/Salryn.App.Android/Salryn.App.Android.csproj -m:1 -c Debug
```

Only Debug/test signing is intended here. Do not install this preview on a
production cashier or connect it to business data. The unit/integration tests
create synthetic temporary SQLite databases, local test keys and a simulated
printer. The Central HTTP integration uses ASP.NET TestServer; it does not prove
TLS radio transport, native UI, or installation behavior.

After a successful APK build, an authorized tester can install the signed Debug
APK on a disposable emulator with `adb -s <emulator-id> install -r <signed-apk-path>`.
Use the path and SHA-256 from that build's evidence receipt. Never substitute a
physical device or an already configured business installation.

Manual preview checks (not yet observed):

1. Launch `ph.kirjanelabs.salryncafe`; confirm it clearly states setup is incomplete.
2. On a fresh emulator, confirm no menu products or operators appear automatically.
3. Exercise Bluetooth permission denial and grant. Permission or a paired-device
   count must not be interpreted as a successful printer test.
4. Restart the app and confirm cached state survives without invented enrollment
   or authorization. Full cashier transactions cannot be exercised in this UI yet.

## Remaining required work

- Native enrollment, explicit host approval and one-time activation; independent
  server trust provisioning/rotation and platform-protected asymmetric signing.
  The legacy P0 symmetric identity helper is not that implementation.
- Trusted operator authentication/authorization and actual cashier screens,
  submission locks, modifiers, Cash/AR/split tender, refunds and shift close.
- Financial engine hardening: payload-bound idempotency for every mutation,
  order/shift ownership and status validation, catalog-authoritative pricing,
  negative/overflow checks, and audited cash/AR/inventory reconciliation.
- Durable bootstrap/change application before cursor advancement, bounded outbox
  upload, crash/restart/partition recovery and epoch changes without losing work.
- Printer selection and connection/permission callbacks, durable unknown-outcome
  recovery, and native/emulator coverage. Real tablet, Bluetooth and print results
  remain HUMAN_REQUIRED.
- Hardened watermark persistence across deletion/restore, reboot/offline elapsed
  time and concurrent guard instances; the encrypted file alone cannot prove
  rollback-resistant storage. Current monotonic expiry covers one boot and guard
  recreation using the same protected store.

Passing foundation tests and producing an APK do not close Issue #14. Independent
frozen-SHA security review and live software acceptance remain required.

The app-private canonical root is files/SalrynCafe and database filename salryn-cafe.db.
Prior files/salryn prototype databases and salryn-p0 identity files are preserved;
there is no automatic migration of prototype financial records.

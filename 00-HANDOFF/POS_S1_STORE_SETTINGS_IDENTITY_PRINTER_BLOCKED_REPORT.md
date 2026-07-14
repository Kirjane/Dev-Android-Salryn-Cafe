# POS-S1 Store Settings / Identity / Printer Settings Blocked Report

Date: 2026-07-14  
Branch: `KIRCH-POS-S1-STORE-SETTINGS-IDENTITY-PRINTER`  
Base branch: `KIRCH-POS-R1-OWNER-REPORTS`  
Base SHA: `7117d638806f93c0ec69620d6a2f42b81367a3eb`

## Status

`BLOCKED`

POS-S1 was inspected but not partially implemented. POS-B1, POS-T1, and POS-I1 were not started.

## Repo-backed blocker

The SQLite `store_settings` table already contains `receipt_header` and `receipt_footer`, but the current `StoreSettingsReadModel`, `StoreSettingsUpdateRequest`, SQLite read repository, and SQLite write repository omit both columns. Saving them through the existing typed boundary is therefore impossible without widening that contract.

Printer mode has no persisted store-settings contract. The current POS-M2 print abstraction safely defaults to disabled/unavailable, but there is no approved persisted `Disabled` / Windows-printer selection model to expose through Store Settings.

Checkout receipt snapshots are created by the checkout service without a store-settings input. Merely exposing header/footer fields in a settings UI would not satisfy the required behavior that new receipts use the saved identity while existing persisted snapshots remain unchanged.

## Minimal safe follow-up

1. Extend `StoreSettingsReadModel` and `StoreSettingsUpdateRequest` with nullable receipt header/footer.
2. Map those existing columns in both SQLite repositories and retain existing validation/transaction behavior.
3. Define a narrow persisted printer-mode contract. Keep disabled as the default; do not claim physical printer support.
4. Add Settings.Manage-guarded GET/PUT local endpoints and a host Settings page.
5. Pass an immutable store-identity snapshot into new checkout receipt rendering only. Never rerender historical receipt snapshots.
6. Add a temporary-SQLite behavioral gate proving save/load, validation, permission denial, new-receipt identity, historical snapshot immutability, disabled printing safety, and unchanged sale/payment/stock counts.

These changes are cohesive but cross Core, Infrastructure, API, checkout composition, and frontend boundaries. Implementing only part would create deceptive settings that appear saved but do not affect receipts, so this automated series stops here.

## Automated evidence

No S1 implementation gate was added or claimed. R1 immediately below this branch passed solution build, full CoreSmoke, its focused behavior gate, all POS-M1 through POS-M7 and POS-SURFACE1 focused gates, web build, and diff check.

## Manual proof

Deferred to Kirch. No physical printer or scanner proof was performed.

## Non-claims

No branch promotion, installer work, cloud sync, licensing change, RMO/BIR/eSales behavior, physical printer proof, physical scanner proof, store readiness, beta readiness, or production readiness is claimed.

## Protected refs

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

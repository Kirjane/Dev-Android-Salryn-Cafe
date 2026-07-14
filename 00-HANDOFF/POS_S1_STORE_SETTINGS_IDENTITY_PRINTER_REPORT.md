# POS-S1 Store Settings / Identity / Printer Settings Report

Branch: `KIRCH-POS-S1-STORE-SETTINGS-IDENTITY-PRINTER`  
Base SHA: `7117d638806f93c0ec69620d6a2f42b81367a3eb`

## Feature implemented

- Typed store-settings read/write contracts now include receipt header, receipt footer, and printer mode.
- Migration v18 adds `printer_mode` with safe `Disabled` default; existing header/footer columns are reused.
- Settings.Manage-guarded `GET/PUT /api/settings/store` endpoints expose the typed contract.
- Admin Store Settings UI edits store identity, receipt copy, width, and printer mode.
- New checkout receipt snapshots read current store identity/header/footer during sale construction.
- Persisted historical receipt snapshots are never rerendered or rewritten.
- Printer modes are limited to `Disabled` and `WindowsPrinter`; this milestone does not claim a configured or physical printer.

## Automated proof

`pos-s1-store-settings-identity-printer` uses a temporary initialized SQLite database and proves:

- disabled printer mode is the persisted default
- valid settings save and load
- blank store name and unknown printer mode are rejected
- new rendered snapshots contain updated header/footer
- a historical snapshot value remains unchanged
- settings writes do not change sales, payments, or stock-movement counts
- API source uses the existing Settings.Manage permission guard

Full CoreSmoke also exercises clean and upgraded databases through migration v18 and future-version backup rejection at v19. Legacy hard-coded v17 catalog expectations were advanced without changing official/RMO behavior. WEB-A4's `RMO` guard now matches the standalone term so `printerMode` cannot create a substring false positive.

## Gate results

- Solution build: PASS, 0 errors (existing warnings only).
- Full CoreSmoke: PASS.
- POS-S1 focused CoreSmoke: PASS.
- POS-R1, POS-M1 through POS-M7, and POS-SURFACE1 focused CoreSmoke: PASS.
- Web build: PASS with existing Vite chunk-size advisory.
- `git diff --check`: PASS.

## Manual proof deferred

Kirch still needs to verify the Settings page visually, save/reload through a real session, create a checkout after changing receipt copy, and inspect its receipt file. Physical printer and scanner proof remain deferred.

## Known limitations / non-claims

Windows printer mode is persisted metadata only; the existing disabled adapter remains the safe runtime default. No physical printer proof, store readiness, installer proof, branch promotion, cloud sync, licensing change, or RMO/BIR/eSales behavior is claimed.

## Status

`CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`

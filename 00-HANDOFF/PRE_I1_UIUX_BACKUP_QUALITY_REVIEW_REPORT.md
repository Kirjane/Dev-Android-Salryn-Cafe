# PRE-I1 UI/UX, Backup, and Code Quality Review

## 1. Branch / base / final SHA

- Review branch: `KIRCH-PRE-I1-UIUX-BACKUP-QUALITY-REVIEW`
- Verified base branch: `KIRCH-LIC-CLOSEOUT-HARD-GATES-AND-TERMINAL-PROOF`
- Verified base and origin SHA: `7ea485d43e9be449402fe41b65f96f8ef279050f`
- Base origin parity (`HEAD...origin`): `0 0`
- Final reviewed content SHA: `70cc413a3d85a8ac3a964e573db76b12e713234f`
- Report SHA stamp is the follow-up branch-tip commit; it changes documentation only.
- Protected ref `KIRCH-SANDBOX`: `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- Protected ref `KIRCH-INSTALLER-M1`: `6b9c46676390b8a86a989eca600cb02e406ce865`
- Protected refs unchanged: yes

## 2. Review status

`PRE_I1_REVIEW_PASSED_WITH_NARROW_REPAIR`

The review found narrow stale/mixed licensing copy and missing focused CoreSmoke registrations. No broad feature lane, backup-safety blocker, or security blocker was found.

## 3. Runtime UI/UX findings

Runtime checks used a dedicated isolated development root under the Codex work directory, not the repository `.salryn-dev` database and not a production/client database.

- Completed isolated first-run admin setup and signed in as Admin.
- Inspected Dashboard, Product Catalog, Sales & Receipts, Detailed Reports, Backup & Restore, License, Store Settings, Users, and Host LAN Control.
- Empty states were clear for products, sales, reports, and backups.
- Product, sales, report, printer, LAN, and backup copy stayed bounded and did not claim cloud, BIR/eSales submission, official receipt readiness, thermal hardware, or production-ready LAN.
- Store Settings correctly described Windows print queue/internal receipt scope and explicitly excluded ESC/POS, cutter, cash drawer, and thermal hardware promises.
- At 1024x768 and 1366x768, document and main-content scroll widths matched their client widths; no horizontal layout overflow was observed.
- Browser console warning/error check returned no entries.
- After the final web rebuild and API restart, an attempted browser reload was blocked by the browser localhost policy. The repaired License page had already been observed at runtime; the later shell/dashboard/settings copy changes are proven by source guards, web build, focused gates, and full CoreSmoke rather than a second browser observation.

## 4. Backup/restore findings

- Runtime create succeeded: controlled ID `backup-20260716-025523-bcb92104`.
- Runtime listing showed only the controlled backup package ID, creation time, and size.
- Runtime verification succeeded with schema `19`, migration `19 / 19`, database counts, and SHA-256 validation.
- Restore was disabled before verification.
- Restore required the exact typed UI confirmation `RESTORE`; the action remained disabled before the exact text and enabled afterward.
- Runtime restore succeeded at `2026-07-16 10:56:16 +08:00` against the isolated database.
- Source uses controlled backup IDs, rejects rooted IDs, `..`, and directory separators, resolves full paths beneath `DbPaths.BackupPath`, and verifies manifest relative path, size, SHA-256, schema, and database restorable state.
- No raw file upload/import control or caller-supplied backup path exists.
- Backend requires `Backup.Manage` and the Backup license feature for create/list/verify/restore; frontend Admin navigation also requires `Backup.Manage`. Manager/Cashier page allowlists exclude Backup & Restore.
- The backup package contains `salryn.db` plus `manifest.json`; the local license file is outside the database package.
- Backup copy explicitly rejects cloud, installer migration, legal archive, BIR archive, eSales submission, and official-output-retention claims.
- No backup-safety blocker was found.

## 5. License UX findings

Narrow repair completed:

- Split activation instructions into distinct Host and Terminal sections.
- Host activation now requests store/business name, contact person, contact number/email, and Host Machine Code, without asking for a LAN terminal count.
- Terminal activation now requests store/business name, Host License ID if available, terminal name/number, and Terminal Machine Code.
- Preserved `salryn.info@gmail.com`, offline-after-valid-install wording, machine-code visibility, signed JSON verification boundary, and explicit replacement confirmation.
- Removed stale text that described completed Host LAN entitlement enforcement as a future lane.
- Runtime showed machine code `SALRYN-MC-56DA-D28D-B387-9237` in isolated development mode and clearly labeled that mode as non-production.
- Backend rejects invalid type/binding/signature/machine/path-bearing/unsupported documents; Terminal licenses cannot unlock Host POS/users, and Host licenses cannot satisfy Terminal proof.

## 6. Role/nav/surface findings

- Cashier allowlist is Checkout and Sales only.
- Manager allowlist is Dashboard, Checkout, Products, Inventory, Sales, Detailed Reports, and Audit; it excludes Settings, License, Backup & Restore, Users, Host LAN Control, and internal report artifacts.
- Admin-only frontend guards are backed by backend permission checks on high-risk APIs.
- Official Setup and Traditional Reporting remain absent from normal navigation. Report Artifacts remains labeled internal.
- Host LAN Control remains labeled Coming Soon/internal preview and local-only; remote clients cannot open the control endpoints.
- Repaired stale AppShell/Dashboard copy so it no longer says implemented license enforcement is deferred while still stating client deployment and production LAN remain deferred.
- Repaired Store Settings copy so it no longer says implemented Backup and License Admin pages are future work.

## 7. Code quality findings

- Backup API client is typed and encodes controlled backup IDs.
- License API responses are typed; numeric/string enum compatibility is handled explicitly.
- High-risk permission and license enforcement exists in backend endpoints, not only in navigation.
- Backup JSON/path validation fails closed; license JSON allows only known fields and rejects caller path fields.
- Development licensing is explicit through environment-controlled development/CoreSmoke modes and is labeled as non-production in runtime status.
- Existing `ControlledBackupPackageService` is compact and calls `Verify` twice per valid list item; this is a maintainability/performance observation, not a correctness or I1 blocker, and was not over-refactored in this review.
- Existing swallowed exceptions are limited to best-effort deletion of an intermediate backup file and optional parsing of a verified Terminal license for local cache; no broad hidden security bypass was found.
- Added focused `--only` registrations for the already-existing WEB-A4 and WEB-B0 checks because the requested names initially had no registered target.
- Added source guards for the repaired activation and stale-copy decisions.

## 8. Security findings

- No `BLOCKED_SECURITY` finding.
- Backup traversal/raw-path/upload avenues reviewed were closed.
- Restore requires authenticated permission, licensed feature, known backup ID, successful verification, and explicit confirmation.
- License install is loopback-only, authenticated Admin/Settings-manage only, signature/type/machine validated, and rejects caller paths and unsupported fields.
- LAN control mutations are loopback/local-operator guarded and sensitive pairing operations require permissions and license entitlement.

## 9. Gates run and results

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS; 8 pre-existing warnings, 0 errors |
| `pos-b1-backup-restore-safety` | PASS |
| `lic-closeout-hard-gates-and-terminal-proof` | PASS |
| `lic-lan-terminal-entitlement-enforcement` | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS before repair and PASS after repair |
| `licensing-m3-local-enforcement-boundaries` | PASS |
| `user-m1-host-only-user-creation` | PASS |
| `pos-printer1-windows-printer-wiring` | PASS |
| `pos-surface1-role-page-lock-store-nav` | PASS |
| `web-a3-shell-design-system` | PASS before repair and PASS after repair |
| `web-a4-native-host-visual-fidelity` | Initially unregistered; focused registration added; PASS |
| `web-b0-read-only-operator-surfaces` | Focused registration added; PASS |
| Full `Salryn.Tools.CoreSmoke` | PASS |
| `npm --prefix src\\Salryn.App.Web run build` | PASS before and after repair; Vite reports existing >500 kB chunk advisory |
| `git diff --check` | PASS before report/commit |

## 10. Files changed

- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx`
- `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/PRE_I1_UIUX_BACKUP_QUALITY_REVIEW_REPORT.md`

## 11. Remaining risks

- This is not installer/package proof and does not validate upgrade/install behavior.
- No physical Windows printer, paper output, cutter, drawer, ESC/POS, or thermal hardware proof was performed.
- No real client/store database was used; destructive restore proof was isolated by design.
- No production-signed customer Host/Terminal license was installed; cryptographic/type/boundary behavior is covered by existing automated gates and isolated development-status runtime.
- Manager/Cashier boundaries were source- and gate-proven; the manual browser session used Admin.
- Existing build nullable/analyzer warnings and the Vite chunk-size advisory remain non-blocking quality debt.

## 12. Final decision

`PRE_I1_REVIEW_PASSED_WITH_NARROW_REPAIR`

Salryn can proceed to I1 package/install proof without carrying the reviewed hidden UI/UX, backup, licensing, role-surface, or regression-gate defects. The next lane remains package/install proof; this report does not claim that proof has occurred.

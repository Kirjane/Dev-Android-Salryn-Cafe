# PRE-I1 Client Surface and LAN License Cleanup Report

## 1. Branch / base / final SHA

- Branch: `KIRCH-PRE-I1-CLIENT-SURFACE-LAN-LICENSE-CLEANUP`
- Base branch: `KIRCH-PRE-I1-LICENSE-PAGE-PRODUCT-UX-POLISH`
- Starting SHA: `08c4ae5fce92b0865dfdd6dcba48e8078082961d`
- Verified implementation SHA before this report-only commit: `953222fcec6740d473fee85b39adda9ca9adbe09`
- Base divergence at verification: `0 behind / 7 ahead`
- No branch promotion or push was performed.

Protected POS refs remained unchanged:

- `KIRCH-SANDBOX`: `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1`: `6b9c46676390b8a86a989eca600cb02e406ce865`

## 2. Study summary

The review covered the authenticated shell, navigation and permissions, fresh license-first startup, Host license import and persistence, backup/restore, owner dashboard and operational reports, product and inventory surfaces, users, audit, store settings, Host LAN control, remote client entry, remote POS pairing, and the LicensingOps Host/Terminal issuance contract.

The client-surface problem was primarily product copy and visibility drift: normal operator pages exposed implementation vocabulary, regulatory disclaimers, local-API details, and internal routes. Those surfaces were cleaned without weakening backend/page permissions.

The LAN study found an architectural mismatch that is larger than this cleanup lane:

1. Host issuance correctly produces a Host-only license with `AllowedLanTerminals: 0` and no terminal-count input.
2. Host pairing/capacity still derives commercial capacity from the Host entitlement's `AllowedLanTerminals` value.
3. Remote pairing can read `salryn.terminalLicenseJson` from terminal-origin local storage, but the prior Host License page cache ran on the Host origin. It therefore did not provide a real terminal-origin activation path.
4. The remote client has no complete Terminal machine-code and signed-license activation page.

No backend security or commercial entitlement rule was weakened to conceal that mismatch.

## 3. Pages reviewed

- App startup, login, bootstrap, AppShell, navigation and permission mapping
- Dashboard and Reports
- Product Catalog and Stock Movements
- Users and Audit
- Store Settings
- License
- Backup & Restore
- Terminal Management / Host LAN control
- Remote client landing, Remote Read-Only and Remote POS entry/pairing
- Report Artifacts and other internal reporting routes
- LicensingOps Host and Terminal issuance forms

## 4. Source files changed

Client source:

- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/pages/AuditPage.tsx`
- `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx`
- `src/Salryn.App.Web/src/pages/BootstrapSetupPage.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/pages/LoginPage.tsx`
- `src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/RemoteClientLandingPage.tsx`
- `src/Salryn.App.Web/src/pages/StoreSettingsPage.tsx`
- `src/Salryn.App.Web/src/pages/UsersPage.tsx`

Source guards/tests:

- `tests/Salryn.Tests/LanM15CLocalControlChecks.cs`
- `tests/Salryn.Tests/LanM16HostClientIdentityUxChecks.cs`
- `tests/Salryn.Tests/LanM16RcBEntryBoundaryChecks.cs`
- `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs`
- `tests/Salryn.Tests/LocalLicensingCloseoutHardGatesAndTerminalProofChecks.cs`
- `tests/Salryn.Tests/LocalLicensingLanTerminalEntitlementEnforcementChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM3LocalEnforcementBoundaryChecks.cs`
- `tests/Salryn.Tests/PosB1BackupRestoreSafetyChecks.cs`
- `tests/Salryn.Tests/PosM5ProductWriteManagementChecks.cs`
- `tests/Salryn.Tests/PosM6StockInStockAdjustmentChecks.cs`
- `tests/Salryn.Tests/PosManual1FrontendRepairChecks.cs`
- `tests/Salryn.Tests/PosManual1R3FrontendSafetyUxRepairChecks.cs`
- `tests/Salryn.Tests/PosPrinter1WindowsPrinterWiringChecks.cs`
- `tests/Salryn.Tests/PosR1OwnerReportsChecks.cs`
- `tests/Salryn.Tests/PreI1FreshLicenseFirstFlowChecks.cs`
- `tests/Salryn.Tests/V2HostLanControlUxChecks.cs`
- `tests/Salryn.Tests/WebA2LocalApiAuthSessionChecks.cs`
- `tests/Salryn.Tests/WebA3ShellDesignSystemChecks.cs`

`DashboardPage.tsx`, `ProductsPage.tsx`, and `InventoryPage.tsx` were formatted while touched, so their textual diff is larger than their product-copy changes.

## 5. Client-surface copy removed

Normal client surfaces no longer present the ledger-console brand, local host/API endpoint wording, host-local API surface wording, the large Advanced warning wall, internal-preview/coming-soon walls, repo-truth language, regulatory non-claims, backup legal/archive disclaimers, or internal reporting boundary explanations.

The shell now uses the store name, `Salryn POS`, `Signed in as`, and `System Ready`. Diagnostics and incomplete tools are not explained in the normal client shell.

## 6. Navigation changes

- `Detailed Reports` / `Owner Reports` became `Reports`.
- `Host LAN Control` became `Terminal Management`.
- `Terminal Management` and `Report Artifacts` are available only when `VITE_SALRYN_INTERNAL_SURFACES === "true"`.
- Official Setup, Traditional Reporting and other internal reporting surfaces remain out of normal client navigation.
- License and Backup & Restore keep their backend/page authorization but have no visible per-item Admin badge.
- Remote POS and Remote Read-Only routes are not placed in the Host navigation.

## 7. Dashboard / Reports decision

Dashboard remains the owner/home operational summary with today's sales, transactions, low stock, void count, quick actions, and small week/month summaries. Reports remains the read-only operational drilldown for daily, weekly and monthly results, top products, cashier totals, category totals, low stock, and inventory value.

The two pages remain separate; product-boundary explanation text was removed from the client UI.

## 8. License page active-state behavior

Before activation, the manual run showed `Activate Salryn`, the machine code, `salryn.info@gmail.com`, Host/Terminal request guidance, and Import License.

After importing a Host license, the page showed:

- Title: `License`
- `Status: Active`
- Licensed customer
- `Device type: Host POS`
- Machine Code
- Support and update dates
- `Works offline after activation.`

`Replace License` is a secondary button; the import text area is not shown until requested. Terminal instructions do not dominate an active Host license. The incorrect Host-origin write of terminal license JSON was removed.

## 9. Backup / Restore cleanup

The page now focuses on Create Backup, Verify Backup, Restore Backup, the current-database replacement warning, and typed confirmation. Cloud, installer, legal, BIR/eSales, and official-retention disclaimer walls were removed. Restore authorization and safety checks remain intact.

## 10. Report Artifacts visibility decision

Report Artifacts remains an internal route and is hidden from normal client navigation. It becomes discoverable only under the explicit internal-surface environment gate.

## 11. Terminal Management / LAN decision

The internal page is named `Terminal Management`, advanced diagnostics are collapsed, and its client-facing developer walls were reduced. Because the Host-only commercial model and terminal activation path are not coherent end to end, the page stays hidden in normal mode and states internally that Terminal activation is not ready for client use.

No pairing, loopback, authorization, session, or signed-license validation boundary was relaxed.

## 12. Terminal activation status

`NEEDS_SEPARATE_TERMINAL_LANE`

The required Terminal machine-code page, terminal-origin signed-license storage, Host association, pairing request, approval/code, and Cashier/Manager login sequence was not implemented in this cleanup. The current remote API fallback still reads `salryn.terminalLicenseJson` from terminal-origin local storage, but there is no complete UI on that origin to populate and manage it. Host pairing also still has a Host `AllowedLanTerminals` capacity dependency while Host issuance intentionally sets it to zero.

This lane therefore does not claim reliable LAN beta.

## 13. License persistence proof

Manual proof used LicensingOps branch `KIRCH-LICOPS-ISSUANCE-CONTRACT-HARDENING` at `10be2b3e03604ffb32f5e312f6569c38eacf486c` and POS at the implementation SHA above. Development-license bypass variables were cleared.

Fresh proof:

- Existing `.salryn-dev` and `license.json` were archived, not deleted.
- Fresh launch opened `Activate Salryn`.
- Machine code: `SALRYN-MC-56DA-D28D-B387-9237`.
- LicensingOps Host form showed no terminal-count field.
- Issued artifact was flat signed JSON, `LicenseType: Host`, with `AllowedLanTerminals: 0`.
- POS accepted the license, then displayed Bootstrap.
- Store `Kirch Test Store` and test admin `admin@kirch` were created; login opened the Host shell.

After API restart without a reset:

- App opened the login page, not Activate Salryn.
- Test-admin login succeeded.
- License page again showed the active Host license without re-import.

After resetting `.salryn-dev` only:

- Installed test license file remained present.
- License SHA-256 remained `93637637D95443A26AFC3E18A031E2C6FCF97453C238F9E6D5A462C53D3BF19C` before and after the reset.
- App opened Store Setup/Bootstrap, not Activate Salryn.

Conclusion: the installed license persists outside the dev database reset as intended. After proof, test processes were stopped and the pre-test dev data/license were restored from their archives.

## 14. Automated gates and results

Final implementation SHA gates:

- `dotnet build Salryn.sln` — PASS, 0 warnings, 0 errors
- `lic-closeout-hard-gates-and-terminal-proof` — PASS
- `lic-beta-manual-host-terminal-activation` — PASS
- `lic-lan-terminal-entitlement-enforcement` — PASS
- `pre-i1-fresh-license-first-flow` — PASS
- `pos-b1-backup-restore-safety` — PASS
- `web-a3-shell-design-system` — PASS
- `web-a4-native-host-visual-fidelity` — PASS
- `web-b0-read-only-operator-surfaces` — PASS
- Full CoreSmoke — PASS, `SALRYN Core smoke checks passed.`
- `npm --prefix src\Salryn.App.Web run build` — PASS; Vite retained its non-blocking >500 kB chunk warning
- `git diff --check` — PASS

During repair, focused stock movement, reports, manual frontend safety, and Windows-printer gates also passed after stale source assertions were aligned with the client-copy decisions.

## 15. Manual app check results

PASS:

- Fresh database opened Activate Salryn first.
- Hardened LicensingOps generated a Host-only flat license artifact with no terminal-count field.
- Host import, Bootstrap, admin creation, login and Dashboard all worked.
- Normal navigation showed POS Checkout, Sales & Receipts, Dashboard, Product Catalog, Stock Movements, Reports, Users, Audit, Store Settings, License, and Backup & Restore.
- Normal navigation hid Terminal Management, Report Artifacts, Official Setup, Traditional Reporting, Remote POS and Remote Read-Only routes.
- License and Backup & Restore had no per-item Admin badge.
- Shell showed store name, Signed in as, System Ready and Salryn POS; no local API address, API-surface footer, or Advanced wall appeared.
- Active License, Backup & Restore, Reports, and Store Settings displayed the intended concise client copy.
- License survived API restart and `.salryn-dev`-only reset.

Not claimed:

- No end-to-end Terminal activation/pairing/Remote POS proof.
- No installer, cloud activation, BIR/RMO/eSales production, hardware printer-output, or production-client-data proof.

## 16. Remaining risks

1. Terminal activation requires a dedicated implementation lane and terminal-origin persistence design.
2. Host pairing capacity still conflicts with Host-only issuance because it reads Host `AllowedLanTerminals` while hardened Host licenses issue zero.
3. Remote POS cannot be called a reliable beta until a real Terminal license is activated on the terminal origin and the full pairing/login path is manually proven.
4. The production bundle retains a non-blocking Vite chunk-size warning (`538.53 kB` minified main JS).
5. Large formatting-only portions of the Product Catalog, Stock Movements, and Dashboard diffs should be recognized during review.

## 17. Final decision

`NEEDS_SEPARATE_TERMINAL_LANE`

The client-surface cleanup and Host license persistence proof passed. The final decision remains the separate-terminal-lane outcome because the Terminal activation/origin/capacity model is incomplete and was intentionally not disguised by copy or weakened enforcement.

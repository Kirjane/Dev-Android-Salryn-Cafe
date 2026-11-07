# Salryn Final Acceptance Evidence Reconciliation

Date: 2026-11-07 (Asia/Manila)  
Scope: Base Salryn POS with U1 Accounts Receivable / Utang included  
Exact tested implementation SHA: `8310b16a90c49220f1953f35a9f1d8358eb13e08`

This register distinguishes executable behavior, historical installed-app evidence, and genuine physical acceptance. Historical PASS records are retained, but are reused only where the relevant path is unchanged or the current candidate has a focused delta proof.

## Applicability matrix

| Capability | Date / tested source | Hardware or environment | Recorded result | Evidence | Changed-code impact | Verdict |
|---|---|---|---|---|---|---|
| Prior installed desktop launch, WebView2 loader, login, dashboard, checkout | 2026-07-25; report commit `60ca767`; installed RC1 build on Kirch Windows PC | Installed under Program Files; real WebView2 runtime; desktop shortcut | PASS | [Beta RC1 installer/app-shell report](BETA_RC1_INSTALLER_APP_SHELL_REPORT.md) | Loader packaging mechanism is retained and the current stage contains root/RID loaders. Installer publisher/path, LAN startup boundary, application code, and version changed. | DELTA TEST REQUIRED for install/upgrade/launch; loader root-cause proof REUSED. |
| Prior package activation and restart persistence | 2026-07-16; starting SHA `7ea411b`, package report commit `9483a45` | Framework-dependent published folder, loopback HTTP, test Host license | PASS | [I1 package/install proof](I1_PACKAGE_INSTALL_PROOF_REPORT.md) | License validator and U1 entitlement changed; current canonical fixtures and focused gates pass, but controlled fresh canonical manifest is unavailable. | DELTA TEST REQUIRED; canonical proof is EXTERNAL BLOCKER. |
| U1 entitlement, checkout, collections, reversal, aging, store-day dashboard/report parity | 2026-11-07; `7eea7a7` and unchanged at `8310b16` | Disposable v24 SQLite databases; loopback API; Manila boundary clock | PASS | [Execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | This is the changed area and was tested directly: paid/base/Terminal boundary, replay, 23:59/00:01, 205-sale dashboard, reversal restoration. | REUSED (current candidate delta proof). |
| Internal LAN release isolation | 2026-11-07; `7eea7a7`, unchanged at `8310b16` | Previously LAN-enabled disposable v24 DB; actual loopback/LAN sockets on Windows | PASS | [Execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | Direct delta proof: production restart binds loopback only; saved config/terminal record retained; explicit internal-QA opt-in restores LAN listener. | REUSED (current candidate delta proof). No two-device LAN acceptance required for this Host-only release boundary. |
| Backup/restore and future-schema rejection | 2026-07-14/16 historical; repeated 2026-11-07 on candidate | Disposable SQLite DB/package; no client database | PASS | [POS-B1 report](POS_B1_BACKUP_RESTORE_SAFETY_REPORT.md), [portable backup report](PRE_BETA_D_PORTABLE_BACKUP_IMPORT_EXPORT_REPORT.md), [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | U1 adds v24 data; current candidate proved clean/legacy → v24, future v25 rejection, safety backup, restore, and non-mutation. | REUSED (current candidate delta proof). Separate-machine installed upgrade remains physical. |
| Keyboard-wedge scanner behavior | 2026-07-16; scanner SHA `6ae2255`; current full CoreSmoke reached canonical gate after scanner checks | Automated keyboard events/source contracts; no physical scanner | Automated PASS; physical NOT RUN | [Scanner-first report](PRE_BETA_F_SCANNER_FIRST_CHECKOUT_REPORT.md) | Checkout changed for U1, but current checkout/U1 gates pass. Physical keyboard-wedge behavior was never proven. | UNPROVEN physical hardware path. |
| Windows print queue, reprint/failure non-mutation | 2026-07-16; printer SHA `b195650`; current print service files unchanged | Fake adapter and disposable DB; no paper printer | Automated PASS; paper NOT RUN | [Printer hardening report](PRE_BETA_G_PRINTER_HARDENING_REPORT.md), [printer freehand review](POS_PRINTER1_FREEHAND_QUALITY_REVIEW.md) | Core print adapter/service/endpoints are unchanged; U1/LAN/installer changes do not alter printing. | REUSED for software behavior; UNPROVEN for real paper/queue compatibility. |
| Offline local POS runtime | 2026-07-16 package proof plus current loopback/runtime gates | Local API/SQLite; no cloud dependency; prior package was not a network-disconnect observation | Functional local PASS; physical disconnect NOT RUN | [I1 package/install proof](I1_PACKAGE_INSTALL_PROOF_REPORT.md), [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | Current app remains loopback/SQLite. WebView2 bootstrapper needs internet only when runtime is absent. | DELTA TEST REQUIRED on installed candidate for explicit network-disconnected checkout/restart. |
| Real compiled Windows installer | 2026-11-07; `8310b16` | Inno Setup 6.7.3, Windows user-scope compiler | PASS (compile/payload inspection) | [Final readiness decision](SALRYN_S5_FINAL_RELEASE_READINESS_DECISION.md), [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | Current packaging was compiled after publisher/version repair. EXE not installed or distributed. | REUSED for compile integrity; DELTA TEST REQUIRED for clean install/upgrade/uninstall preservation. |
| Canonical Host/Terminal activation including retired-key rejection | Historical 2026-07-19 proof at `5e1820b`; current canonical fixture gate passes | Historical controlled LicensingOps; current machine has no LicensingOps repo/manifest | Historical PASS; current exact-SHA full gate BLOCKED | [Canonical proof report](PRE_I_CANONICAL_LICENSE_ACTIVATION_PROOF_REPORT.md), [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | Licensing clock/test changes occurred. Current public fixtures pass, but the required fresh manifest and retired-key negative asset are controlled externally. | EXTERNAL BLOCKER. |
| Large U1 UI responsiveness | Current runtime covers 205 completed sales; no 100-customer/500-ledger interactive observation | Headless disposable DB only | NOT RUN interactively | [Execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md) | U1 is new and materially affected. | UNPROVEN physical/operator observation. |

## Minimal operator scripts

Use disposable data only. Do not use a client database or real license.

### Installed candidate: clean install, upgrade, offline, and recovery

1. On a QA Windows PC, record the setup SHA-256 and confirm it equals `0D06FE18B6B371DD96AE8BF230534DE9A42D65F570DA8A74F8D51B6244A01034`.
2. If testing upgrade, first back up `%ProgramData%\Salryn` and `%LocalAppData%\Salryn\Licensing`; record the prior installed version and one disposable sale/backup ID.
3. Run `Salryn-POS-BetaRC1-Setup.exe`; confirm publisher text, shortcut, WebView2 launch, first-admin or existing-login path, and no LAN listener on the machine's non-loopback address.
4. With a test license supplied through the controlled licensing process, confirm Base denies U1, paid Host allows U1, and Terminal denies Host POS/U1.
5. Create one cash sale and one U1 sale; collect and reverse the U1 payment; confirm balance/aging returns to the original due date.
6. Create and verify a backup. Disconnect network, restart Salryn, log in, perform a cash sale, reprint, and verify dashboard/owner totals. Restore the disposable backup with the app stopped as instructed; confirm the pre-restore safety backup and restored records.
7. Uninstall and confirm `%ProgramData%\Salryn` remains. Do not distribute the EXE.

### Physical scanner/printer delta

1. Configure a disposable product barcode and a real keyboard-wedge scanner; scan with Enter and Tab suffixes, verify one cart line increments, then scan an unknown code and verify Fast Add without duplicate sale/stock writes.
2. Configure the exact Windows print-queue name; run Test Print, complete one disposable sale, print and reprint, then simulate an unavailable queue. Confirm the sale remains saved, reprint uses the stored snapshot, and no payment/stock/AR row is duplicated.

### Large U1 UI observation

1. Use a disposable v24 database containing at least 100 customers and 500 AR ledger entries.
2. Open customer list, customer detail, aging, collections, owner reports, and dashboard; record load time, filtering behavior, scroll usability, and any error. Do not import client data.

## Canonical proof handoff

`Kirjane/Salryn.LicensingOps` is not visible to the currently authenticated GitHub account and no local checkout or `%TEMP%\salryn-canonical-license-activation-proof-*\manifest.json` exists. Kirch/licensing operator must:

```powershell
cd <authorized Salryn.LicensingOps checkout>
npm ci
npm run proof:canonical-activation
$env:SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST = '<absolute path to generated manifest.json>'
cd C:\Salryn
dotnet run --project tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj -c Release -- --only pre-i-canonical-license-activation-proof
dotnet run --project tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj -c Release --no-build
```

Record the LicensingOps commit, manifest SHA-256, Host/Terminal proof filenames (not their contents), retired-key negative fixture result, Salryn SHA, and exit codes. Delete temporary signed proof files per LicensingOps policy. Never commit or paste licenses/private keys.

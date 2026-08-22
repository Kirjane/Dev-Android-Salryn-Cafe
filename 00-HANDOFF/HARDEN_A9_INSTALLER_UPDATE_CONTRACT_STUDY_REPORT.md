# HARDEN-A9 — Installer / Update Contract Study Report

## 1. Branch / Base

Branch: `KIRCH-HARDEN-A9-INSTALLER-UPDATE-CONTRACT-STUDY`

Base branch: `KIRCH-HARDEN-A8-REPORTING-EXPORT-CODE-SIDE-READINESS`

Base SHA: `bd18eefb605e92f817919ee90cd2dcb1c9423741`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the Freehand final response or repository branch head after this report commit for the exact final SHA.

## 2. Scope

Milestone: `HARDEN-A9 — Installer / Update Contract Study`

This is a study and contract-readiness lane. It defines installer/update preservation and overwrite rules for future implementation. It does not implement installer behavior, build an installer, create a launcher, promote branches, or change runtime app behavior.

## 3. Files changed

- `00-HANDOFF/HARDEN_A9_INSTALLER_UPDATE_CONTRACT_STUDY_REPORT.md`

No source code, runtime, UI, migration, installer, launcher, or CoreSmoke files were changed by A9.

## 4. Installer/update inventory

### Current hardened branch inventory

Persistent/local data owners observed from current branch source:

- `DbPaths` owns production/development SQLite/data roots.
- `SalrynLocalDataContract` documents preservation expectations for database, backups, exports, logs, receipts, config, license, license backups, and native PDF report artifacts.
- `LocalLicenseFileStore` owns local license file storage under LocalApplicationData, safe license file names, and backup-before-replace behavior.
- `NativePdfArtifactRootPolicy` owns controlled native PDF report artifact roots, safe relative artifact paths, caller-path rejection, and path traversal rejection.
- `NativePdfArtifactStore` writes PDF artifacts plus sidecar manifest JSON, PDF hash, dataset hashes, renderer metadata, relative paths, and retention note.
- `DefaultDatabaseInitializationFactory` and `MigrationRunner` own database initialization and ordered SQLite migrations.
- `LocalApiApplicationFactory` resolves React `dist` from a repo-shaped layout and exposes `/api/system/web-dist` for local diagnostics.
- `WebDistDiagnostics` checks `index.html`, hashed JS/CSS assets, and `salryn-build-stamp.txt`.

### Protected installer lane inventory, read-only

`KIRCH-INSTALLER-M1` contains installer study/scaffold work that is useful for later but must not be merged in this lane:

- installer documentation under `00-HANDOFF/INSTALLER_*`
- WiX files under `installer/`
- `Salryn.Launcher`
- `tools/installer/Build-SalrynMsi.ps1`
- installer scaffold checks
- solution updates for launcher/installer projects

Connector compare showed the protected installer lane is diverged from current A8 hardening state. The useful contract ideas are reusable later, but direct merge is not appropriate before A10/manual proof planning.

## 5. Persistent data contract

The future installer/update flow must preserve operator/customer-owned local data. Data that must survive install/update:

- SQLite production database: `ProgramData/Salryn/data/salryn.db`
- database backups: `ProgramData/Salryn/backups`
- export files: `ProgramData/Salryn/exports`
- receipts/output files: `ProgramData/Salryn/receipts`
- local config: `ProgramData/Salryn/config`
- audit records if DB-backed or export-backed
- local license file: `LocalApplicationData/Salryn/Licensing/license.json`
- local license backups: `LocalApplicationData/Salryn/Licensing/*.bak.json`
- native PDF artifacts: `LocalApplicationData/Salryn/Reports/NativePdf`
- native PDF manifest JSON and hash metadata under the artifact root
- frontend build stamp and static assets inside the installed app folder, but these are application assets, not user data

Logs are treated differently: they may be rotated or pruned by a later explicit policy, but updates should not indiscriminately wipe logs without a documented support decision.

## 6. Path preservation matrix

| Category | Current source/file owner | Current path or path policy | Controlled root? | Caller-controlled? | Installer action | Manual proof required? | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Database | `DbPaths` | `ProgramData/Salryn/data/salryn.db` | Yes | No | Preserve; backup before migration | Yes | Never package or delete production DB. |
| Database backups | `DbPaths` / backup services | `ProgramData/Salryn/backups` | Yes | No | Preserve | Yes | Installer may create root but must not wipe contents. |
| Exports | `DbPaths` | `ProgramData/Salryn/exports` | Yes | Unknown per export caller; current root is controlled | Preserve | Yes | Future export cleanup must be operator-driven. |
| Logs | `DbPaths` | `ProgramData/Salryn/logs` | Yes | No | Preserve or rotate by explicit policy only | Yes | Do not wipe blindly during upgrade. |
| Receipts/output files | `DbPaths` | `ProgramData/Salryn/receipts` | Yes | No | Preserve | Yes | Customer/business artifacts. |
| Config | `DbPaths` | `ProgramData/Salryn/config` | Yes | No | Preserve; backup before incompatible changes | Yes | Never reset settings silently. |
| Local license | `LocalLicenseFileStore` | `LocalApplicationData/Salryn/Licensing/license.json` | Yes | File name safe-checked; no path input accepted | Preserve | Yes | Do not overwrite with blank/unlicensed state. |
| License backups | `LocalLicenseFileStore` | `LocalApplicationData/Salryn/Licensing/*.bak.json` | Yes | No | Preserve | Yes | Created during replacement. Keep for recovery. |
| Native PDF report artifacts | `NativePdfArtifactRootPolicy` / `NativePdfArtifactStore` | `LocalApplicationData/Salryn/Reports/NativePdf` | Yes | No caller path accepted | Preserve | Yes | Contains PDF artifacts and manifests. |
| Manifest JSON | `NativePdfArtifactStore` | Relative manifest path under controlled report root | Yes | No | Preserve | Yes | Sidecar proof for hashes/metadata. |
| Temp license replacement files | `LocalLicenseFileStore` | Same controlled license root, `license-*.tmp` | Yes | No | Clean up only if orphaned and verified safe | Yes | Replacement finally block deletes current temp file. |
| Frontend dist | `LocalApiApplicationFactory` / React build | repo-shaped `src/Salryn.App.Web/dist` currently; installer lane stages `dist` under Program Files | Yes when installed | No | Overwrite app asset copy on update | Yes | Must prove installed layout resolves correctly. |
| App binaries | publish/installer | future `Program Files/Salryn` | Yes | No | Overwrite on update | Yes | Safe target for installer replacement. |
| Start Menu shortcut | installer lane only | protected M1 planned Start Menu entry | Yes | No | Create/update in installer implementation only | Yes | A9 does not implement. |
| Dev data | `DbPaths.CreateDevelopment` | repo `.salryn-dev` | Yes, dev-only | No | Never package; never migrate into production | No for production, yes for dev testing | Must not enter MSI. |

## 7. What installer may overwrite

Future installer may overwrite:

- application binaries under the install folder
- published DLLs/runtime files under the install folder
- frontend static assets under the install folder
- bundled app documentation or static support files
- Start Menu shortcut metadata
- installer registry/product metadata
- non-user templates if a future lane explicitly introduces them

The installer may not treat user data as application binaries.

## 8. What installer must preserve

Future installer must preserve:

- production SQLite database
- `ProgramData/Salryn/data`
- `ProgramData/Salryn/backups`
- `ProgramData/Salryn/exports`
- `ProgramData/Salryn/receipts`
- `ProgramData/Salryn/config`
- logs unless explicit support cleanup policy exists
- local license file
- local license backups
- report PDF artifacts
- report manifest JSON
- hash metadata
- audit/business records

## 9. What installer must never touch without backup

Must never be overwritten, deleted, truncated, or migrated without backup and explicit versioned path:

- `ProgramData/Salryn/data/salryn.db`
- `ProgramData/Salryn/config`
- `ProgramData/Salryn/backups`
- `LocalApplicationData/Salryn/Licensing/license.json`
- `LocalApplicationData/Salryn/Licensing/*.bak.json`
- `LocalApplicationData/Salryn/Reports/NativePdf`
- report manifests/hashes

If backup fails, future installer/update should stop rather than continue with destructive changes.

## 10. License preservation contract

Current licensing contract is local/offline-first:

- local runtime must work offline
- local verification must not require network access
- private signing keys must not be in the repo
- local runtime must not depend on recurring billing state
- future external issuer can create signed license files later

Future installer/update rule:

- preserve existing `license.json`
- preserve `*.bak.json` license backups
- do not overwrite license with blank/unlicensed state
- do not require Cloud to run installed app
- do not generate or store private signing keys
- do not reset licensed-machine state accidentally
- do not bind update success to online payment/subscription status
- if update entitlement exists later, it may gate access to update packages, not daily offline app usage unless Kirch explicitly scopes a new licensing policy

## 11. Database/migration contract

Current database state:

- production root is `ProgramData/Salryn`
- database is `data/salryn.db`
- migrations are ordered by integer version
- `schema_migrations` records applied versions
- migrations run transactionally one migration at a time

Future update rule:

- preserve DB before app upgrade
- run schema migrations only through versioned migration system
- never downgrade schema silently
- never wipe/recreate DB as a repair strategy
- stop and surface a clear error if migration fails
- prefer DB backup before any migration that changes schema or high-risk data
- manual proof must include upgrade from realistic old DB copy, backup restore, and writability check

Current gap: no installer-level backup-before-migration orchestration is proven in this lane.

## 12. Reporting/export artifact preservation contract

A8 state preserved:

- native PDF remains primary
- Crystal remains parked compatibility
- no printer integration
- no caller output path
- no cloud destination
- no SAP package
- no `.rpt` files
- no reporting/RMO/BIR/eSales semantic change

Future installer/update rule:

- preserve native PDF artifact root
- preserve manifest JSON
- preserve hash metadata
- preserve report artifacts unless operator/admin cleanup workflow exists
- do not rewrite report history during install/update
- do not introduce arbitrary output path selection
- do not introduce cloud report upload as part of installer/update

## 13. Frontend/WebView asset update contract

Current branch behavior:

- local API serves static web assets when `ResolveWebDistPath()` finds `src/Salryn.App.Web/dist` under a repository-shaped root
- `WebDistDiagnostics` inspects `index.html`, hashed JS/CSS assets, and `salryn-build-stamp.txt`
- `index.html` gets no-cache headers
- `/api/system/web-dist` exposes local diagnostics behind loopback guard

Future installer/update rule:

- installed layout must make frontend `dist` resolvable without relying on the source repository root, or the resolver must be changed in a dedicated implementation lane
- update should replace frontend `dist` atomically with app binaries
- manual proof must check the build stamp after update
- stale bundle detection should include visible build/version evidence before beta
- WebView/browser cache behavior must be tested after update

Current blocker: installed `Program Files` layout and source-repo-shaped resolver must be reconciled by future installer implementation. Protected Installer M1 already identified this as a packaging gap.

## 14. Version/update entitlement findings

Observed version/update facts:

- installer lane proposes SemVer product version, Windows-compatible MSI version, Git tag, MSI asset name, and SHA-256 sidecar
- frontend build stamp already exists via `salryn-build-stamp.txt`
- web dist diagnostics can expose build stamp and asset names
- database schema has integer migration versions
- local licensing policy supports future external issuer signed files without network-dependent runtime

Before beta, recommended minimum version contract:

- one documented app/product version source
- visible app/build version in UI or diagnostics
- web build stamp visible in support path
- migration compatibility note per release
- release asset SHA-256
- update eligibility policy separated from daily offline runtime usage

Do not implement online update, forced auto-update, payment sync, or subscription gating in the local runtime.

## 15. Relationship to `KIRCH-INSTALLER-M1`

`KIRCH-INSTALLER-M1` was inspected read-only only.

Findings:

- It is based from protected/stable history and now diverges from current hardening branches.
- It contains WiX installer scaffold, launcher, build script, installer reports/checklists, and Start Menu packaging decisions.
- It documents ProgramData preservation and no auto-update/cloud/licensing enforcement.
- It has a packaging policy that does not package production DB/dev data.
- It includes a launcher and Start Menu behavior that A9 intentionally does not implement.

Reuse later:

- ProgramData preservation principles
- no DB packaging rule
- WiX package policy ideas
- build script/checklist structure
- unsigned/internal release notes model

Do not merge yet:

- The lane is behind current A8 hardening/reporting/licensing work.
- It introduces launcher/installer implementation that is explicitly out of A9 scope.
- It must be reconciled after A10 lane register cleanup and manual proof planning.

## 16. Dev launcher parking decision

Dev launcher remains parked.

Decision:

- no desktop shortcut created
- no dev launcher created
- no launcher code added
- no Start Menu implementation added
- no installer M1 branch merge performed

Reason: branch consolidation/promotion is not done, A9 is study-only, A10 lane register cleanup is next, and manual proof pack has not run.

## 17. Future installer implementation blockers

Blockers before actual installer/update implementation:

1. installed `dist` resolution must be solved without relying on repository-shaped layout
2. real Windows ProgramData ACL and non-admin first-run proof is required
3. DB backup-before-migration orchestration must be designed or explicitly deferred
4. license preservation across upgrade/uninstall must be proven
5. report artifact preservation across upgrade/uninstall must be proven
6. app running/locked-file behavior during upgrade must be defined
7. downgrade handling must be explicit
8. update rollback/recovery policy must be defined
9. version source must be unified or documented
10. installer lane divergence must be reconciled after A10/manual proof planning

## 18. Manual proof requirements

Manual proof still required later:

- actual install
- upgrade over existing install
- preserve SQLite DB
- preserve local license
- preserve license backups
- preserve config
- preserve report artifacts
- preserve logs/audit where intended
- frontend stale-bundle check
- WebView/browser launch after update
- Windows ProgramData/AppData path proof
- backup-before-replace proof
- rollback/recovery proof if supported
- operator install experience
- uninstall does not delete protected user data
- downgrade blocked or handled safely

## 19. Commands / gates run

No local PowerShell commands were run by this ChatGPT connector session.

Required local commands still need Sol/Codex/local verification:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-HARDEN-A9-INSTALLER-UPDATE-CONTRACT-STUDY
git status
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-INSTALLER-M1

dotnet restore Salryn.sln
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m3-local-enforcement-boundaries
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only harden-a1-a6-pre-beta-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

Connector actions performed:

- Fetched accepted A8 report from `bd18eefb605e92f817919ee90cd2dcb1c9423741`.
- Created branch `KIRCH-HARDEN-A9-INSTALLER-UPDATE-CONTRACT-STUDY` from accepted A8 SHA.
- Fetched selected path, license, reporting, migration, web-dist, and installer-lane files.
- Compared current A8 state against protected `KIRCH-INSTALLER-M1` read-only.
- Added this report only.

## 20. Required report answers

| Question | Answer |
| --- | --- |
| Did you implement installer behavior? | No |
| Did you build an installer? | No |
| Did you modify `KIRCH-INSTALLER-M1`? | No |
| Did you create a dev launcher? | No |
| Did you promote/unify branches? | No |
| Did you change runtime app behavior? | No |
| Did you define preservation rules? | Yes |
| Did you define update overwrite rules? | Yes |
| Did you define manual proof requirements? | Yes |

## 21. Non-claims

This lane does not claim:

- installer readiness
- beta readiness
- production release readiness
- Cloud readiness
- online activation
- payment/subscription update system
- dev launcher readiness
- branch promotion
- manual install proof
- Windows upgrade proof
- BIR approval
- legal approval
- eSales production transmission
- local build/test/smoke GREEN from this ChatGPT connector session

## 22. Status

HARDEN-A9 is a connector-authored contract study candidate only.

Sol/Codex/local verification and Maintainer Chat remote-truth verification are required before this lane is accepted or called GREEN.

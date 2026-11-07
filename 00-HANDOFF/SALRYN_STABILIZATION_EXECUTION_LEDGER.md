# Salryn Stabilization Execution Ledger

Date: 2026-11-07 (Asia/Manila)

Branch: `codex/issue-9-s0-s5-rc`

Source branch point: `f85e910902622f950230095a3dcf00e43f09b46b`

Candidate implementation/evidence SHA: `8310b16a90c49220f1953f35a9f1d8358eb13e08`.

Review continuation: PR #10 review checkpoint comment `6702228302` was addressed on 2026-11-07 PHT. `origin/main` was merged without force-push; the actual conflict was `README.md`. The resolution retains the accepted company attribution, RC boundaries, and U1 AR scope.

Draft review: GitHub PR #10 (`codex/issue-9-s0-s5-rc` → `main`). The PR is intentionally draft and does not authorize merge or release.

## Phase status

| Phase | Status | Evidence / disposition |
|---|---|---|
| S0 | PASS | Immutable SHAs, 6/20 divergence, main-only README delta, required handoffs, PR #8, and product boundaries verified. Baseline file committed with the candidate. |
| S1 | PASS | Dedicated U1 branch; reviewed main README and PR #8 branding/LAN changes integrated. Release C# build and React production build pass. |
| S2 | PASS | AR reversal aging, Manila store-day, adjustment replay boundary, dashboard 200-row/void defect, report parity, checkout/collection replay, stock, identity, and reversal gates pass. |
| S3 | PASS (automated) | Schema 1–24 upgrade/future-25 rejection, isolated backup/restore, entitlement/role/Terminal gates, dependency audit, and host-local official-setup boundary pass. No migrations 25–26 imported. |
| S4 | EXECUTABLE IN-REPO GATES PASS; CONTROLLED PROOF + MINIMAL PHYSICAL QA BLOCKED | Full CoreSmoke passes through `pre-i-license-key-rotation-canonical-beta`, then stops at `pre-i-canonical-license-activation-proof`. The authenticated Kirch account cannot see `Kirjane/Salryn.LicensingOps`; no local checkout/temp manifest exists. Focused U1/checkout/entitlement/LAN/security/infrastructure/schema gates pass. Historical and current evidence is reconciled in `SALRYN_FINAL_ACCEPTANCE_EVIDENCE_RECONCILIATION.md`; two-device LAN QA is not an initial Host-release requirement. `dotnet test` is not counted because `Salryn.Tests` is a class library. |
| S5 | REAL SETUP EXE PASS; INSTALLED DELTA + RELEASE APPROVAL BLOCKED | Inno Setup 6.7.3 was safely installed per-user and compiled the actual setup EXE. Identity, payload, forbidden-content scan, and hashes pass. Clean install/upgrade/offline/hardware observations and Kirch approval remain external/physical gates. |

## Defects repaired

- AR-01: allocation now treats `CollectionReversal` as restoration of the original collection credit, excludes undated reversal debits from invoice aging, restores original oldest due date, and keeps AR summary/customer/owner-report totals aligned.
- AR-02: AR summary, collections-today, owner reports, and dashboard use a deterministic Asia/Manila store day. A 23:59/00:01 fixture proves the boundary.
- AR-03: replay-unsafe manual adjustment API is deliberately unavailable in the release surface; two authorized identical submissions return 404 and make no ledger mutation. Historical adjustment rows and the internal service remain intact.
- FIN-04: dashboard reads every completed sale page, excludes voids, uses decimal accumulation, and matches owner reports with 205 completed sales plus a void.
- S3 security: official setup endpoints now enforce loopback before permission checks.
- Dependency hygiene: npm audit repaired 7 build-chain advisories; explicit `System.Security.Cryptography.Pkcs` 10.0.14 removes the transitive high/moderate advisories.
- Source guards were aligned to accepted U1 receipt printing, entitlement-aware navigation, schema v24, and LAN future-feature language.
- LIC-01: LAN terminal proof validation now accepts an injectable UTC clock, keeping host and terminal validation deterministic and preventing fixed-date proof tests from silently testing expiry instead of store/host mismatch behavior.
- DB-01: portable-backup future-schema proof now uses v25, one beyond the supported v24 catalog, instead of colliding with an existing migration row.
- LAN-RC-01: release startup now suppresses every non-loopback listener unless the explicit internal-QA capability is enabled, including when an older database persisted `RemoteTransportEnabled=true`. Explicit `--lan-bind` also fails closed without `--internal-lan-qa`. Saved LAN configuration and terminal registrations are retained for reversible QA; authorization gates are unchanged.
- PKG-01: installer publisher/install-path identity now uses Kirjane Information Technology Services, and the build's `-Version` value is passed to Inno Setup rather than diverging from a fixed script version. A source guard prevents regression to the superseded publisher.

## Fresh command evidence

| Command | Result | Notes |
|---|---|---|
| `git diff --check` | PASS | No whitespace errors. |
| `dotnet restore Salryn.sln` | PASS | Fresh clone assets restored. Initial `--no-restore` attempt failed as expected because assets did not yet exist. |
| `dotnet build Salryn.sln -c Release --no-restore` | PASS | 0 errors; pre-existing nullable/analyzer warnings remain. |
| `dotnet test Salryn.sln -c Release --no-restore` | PASS / 0 executed | Exit 0 with no test execution output; class-library limitation explicitly retained. |
| `npm --prefix src/Salryn.App.Web ci` | PASS | Lockfile reproducible install. |
| `npm --prefix src/Salryn.App.Web audit fix` | PASS | 0 npm vulnerabilities after repair. |
| `npm --prefix src/Salryn.App.Web run build` | PASS | Vite 6.4.5 production build; only the existing >500 kB chunk warning. |
| `dotnet list Salryn.sln package --vulnerable --include-transitive` | PASS | No vulnerable NuGet packages after the Pkcs override. |
| CoreSmoke `--only u1-accounts-receivable` | PASS | Runtime financial, authorization, boundary, backup/restore, 205-sale dashboard, and adjustment retry assertions executed. |
| CoreSmoke `--only u1-entitlement-gating` | PASS | Base denied, paid Host allowed, Terminal denied. |
| CoreSmoke `--only v2-host-checkout` | PASS | Host checkout validation and durable duplicate reconciliation pass. |
| InfrastructureSmoke `pos-phase-b-regression` | PASS | Store-day, repositories, admin operations, backup guardrails, and paging pass in disposable data. |
| CoreSmoke `--only rmo-bir-qa1-real-db` | PASS | Clean/legacy upgrade to v24, future v25 rejection, backup/restore and non-mutation pass in isolated temp root. |
| CoreSmoke `--only lic-lan-terminal-entitlement-enforcement` | PASS | Deterministic terminal validation, Host/Terminal type separation, store/host/machine mismatch, capacity, and source guards pass. |
| CoreSmoke `--only pre-beta-d-portable-backup-import-export` | PASS | Export/import, hash rejection, future-v25 rejection, and restore archive pass. |
| CoreSmoke `--only v2-lan-bind-truth` | PASS (exit 0) | Disposable previously-LAN-enabled v24 database restarted in release mode: actual listener set was loopback-only, LAN TCP connection failed, loopback `/health` returned 200, and saved config plus terminal registration remained intact. Explicit internal-QA mode then bound the assigned LAN address and LAN `/health` returned 200. |
| CoreSmoke `--only sec-a1-local-api-webview-security` | PASS (exit 0) | Focused security runner added and executed after the listener-boundary change. |
| Post-review CoreSmoke `--only u1-accounts-receivable` | PASS (exit 0) | U1 AR financial, authorization, reversal, dashboard, and replay gates retained after main reconciliation. |
| Post-review CoreSmoke `--only u1-entitlement-gating` | PASS (exit 0) | U1 paid Host/base/Terminal boundary retained. |
| Post-review CoreSmoke `--only v2-host-checkout` | PASS (exit 0) | Loopback Host checkout remains operational and idempotent. |
| Post-review `dotnet build Salryn.sln -c Release --no-restore` | PASS (exit 0) | Exact tested implementation SHA `7eea7a7bb7b15d119e0dbca217bd2cac46468816`; 0 warnings, 0 errors. |
| Post-review `npm --prefix src/Salryn.App.Web run build` | PASS (exit 0) | Vite 6.4.5 production build; existing chunk-size advisory only. |
| Post-review `dotnet list Salryn.sln package --vulnerable --include-transitive` | PASS (exit 0) | No vulnerable NuGet packages. |
| Post-review `npm --prefix src/Salryn.App.Web audit --audit-level=high` | PASS (exit 0) | 0 vulnerabilities. |
| Post-review `git diff --check` | PASS (exit 0) | No whitespace errors before candidate commit. |
| Full CoreSmoke | BLOCKED at external proof gate | Rerun at exact tested implementation SHA `7eea7a7bb7b15d119e0dbca217bd2cac46468816`. All gates—including the new actual-socket LAN release-boundary proof—execute successfully through canonical key rotation. Execution then stops at `pre-i-canonical-license-activation-proof`: no external proof manifest is available. The gate explicitly requires `npm run proof:canonical-activation` in `Salryn.LicensingOps` or `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST`; neither the external repository nor signing proof is present locally. This evidence was not fabricated or bypassed. |
| Installer staging | PASS | `Build-SalrynBetaRc1Installer.ps1` produced `artifacts/installer/staging/Salryn` with WebView2Loader present. |
| `winget install --id JRSoftware.InnoSetup --exact --scope user --silent ...` | PASS (exit 0) | Official winget package installed Inno Setup 6.7.3 without elevation at `%LocalAppData%\Programs\Inno Setup 6\ISCC.exe`; package-manager download hash verification passed. |
| `Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -InnoSetupCompilerPath <per-user ISCC>` | PASS (exit 0) | Actual EXE compiled at `artifacts/installer/setup/Salryn-POS-BetaRC1-Setup.exe`; Inno compile successful. |
| Installer identity/payload inspection | PASS | Product `Salryn POS`; version `0.1.0-rc1`; company `Kirjane Information Technology Services`; size 24,110,051 bytes. WebView2 bootstrapper, root/RID loader, and production `dist/index.html` present. Database/license/private-key/test artifact count 0; private marker count 0. |
| CoreSmoke `--only pre-beta-c-store-logo-branding` | PASS (exit 0) | Current packaging publisher/version source guards and v24 branding behavior pass at implementation SHA `8310b16a90c49220f1953f35a9f1d8358eb13e08`. |

Raw stdout/stderr is preserved in the Codex task transcript. The repository intentionally does not commit volatile machine-path logs or disposable SQLite files.

## Required human actions / blockers

- `BLOCKED: physical operator/maintainer action required` — run the short disposable-data script in `SALRYN_FINAL_ACCEPTANCE_EVIDENCE_RECONCILIATION.md` for the newly compiled setup: clean install or upgrade, WebView2 launch, controlled Base/U1/Terminal states, offline checkout/restart, backup/restore, uninstall data preservation, and large-U1 UI observation.
- `BLOCKED: physical hardware action required` — real keyboard-wedge scanner and Windows print-queue paper/reprint/failure observations using the short script in the reconciliation report. Software invariants are already proven and are not to be repeated indiscriminately.
- `BLOCKED: controlled LicensingOps asset required` — the authenticated GitHub account cannot resolve `Kirjane/Salryn.LicensingOps`, and no local checkout or proof manifest exists. Generate the canonical proof in the authorized environment, set `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST`, and rerun canonical plus full CoreSmoke. Exact commands are in the reconciliation report.
- `BLOCKED: Kirch approval required` — exact candidate SHA acceptance, pilot signoff, merge, and any public/client distribution.

## Next action

Provide the controlled canonical manifest, execute only the minimal physical delta scripts, and obtain Kirch's written exact-SHA decision. The real installer remains local and unpublished. Keep PR #10 draft; do not merge or distribute.

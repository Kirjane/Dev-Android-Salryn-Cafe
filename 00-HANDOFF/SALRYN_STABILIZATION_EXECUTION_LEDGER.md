# Salryn Stabilization Execution Ledger

Date: 2026-11-07 (Asia/Manila)

Branch: `codex/issue-9-s0-s5-rc`

Source branch point: `f85e910902622f950230095a3dcf00e43f09b46b`

Candidate implementation/evidence SHA: `7b9d1a753b170e20cd67bfa88abc3d480d9992b2`.

Draft review: GitHub PR #10 (`codex/issue-9-s0-s5-rc` → `main`). The PR is intentionally draft and does not authorize merge or release.

## Phase status

| Phase | Status | Evidence / disposition |
|---|---|---|
| S0 | PASS | Immutable SHAs, 6/20 divergence, main-only README delta, required handoffs, PR #8, and product boundaries verified. Baseline file committed with the candidate. |
| S1 | PASS | Dedicated U1 branch; reviewed main README and PR #8 branding/LAN changes integrated. Release C# build and React production build pass. |
| S2 | PASS | AR reversal aging, Manila store-day, adjustment replay boundary, dashboard 200-row/void defect, report parity, checkout/collection replay, stock, identity, and reversal gates pass. |
| S3 | PASS (automated) | Schema 1–24 upgrade/future-25 rejection, isolated backup/restore, entitlement/role/Terminal gates, dependency audit, and host-local official-setup boundary pass. No migrations 25–26 imported. |
| S4 | EXECUTABLE IN-REPO GATES PASS; EXTERNAL PROOF + PHYSICAL QA BLOCKED | Full CoreSmoke passes through `pre-i-license-key-rotation-canonical-beta`, then stops at `pre-i-canonical-license-activation-proof` because this machine has neither a `Salryn.LicensingOps` proof manifest nor `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST`. Focused U1/checkout/entitlement/infrastructure/schema gates pass. `dotnet test` exits 0 but executes no framework tests because `Salryn.Tests` is a class library. Installed WebView2, printer/scanner, reinstall/upgrade, and two-device checks require a physical operator. |
| S5 | STAGING PASS; SETUP EXE + RELEASE APPROVAL BLOCKED | Installer payload staging succeeds and is hashed. Setup compilation is blocked because Inno Setup 6 (`ISCC.exe`) is not installed/configured. Draft PR may be opened; merge and public/client release remain blocked on Kirch approval. |

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
| Full CoreSmoke | BLOCKED at external proof gate | All gates execute successfully through canonical key rotation. Execution then stops at `pre-i-canonical-license-activation-proof`: no external proof manifest is available. The gate explicitly requires `npm run proof:canonical-activation` in `Salryn.LicensingOps` or `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST`; neither the external repository nor signing proof is present locally. This evidence was not fabricated or bypassed. |
| Installer staging | PASS | `Build-SalrynBetaRc1Installer.ps1` produced `artifacts/installer/staging/Salryn` with WebView2Loader present. |
| Installer setup compile | BLOCKED | `-BuildInstaller` reaches staging, then reports Inno Setup 6 `ISCC.exe` missing. Install Inno Setup 6 and set `INNO_SETUP_ISCC`, or pass `-InnoSetupCompilerPath`. |

Raw stdout/stderr is preserved in the Codex task transcript. The repository intentionally does not commit volatile machine-path logs or disposable SQLite files.

## Required human actions / blockers

- `BLOCKED: physical operator/maintainer action required` — install the generated setup on a clean Windows QA machine; verify WebView2 launch, first admin, base/U1/expired/downgrade/reactivation states, all three roles, offline checkout, restart, backup/restore, and upgrade without data loss.
- `BLOCKED: physical operator/maintainer action required` — real printer/PDF target, barcode scanner, receipt print failure/reprint, and 100+ customer/500+ ledger UI responsiveness.
- `BLOCKED: Kirch/licensing operator action required` — generate the canonical activation proof from `Salryn.LicensingOps` (`npm run proof:canonical-activation`) and provide its manifest via `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST`, then rerun full CoreSmoke. The required retired-key negative fixture is intentionally not present in this repository.
- `BLOCKED: build-machine action required` — install Inno Setup 6 and expose `ISCC.exe` via `INNO_SETUP_ISCC` or `-InnoSetupCompilerPath`, then rerun the installer build. Staged hashes: `Salryn.Desktop.WebView.exe` = `6DEAE24F7C9418C781F1F8D6B628A7E6DDFF90A3BCA988921EBCE0E7D4E54D5A`; `Salryn.Api.Local.exe` = `D0FEC855003F44658AEE86764B8919587AB780A62297C0FE270F6419A7EB6B27`; WebView2 bootstrapper = `23A55FBFF920C0F99887848CFC25125F8F915DF35638E01BEB8F8FA9B5A0BC51`.
- `BLOCKED: Kirch approval required` — exact candidate SHA acceptance, pilot signoff, merge, and any public/client distribution.

## Next action

Provide the canonical activation proof manifest and Inno Setup compiler, rerun the two blocked gates, then perform physical/hardware QA. The dedicated branch may be reviewed in a draft PR. Do not merge or publish without Kirch approval.

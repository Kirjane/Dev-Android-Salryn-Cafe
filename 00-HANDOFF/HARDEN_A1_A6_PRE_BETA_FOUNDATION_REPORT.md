# HARDEN-A1-A6 — Pre-Beta Foundation Report

## 1. Branch / Base

Branch: `KIRCH-HARDEN-A1-A6-PRE-BETA-FOUNDATION`

Base branch: `KIRCH-LICENSING-M3-LOCAL-ENFORCEMENT-BOUNDARIES`

Base SHA: `0c805899f5a83135ec3be582edf3dcb3359263cc`

M3 base expectation: M3 remains the accepted Sol-verified automated code/integration base supplied by Kirch.

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the Freehand final response or repository branch head after this report commit for the exact final SHA.

## 2. Scope

Milestone: `HARDEN-A1-A6 — Pre-Beta Codebase Evidence / Security / Licensing / Data Foundation`

This was a narrow pre-beta hardening pass, not a beta start, Cloud start, frontend polish sprint, installer implementation, or RMO/reporting reopen.

## 3. Files changed

- `00-HANDOFF/HARDEN_A1_A6_PRE_BETA_FOUNDATION_REPORT.md`
- `src/Salryn.Core/Services/Licensing/LocalLicenseContractPolicy.cs`
- `src/Salryn.Infrastructure/Paths/SalrynLocalDataContract.cs`
- `tests/Salryn.Tests/HardenA1A6PreBetaFoundationChecks.cs`
- `tests/Salryn.Tests/SmokeProof/SmokeProofCatalog.cs`
- `tests/Salryn.Tests/SmokeProof/SmokeProofClass.cs`
- `tests/Salryn.Tests/SmokeProof/SmokeProofRecord.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## 4. Subpass summary

### HARDEN-A1 — Evidence/CoreSmoke classification

Added `SmokeProofClass` with these proof classes:

- `SourceInspection`
- `CompileBoundary`
- `SyntheticBehavior`
- `FileSystemBehavior`
- `RuntimeComposition`
- `ManualProofRequired`

Added `SmokeProofRecord` and `SmokeProofCatalog` so pre-beta handoffs can distinguish what a check proves and does not prove.

Classified high-risk-overclaim lanes including licensing M1/M2/M3, remote POS/LAN, production report export, installer packaging, and RMO/BIR/eSales external compliance.

### HARDEN-A2 — Test structure cleanup

Added `HardenA1A6PreBetaFoundationChecks.Run()` as a focused source/contract check rather than rewriting CoreSmoke.

Registered the check in full CoreSmoke immediately after licensing M1/M2/M3 checks.

Added `--only harden-a1-a6-pre-beta-foundation` to CoreSmoke.

This improves proof readability without deleting, renaming, or thinning the existing smoke system.

### HARDEN-A3 — Security source audit

Added source checks for the current licensing slice and controlled report artifact path seam.

The check asserts the M3 licensing slice does not introduce obvious internet/payment/key runtime dependencies such as:

- `HttpClient`
- `Vercel`
- `OnlineActivation`
- `PrivateSigningKey`
- `PrivateKey`
- `Stripe`
- `PayPal`

It also checks that native PDF artifact root policy still exposes controlled path resolution, caller-path rejection, and path traversal rejection.

No security redesign was performed.

### HARDEN-A4 — Permission model audit

Added guard expectation checks for:

- `GET /api/license/status`
- `GET /api/license/entitlements`
- `POST /api/license/install`

The checks assert expected source markers for:

- loopback/local operator guard
- authenticated session guard
- `Settings.Manage` permission guard for install
- 401 authentication-required path
- 403 permission-denied path
- deterministic `License.FeatureDenied` API license denial

No permission model redesign was performed.

### HARDEN-A5 — Licensing contract hardening

Added `LocalLicenseContractPolicy` as non-executable policy-as-code.

The policy asserts:

- local runtime must work offline
- local verification must not require network access
- private signing material must not be allowed in the repo
- local runtime must not depend on recurring billing state
- future external issuer workflows may create signed license files later

This preserves the intended one-time-bought/offline-first app direction without implementing Cloud, payment, subscription, or online activation.

### HARDEN-A6 — Data/path/backup contract hardening

Added `SalrynLocalDataContract` as documentation-as-code for future installer/update work.

The contract names preservation expectations for:

- production database
- backups
- exports
- logs
- receipts
- config
- local license file
- local license backup files
- native PDF report artifacts

The contract aligns with current roots:

- `ProgramData/Salryn` for production DB/config/export/log/receipt style data
- `LocalApplicationData/Salryn/Licensing` for local license files/backups
- `LocalApplicationData/Salryn/Reports/NativePdf` for native PDF artifacts

No installer work, migration rewrite, data path move, or filesystem refactor was performed.

## 5. What changed

- Added a lightweight proof taxonomy.
- Added a pre-beta proof catalog.
- Added a focused HARDEN-A1-A6 source/contract smoke check.
- Registered the HARDEN check in full CoreSmoke and as a focused `--only` runner.
- Added local licensing policy-as-code for offline-first/non-network runtime constraints.
- Added local path/data preservation contract-as-code for future installer/update lanes.

## 6. What was only audited/documented

- Security terms and forbidden dependency scan are source-level checks, not a full security assessment.
- Permission guard checks are source-level guard expectations, not live HTTP endpoint proof.
- Licensing policy is non-executable; it does not enforce product access.
- Data/path contract is non-migrating; it does not move existing files or implement update behavior.
- Native PDF artifact path policy was checked for controlled-root seams only; reporting behavior was not changed.

## 7. Intentionally excluded

- No beta release work.
- No Salryn Cloud implementation.
- No Vercel licensing.
- No payment/subscription sync.
- No online activation.
- No installer implementation.
- No frontend polish.
- No broad test-framework migration.
- No permission system redesign.
- No RMO/reporting behavior changes.
- No production legal/compliance claims.

## 8. Commands / gates run

No local PowerShell commands were run by this ChatGPT connector session.

Required local commands still need Sol/Codex/local verification:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-HARDEN-A1-A6-PRE-BETA-FOUNDATION
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
```

Connector actions performed:

- Created branch `KIRCH-HARDEN-A1-A6-PRE-BETA-FOUNDATION` from `0c805899f5a83135ec3be582edf3dcb3359263cc`.
- Fetched selected repo files for source inspection.
- Added new source/test/report files through GitHub contents API.
- Updated `tools/Salryn.Tools.CoreSmoke/Program.cs` through GitHub contents API.
- Ran connector compare against the base SHA.

## 9. Remaining manual proof gaps

Still unverified until Sol/Codex/local execution:

1. `dotnet restore`.
2. `dotnet build`.
3. focused M1/M2/M3 licensing smoke.
4. focused HARDEN-A1-A6 smoke.
5. full CoreSmoke.
6. React/Vite web build.
7. live HTTP tests for license status/install/entitlements.
8. Windows file replacement/backup behavior for license install.
9. real ProgramData/LocalApplicationData preservation behavior.
10. protected ref verification from local git truth.

## 10. Stop conditions encountered

No source-level stop condition was found that required broad redesign.

Local handoff was declined, so local verification could not be performed here. This is a verification stop, not a source-implementation stop.

If Sol/Codex finds compile failures, runtime guard failures, or path contract mismatch, do not promote this hardening lane. Repair in a focused follow-up or split the affected subpass.

## 11. Explicit non-claims

This hardening lane does not claim:

- beta readiness
- production release readiness
- production licensing/legal approval
- BIR approval
- eSales production transmission
- Salryn Cloud implementation
- Vercel licensing
- payment/subscription sync
- online activation
- installer implementation
- hardware lockout
- private signing keys
- RMO/reporting behavior changes
- local build/test/smoke GREEN from this ChatGPT connector session

## 12. Status

HARDEN-A1-A6 is a connector-authored candidate only.

Sol/Codex/local verification must run before this hardening lane is accepted.

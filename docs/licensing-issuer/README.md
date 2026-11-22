# Kirjane Labs — Standalone Salryn Licensing Issuer

This directory documents the **standalone licensing administration web application and server-side offline license issuer** implemented for [Issue #39](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/39).

## 1. Isolation Boundary (Not Part of Salryn Cafe POS Installer)

- **Backend:** `services/licensing-issuer/` (`Salryn.LicensingIssuer.Server`, ASP.NET Core 10 Minimal API + SQLite WAL persistence).
- **Frontend:** `apps/licensing-web/` (`@salryn/licensing-web`, React 19 + TypeScript + Vite reusing `@salryn/cafe-ui` and `assets/brand/`).
- **Tests:** `tests/Salryn.LicensingIssuer.Tests/` (12 xUnit real HTTP + disposable SQLite security, RBAC, separation-of-duties, concurrency, and contract tests).
- **Packaging Isolation:** Neither `services/licensing-issuer` nor `apps/licensing-web` is referenced by `src/Salryn.Desktop.WebView`, `src/Salryn.Api.Local`, `installer/SalrynCafe.iss`, or `scripts/Build-SalrynCafeInstaller.ps1`. It is never bundled into `Salryn-Cafe-Setup.exe` or installed on customer workstations.

---

## 2. Threat Model, Role Authorization & Key Custody

| Control | Implementation |
| --- | --- |
| **Role-based access control (RBAC)** | Three server-enforced roles: `IssuerOperator` (`customers:write`, `stores:write`, `requests:draft`), `IssuerReviewer` (`requests:review`, `audit:read`), and `IssuerAdmin` (all permissions + `requests:issue` and `users:manage`). Wrong-role requests return `403 Issuer.ForbiddenRole`. |
| **Separation of duties (two-person rule)** | By default (`AllowSelfApprovalException = false`), the user who drafted a license request cannot approve it (`403 Issuer.SelfApprovalForbidden`, audited as `LicenseRequest.SelfApprovalDenied`). If `SALRYN_ISSUER_ALLOW_SELF_APPROVAL_EXCEPTION=1` is explicitly configured for an emergency single-admin host, a documented authorization note (>= 10 characters) is mandatory and audited with `selfApprovalException: true`. |
| **Private key isolation** | RSA-2048 private signing keys are read strictly server-side from `SALRYN_ISSUER_SIGNING_KEY_PEM_PATH` or `SALRYN_ISSUER_SIGNING_KEY_PEM`. Keys inside any Git worktree are rejected (`SIGNER_KEY_INSIDE_REPO_FORBIDDEN`). Keys never enter Git, Vite/browser bundles, `VITE_*` env vars, SQLite tables, API responses, or audit logs. |
| **Fail-closed signing & production test-key block** | When no key is configured, `POST /api/issuer/requests/{id}/issue` returns `503 Service Unavailable` (`SIGNER_NOT_CONFIGURED`) and writes a `LicenseRequest.IssueBlocked` audit entry. Mismatched keys fail closed with `503 SIGNER_KEY_UNTRUSTED`. In `Production` (`ASPNETCORE_ENVIRONMENT=Production`), `AllowUntrustedTestSigner` is strictly blocked (`503 SIGNER_TEST_KEY_FORBIDDEN_IN_PRODUCTION`). |
| **No product substitution in verification** | `ExternalLicenseSignerService.SignAndVerify` validates and signs the **original, unchanged** `LocalLicenseDocument` (`"Salryn.Cafe"` or `"Salryn.POS"`), never substituting `Salryn.POS` for `Salryn.Cafe`, and honestly distinguishes `VerifierPassed` (key-pair verified) from `RuntimeTrustedVerifierPassed` (verified against `TrustedPublicKeySpkiSha256 = 30153dcb...`). |
| **Tamper-evident audit hash chain** | Every row in `issuer_audit_log` chains `prev_hash` -> `entry_hash` via SHA-256 over canonical fields; `GET /api/issuer/audit/verify` verifies the full chain (`IsValid`, `CheckedEntries`, `FirstBrokenId`). |
| **Admin authentication, CSRF & TLS guard** | Passwords hashed with `SalrynPasswordHasher` (PBKDF2-SHA256). Sessions use 256-bit random server-side tokens in `HttpOnly; SameSite=Strict` cookies paired with constant-time `X-Salryn-Issuer-Csrf` checks on all `POST` endpoints, sliding-window login rate limiting (`429`), strict CSP / `nosniff` / `DENY` / `no-store` headers, and non-loopback HTTPS enforcement (`403 Issuer.HttpsRequired`). |

---

## 3. Standalone Build & Run

### Build Frontend Bundle
```powershell
cd apps/licensing-web
npm install
npm test
npm run build
```

### Run Backend Integration & Security Tests (12 Tests)
```powershell
dotnet test tests/Salryn.LicensingIssuer.Tests/Salryn.LicensingIssuer.Tests.csproj -c Release
```

### Run Standalone Local Admin Server (Fail-Closed Mode — No Production Key Required)
```powershell
$env:SALRYN_ISSUER_DB_PATH = "$env:LOCALAPPDATA\SalrynLicensingIssuer\issuer.db"
$env:SALRYN_ISSUER_URL = "http://127.0.0.1:5410"
$env:SALRYN_ISSUER_ADMIN_USERNAME = "kirjane-admin"
$env:SALRYN_ISSUER_ADMIN_PASSWORD = "<strong-admin-password>"
$env:SALRYN_ISSUER_REVIEWER_USERNAME = "kirjane-reviewer"
$env:SALRYN_ISSUER_REVIEWER_PASSWORD = "<strong-reviewer-password>"
$env:SALRYN_ISSUER_OPERATOR_USERNAME = "kirjane-operator"
$env:SALRYN_ISSUER_OPERATOR_PASSWORD = "<strong-operator-password>"

dotnet run --project services/licensing-issuer/Salryn.LicensingIssuer.Server.csproj -c Release
```

---

## 4. Production Key & Issue #38 Coordination

1. **External Blocker (`BLOCKED_EXTERNAL` / `ISSUANCE_BLOCKED_EXTERNAL`):** Production offline license issuance remains blocked until Kirjane Labs provisions the authorized private RSA key matching `TrustedPublicKeySpkiSha256` (`30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4`) on the issuer host or HSM/KMS adapter implementing `ILicenseSignerService`.
2. **Issue #38 Coordination:** Offline `Salryn.Cafe` licenses signed by this issuer carry `"applicationId": "Salryn.Cafe"` and `"schemaVersion": 2`. Customer Windows workstations require [Issue #38 / PR #40](https://github.com/Kirjane/Dev-Salryn-Cafe/pull/40) (`forge/salryn-cafe-license-first-ui`) so `LocalLicenseService` validates `"Salryn.Cafe"` instead of legacy `"Salryn.POS"`.

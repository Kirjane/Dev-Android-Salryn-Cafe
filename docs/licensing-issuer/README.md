# Kirjane Labs — Standalone Salryn Licensing Issuer

This directory documents the **standalone licensing administration web application and server-side offline license issuer** implemented for [Issue #39](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/39).

## 1. Isolation Boundary (Not Part of Salryn Cafe POS Installer)

- **Backend:** `services/licensing-issuer/` (`Salryn.LicensingIssuer.Server`, ASP.NET Core 10 Minimal API + SQLite WAL persistence).
- **Frontend:** `apps/licensing-web/` (`@salryn/licensing-web`, React 19 + TypeScript + Vite reusing `@salryn/cafe-ui` and `assets/brand/`).
- **Tests:** `tests/Salryn.LicensingIssuer.Tests/` (xUnit real HTTP + disposable SQLite security & contract tests).
- **Packaging Isolation:** Neither `services/licensing-issuer` nor `apps/licensing-web` is referenced by `src/Salryn.Desktop.WebView`, `src/Salryn.Api.Local`, `installer/SalrynCafe.iss`, or `scripts/Build-SalrynCafeInstaller.ps1`. It is never bundled into `Salryn-Cafe-Setup.exe` or installed on customer workstations.

---

## 2. Threat Model & Key Custody

| Control | Implementation |
| --- | --- |
| **Private key isolation** | RSA-2048 private signing keys are read strictly server-side from `SALRYN_ISSUER_SIGNING_KEY_PEM_PATH` or `SALRYN_ISSUER_SIGNING_KEY_PEM`. Keys never enter Git, Vite/browser bundles, `VITE_*` env vars, SQLite tables, API responses, or audit logs. |
| **Fail-closed signing** | When no key is configured, `POST /api/issuer/requests/{id}/issue` returns `503 Service Unavailable` (`SIGNER_NOT_CONFIGURED`) and writes a `LicenseRequest.IssueBlocked` audit entry. Draft creation, customer/store registration, and approvals remain functional. |
| **Trusted SPKI enforcement** | The server computes the configured key's SPKI SHA-256 and compares it to `LocalLicenseSignatureVerifier.TrustedPublicKeySpkiSha256` (`30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4`). Mismatched keys fail closed with `503 SIGNER_KEY_UNTRUSTED` unless `SALRYN_ISSUER_ALLOW_UNTRUSTED_TEST_SIGNER=1` is explicitly set for non-production QA, in which case every banner and issued record is visibly labeled `TestUntrustedKey`. |
| **Admin authentication & CSRF** | Passwords hashed with `SalrynPasswordHasher`. Sessions use 256-bit random server-side tokens stored in `HttpOnly; SameSite=Strict` cookies (`Secure` on HTTPS), paired with a per-session `X-Salryn-Issuer-Csrf` header checked via constant-time comparison on all state-changing `POST` endpoints. |
| **Brute-force protection** | Sliding-window login rate limiter keyed by client IP + username (`429 Too Many Requests`). |
| **HTTP security headers** | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, strict `Content-Security-Policy`, `Cache-Control: no-store` on `/api/*`, and zero open CORS headers. |
| **Offline non-revocation honesty** | Salryn Cafe verifies offline license files locally with RSA-SHA256 PKCS#1 v1.5. Once copied to a customer PC, an offline license file cannot be remotely revoked without a future runtime revocation design. Reissue marks the prior record `Superseded` in issuer history and audit logs. |

---

## 3. Standalone Build & Run

### Build Frontend Bundle
```powershell
cd apps/licensing-web
npm install
npm test
npm run build
```

### Run Backend Integration & Security Tests
```powershell
dotnet test tests/Salryn.LicensingIssuer.Tests/Salryn.LicensingIssuer.Tests.csproj -c Release
```

### Run Standalone Local Admin Server (Fail-Closed Mode — No Production Key Required)
```powershell
$env:SALRYN_ISSUER_DB_PATH = "$env:LOCALAPPDATA\SalrynLicensingIssuer\issuer.db"
$env:SALRYN_ISSUER_URL = "http://127.0.0.1:5410"
$env:SALRYN_ISSUER_ADMIN_USERNAME = "kirjane-admin"
$env:SALRYN_ISSUER_ADMIN_PASSWORD = "<strong-operator-password>"
# Optional external signing key (keep outside repository; production deployments should use HSM/KMS or tightly ACL'd host secret file):
# $env:SALRYN_ISSUER_SIGNING_KEY_PEM_PATH = "C:\Secrets\Kirjane\salryn-offline-signing-key.pem"

dotnet run --project services/licensing-issuer/Salryn.LicensingIssuer.Server.csproj -c Release
```

Open `http://127.0.0.1:5410` in a browser on the authorized Kirjane Labs admin host (or behind internal TLS/VPN reverse proxy).

---

## 4. Production Key & Issue #38 Coordination

1. **External Blocker (`ISSUANCE_BLOCKED_EXTERNAL`):** Production offline license issuance remains blocked until Kirjane Labs connects the authorized private RSA key matching `TrustedPublicKeySpkiSha256` (`30153dcb...`) on an authorized signing host or HSM/KMS adapter implementing `ILicenseSignerService`.
2. **Issue #38 Coordination:** Offline `Salryn.Cafe` licenses signed by this issuer carry `"applicationId": "Salryn.Cafe"` and `"schemaVersion": 2`. Customer Windows workstations require [Issue #38](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/38) (`forge/salryn-cafe-license-first-ui`) so `LocalLicenseService` validates `"Salryn.Cafe"` instead of legacy `"Salryn.POS"`.

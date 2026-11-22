# Kirjane Labs — Signing Authority Inventory, Provisioning & Key Custody Record

**Status:** `CANONICAL_SIGNING_AUTHORITY_VERIFIED`  
**Target Product:** Salryn Cafe (`Salryn.Cafe`) & Standalone Licensing Issuer (`Salryn.LicensingIssuer.Server`)  
**Canonical Public Key SPKI SHA-256:** `30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4`

---

## 1. Read-Only Signing Authority Inventory

1. **Runtime Verification Contract (`src/Salryn.Infrastructure/Licensing/LocalLicenseSignatureVerifier.cs`):**
   - Salryn Cafe verifies offline license documents (`schemaVersion: 2`, `applicationId: "Salryn.Cafe"`) using **RSA-2048 SHA-256 PKCS#1 v1.5** (`RSASignaturePadding.Pkcs1`) over `LocalLicenseSignatureVerifier.BuildSigningPayload(document)`.
   - The embedded `PublicKeyPem` has SPKI SHA-256 `30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4`.
2. **External Custody Record (`C:\Salryn-LicensingOps-I3\docs\KEY_CUSTODY.md` / `Kirch-Nairu/Salryn.LicensingOps`):**
   - The read-only inventory located the canonical Kirjane Labs `Salryn.LicensingOps` custody directory at `C:\Salryn-LicensingOps-I3` (outside all Salryn POS worktrees).
   - `C:\Salryn-LicensingOps-I3\docs\KEY_CUSTODY.md` defines the sealed contract:
     - Canonical Salryn/LicensingOps public-key SPKI SHA-256: `30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4`.
     - Private key custody path: `C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem` (gitignored, never committed to Git).
   - Metadata and derived public SPKI verification confirmed `C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem` derives the exact public SPKI SHA-256 `30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4` (`MatchesTrustedRuntimeKey == true`). Zero private key bytes were printed, copied, or exposed.

---

## 2. Authorized Signer Provisioning Procedure (Without Exporting Key Material)

To operate the standalone `Salryn.LicensingIssuer.Server` in **`ProductionTrustedSigner`** mode on the authorized Kirjane Labs administration host:

1. **Keep the private key in its external custody path** (`C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem` or an equivalent ACL-restricted out-of-repo path; `ExternalLicenseSignerService` blocks any path inside a `Salryn.sln` worktree with `SIGNER_KEY_INSIDE_REPO_FORBIDDEN`).
2. **Pass the path via server environment variable (never in Git, Vite, or client code):**
   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Production"
   $env:SALRYN_ISSUER_SIGNING_KEY_PEM_PATH = "C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem"
   $env:SALRYN_ISSUER_ALLOW_UNTRUSTED_TEST_SIGNER = "0"
   ```
3. **Verify public health & SPKI match:**
   - `GET /api/issuer/health` returns:
     - `signerConfigured: true`
     - `signerCanIssue: true`
     - `signerMode: "ProductionTrustedSigner"`
     - `trustedRuntimeSpkiSha256: "30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4"`
   - In GitHub CI or developer machines where `C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem` is absent, the issuer automatically fails closed (`503 SIGNER_NOT_CONFIGURED`).

---

## 3. Future Key Rotation Policy (Dedicated `Salryn.Cafe` Root Key Option)

Per `C:\Salryn-LicensingOps-I3\docs\KEY_CUSTODY.md`, if Technical Authority later elects to rotate `Salryn.Cafe` to a product-dedicated RSA-3072 keypair separate from `Salryn.POS`:
1. Generate the new keypair inside `LicensingOps` `.secrets/` (or HSM/KMS) and export **only** the public key PEM and SPKI SHA-256 fingerprint.
2. Update `PublicKeyPem` and `TrustedPublicKeySpkiSha256` in `src/Salryn.Infrastructure/Licensing/LocalLicenseSignatureVerifier.cs` in an audited PR reviewed by `Kirion-Aegis` and `Kirion-Nox`.
3. Point `SALRYN_ISSUER_SIGNING_KEY_PEM_PATH` on the standalone issuer host to the rotated private key and rebuild `Salryn-Cafe-Setup.exe`.

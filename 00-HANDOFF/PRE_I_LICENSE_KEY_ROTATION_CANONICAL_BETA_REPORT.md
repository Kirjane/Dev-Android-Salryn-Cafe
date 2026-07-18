# PRE-I License Key Rotation — Canonical Beta Report

## 1. Branch, base, and implementation SHA

- Branch: `KIRCH-PRE-I-LICENSE-KEY-ROTATION-CANONICAL-BETA`
- Base: `KIRCH-PRE-I-AUDIT-CSV-EXPORT`
- Base SHA: `6d84e8de5aaf3efe0538bddfe57a01f4707b9f1d`
- Implementation SHA: `6e7b52dc0db4656d9867da1f2af3e91a57a2ae70`
- The report closure commit is documentation-only; remote final SHA is recorded in the handoff output.

## 2. Old Salryn public fingerprint

Retired SPKI SHA-256: `10149b43f8dcb6c82e4c8fdd538572faaa78fd126af2caaa29cad997e67bc284`.

## 3. New canonical public fingerprint

Trusted SPKI SHA-256: `97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59`. Salryn contains one trusted beta public key only.

## 4. Validation behavior preserved

RSA-SHA256/PKCS#1 signature validation remains mandatory. Schema, application identity, flat-document field allowlist, machine binding, Host/Terminal type separation, Store/Host linkage, and fail-closed behavior remain intact. Wrapper JSON, tampering, wrong machine binding, Host-as-Terminal proof, and Terminal access to Host POS features are rejected.

## 5. Fresh Host and Terminal proof

Fresh flat Host and Terminal-from-Host documents issued by LicensingOps under the canonical key are committed as public test fixtures. The focused Salryn gate verifies both real signatures, imports the Host through `LocalLicenseService`, validates the Terminal proof path, and confirms Terminal local validation without a mocked signature verifier.

## 6. Deprecated fixture handling

The focused gate uses only `tests/Salryn.Tests/Fixtures/CanonicalBeta`. It asserts the retired fingerprint is absent from the verifier. Historical old-key fixtures are not current issuance proof and are not accepted through a fallback or dual-key path. The existing user-level old-key license correctly failed closed during the first full-smoke attempt; it was temporarily isolated and restored byte-for-byte for the development-mode full gate.

## 7. Files changed

- `src/Salryn.Infrastructure/Licensing/LocalLicenseSignatureVerifier.cs`
- `tests/Salryn.Tests/PreILicenseKeyRotationCanonicalBetaChecks.cs`
- `tests/Salryn.Tests/Fixtures/CanonicalBeta/host-license.json`
- `tests/Salryn.Tests/Fixtures/CanonicalBeta/terminal-license.json`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/PRE_I_LICENSE_KEY_ROTATION_CANONICAL_BETA_REPORT.md`

## 8. Gates run

- `dotnet build Salryn.sln` — passed (existing warnings only)
- Focused canonical rotation gate — passed
- Audit CSV, audit trail, client-facing polish, action notification, and code-proven QA focused gates — passed
- Full CoreSmoke — passed after reversible isolation of the retired user license; restored hash matched
- Web production build — passed (existing chunk-size warning only)
- `git diff --check` — passed

## 9. Remaining risks

- Any installed license signed by the retired key now fails closed and must be replaced with a freshly issued canonical license.
- Automated import/signature/entitlement proof passed; final client-machine manual browser QA remains a separate human activity.
- Canonical private-key custody remains local to LicensingOps and depends on disciplined backup/access handling outside this repository.

## 10. Decision

`PRE_I_LICENSE_KEY_ROTATION_CANONICAL_BETA_REPAIRED_AND_PASSED`

No promotion was performed.

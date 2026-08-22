# PRE-I Canonical License Activation Proof Report

## 1. Branch, base, and implementation SHA

- Branch: `KIRCH-PRE-I-CANONICAL-LICENSE-ACTIVATION-PROOF`
- Base branch: `KIRCH-PRE-I-LICENSE-KEY-ROTATION-CANONICAL-BETA`
- Base SHA: `9c091966cfdfa7fe937d8fd859d86473b03e155b`
- Implementation SHA: `5e1820b67ac9eb740f093cfeaefcc78c0e6b913d`
- The report closure commit is documentation-only; remote final SHA is recorded in the final handoff.

## 2. Canonical fingerprint

Salryn embedded key, proof manifest, and LicensingOps contract use SPKI SHA-256 `97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59`.

## 3. Host proof result

LicensingOps generated a fresh canonical Host license in a unique temp proof folder. Salryn consumed the manifest, verified the real signature, confirmed manifest/machine linkage, and imported it through `LocalLicenseService` in isolated fresh state with `Licensed` status.

## 4. Terminal proof result

LicensingOps generated a fresh Terminal license from the fresh Host context. Salryn verified its real signature, Host license linkage, terminal machine binding, and successful `ValidateTerminalLicenseProof` activation-path result.

## 5. Rejection tests

The gate rejects wrong Host machine binding, tampered Host fields/signature, wrapper JSON, wrong Terminal machine binding, Host-as-Terminal proof, Terminal-as-Host entitlement, and Terminal access to the Host POS feature.

## 6. Old-key fail-closed proof

The manifest identifies a retired historical old-key Host fixture separately from the current Host and Terminal proof paths. Salryn verifies that its signature fails under the sole canonical key and that import is rejected. It is never used as current issuance proof; no fallback or dual-key logic was added.

## 7. Manifest and proof input method

Preferred command:

`dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pre-i-canonical-license-activation-proof --proof-manifest "<manifest path>"`

The gate also accepts `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST` or locates the newest `%TEMP%\salryn-canonical-license-activation-proof-*\manifest.json` generated locally by LicensingOps.

## 8. Files changed

- `tests/Salryn.Tests/PreICanonicalLicenseActivationProofChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/PRE_I_CANONICAL_LICENSE_ACTIVATION_PROOF_REPORT.md`

## 9. Gates run

- `dotnet build Salryn.sln` — passed with existing warnings only
- Canonical activation proof, key rotation, audit CSV, audit trail, client-facing polish, and action notification focused gates — passed
- Explicit manifest and latest-temp fallback flows — passed
- Full CoreSmoke — passed; retired user license was isolated reversibly and restored with identical SHA-256
- Web production build — passed with existing chunk-size warning only
- `git diff --check` — passed

## 10. What remains manual

- Visual License page look
- Browser import button feel
- Mobile layout
- Actual client operator-flow comfort

Cryptographic Host/Terminal acceptance is automated and is not a manual remainder.

## 11. Remaining risks

- Existing client licenses signed by the retired key correctly fail closed and require canonical reissuance.
- Temp proof folders contain public signed license documents and should be removed when no longer needed.
- Local private-key custody remains a LicensingOps operational responsibility; no private material enters Salryn.

## 12. Decision

`PRE_I_CANONICAL_LICENSE_ACTIVATION_PROOF_PASSED`

No promotion was performed.

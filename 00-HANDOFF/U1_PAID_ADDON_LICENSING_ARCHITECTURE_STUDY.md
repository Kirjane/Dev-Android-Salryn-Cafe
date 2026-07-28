# U1 Paid Add-on Licensing Architecture Study

Date: 2026-07-28

## Branch

`KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

## Source repo HEAD

`0b5a90a05ed186cc550025276ff3dfa6a1f1cc10`

## Licensing repo discovered/path/branch/HEAD

- Discovered: yes
- Path: `D:\SALRYN STUFF\Salryn.LicensingOps`
- Branch: `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF`
- HEAD and origin: `f73bf74a6e68f835640d0ea3942f979f8fc823d9`
- Worktree: clean

No LicensingOps file was modified. Its `00-HANDOFF` equivalent should be added with the implementation change, after Kirch approves the cross-repo contract.

## Decision

`U1_PAID_ADDON_LICENSE_ENTITLEMENT_IMPLEMENTATION_REQUIRED`

## Business model

U1 remains in the product code and database but is commercially disabled unless a signed Host license contains the paid add-on feature. User permissions independently constrain operations.

Internal: operational receivables tracking only; not accounting compliance, BIR registration, or official tax reporting.

Future public: optional paid add-on for stores needing organized customer credit tracking. Use “separately quoted” until pricing and grandfathering are decided.

## Feature key recommendation

Recommend the exact signed key:

`ReceivablesUtangLedger`

Reason: the current signed feature contract is case-sensitive and uses PascalCase identifiers (`Pos`, `Reports`, `LanTerminals`). A dotted key such as `receivables.utang_ledger` would introduce a second naming convention and would currently be filtered as unknown by POS.

The exact spelling must be defined once in both repositories and covered by cross-repo fixture parity tests. Do not silently alias multiple spellings in RC1/U1.

## License entitlement architecture

```text
Signed Host license
  -> Features includes ReceivablesUtangLedger
  -> POS signature/schema/machine/type validation
  -> known Host-only feature normalization
  -> backend RequireLicensedFeatureAsync
  -> operation-specific permission guard
  -> mutation/read

Frontend entitlement status + session permissions
  -> hide/lock navigation and payment choices
  -> never replaces backend enforcement
```

Decision matrix:

| Entitlement | Permission | Result |
|---|---|---|
| No | No | Deny |
| No | Yes | Deny |
| Yes | No | Deny |
| Yes | Yes | Allow the specifically permitted operation |

## Current source repo gating

- Signed schema v2 license documents already include `Features`.
- `LocalLicenseSignatureVerifier` signs/verifies the feature list.
- `LocalLicenseValidator` normalizes only known features and filters terminal features.
- `LocalLicenseEnforcementService` and `ApiLicenseGuard` provide fail-closed feature decisions.
- `/api/license/status` exposes enabled features to loopback clients.
- `/api/license/entitlements` exposes known features and decisions to an authenticated loopback operator.
- Existing feature gates protect POS, reports, users, backup, and LAN-related boundaries.
- U1 endpoints use granular permissions.

## Missing source repo gating

- No `ReceivablesUtangLedger` constant in `LicenseFeatures`.
- Not present in `All`, `AllList`, or `HostOnly`.
- No U1 feature decision or focused enforcement test.
- `/api/checkout` requires only `Pos`; Utang paths lack the add-on guard.
- All receivables endpoints lack the add-on guard.
- Owner-report receivables data lacks an add-on guard.
- React navigation and checkout use permissions only.
- Direct U1 page state has no unlicensed presentation.
- No downgrade/re-enable test proves data retention.

## Current licensing repo support

- Schema v2 has a signed `Features: string[]`.
- Canonical payload normalization includes features.
- Issuance accepts an internal optional `features` array.
- Default Host features are `Pos`, `Reports`, `Users`, `Backup`, and `Licensing`; `LanTerminals` is conditional.
- Terminal licenses are fixed to `LanTerminals`.
- Private signing material is loaded from environment or ignored `.secrets` paths; no private key was inspected or exposed in this study.
- Issued documents are verified before being written and ledgered.
- Canonical fixtures and cross-repo activation proof infrastructure already exist.

## Missing licensing repo support

- The feature vocabulary does not include U1.
- Structure verification does not reject unknown/duplicate/inappropriate feature names or require base `Pos` on Host licenses.
- Normal store-based Host issuance does not accept an add-on selection.
- The CLI has no U1 add-on flag.
- Localhost operator APIs/UI do not accept or present an add-on selection.
- Issuance ledger/operator views do not make commercial entitlements prominent enough for review.
- No U1-enabled and U1-disabled signed fixtures.
- No cross-repo test proves POS accepts the new exact key and rejects a missing/unknown key.

## Required schema/generator/validator/sample-test/source/frontend/API/report-receipt changes

### Schema

- Add `ReceivablesUtangLedger` to the shared feature vocabulary.
- Prefer retaining schema version 2 because `Features` is already signed and extensible; bump only if policy requires a breaking document-shape change.
- Define it as Host-only. Do not grant it through a Terminal license document.

### Generator

- Add an explicit boolean/selectable paid add-on field to the controlled Host issuance path.
- Default off.
- Carry the selection through store-based issuance; do not rely on the lower-level arbitrary `features` escape hatch.
- Display selected entitlements in the confirmation and issuance record.

### Validator

- LicensingOps should reject unknown, blank, duplicate, wrong-license-type, and policy-invalid features before signing.
- POS must add the exact key to its known Host-only allowlist.
- Preserve signature, application, machine, expiry, and license-type validation.

### Sample/test

- Add deterministic Host fixtures with and without U1.
- Prove canonical signature verification across LicensingOps and POS.
- Prove absent/unknown/misspelled keys fail closed.
- Prove Terminal license alone cannot unlock U1.
- Do not issue a real customer license during development or QA.

### Source

- Register the key in product licensing contracts and entitlement status.
- Keep database migrations applied irrespective of entitlement so disable/re-enable does not destroy data.

### Frontend

- Combine enabled feature and permission state.
- Hide Utang payment choices when unlicensed.
- Hide the U1 navigation entry for ordinary unlicensed users; optionally present a non-actionable locked add-on entry to approved managers/admins.
- Handle direct navigation with a clear locked/denied state.

### API

- Require add-on entitlement before every customer/receivable read and mutation.
- Require it for any checkout containing `AccountsReceivable`, in addition to `Pos` and `Receivables.CreateCreditSale`.
- Apply entitlement before mutation and preserve checkout/collection request-id behavior.
- Define explicit, consistent denial codes suitable for frontend mapping and audit.

### Report/receipt

- Suppress or lock receivables sections without entitlement.
- Preserve operational wording and current non-official disclaimers.
- Do not imply accounting, BIR, tax, accreditation, or compliance status.

## Backup/restore

U1 tables live in the same SQLite database as base POS data. Existing online backup/controlled restore therefore carries U1 data with the database.

Required policy:

- License disable/removal never deletes or rewrites receivable data.
- Backup remains governed by base `Backup` entitlement and `Backup.Manage`, not by U1.
- Restoring a database with U1 data under a non-U1 license succeeds, but U1 reads/mutations stay inaccessible.
- Later installation of a valid U1 license reveals the preserved data.
- Test migration compatibility, balances, collections, checkout request IDs, collection request IDs, and safety backups.

## Security risks

- Permission-only enforcement permits commercial bypass.
- UI-only enforcement permits direct API bypass.
- Arbitrary feature input in a signing tool increases operator-error risk without an allowlist.
- A spelling/casing mismatch silently denies legitimate customers.
- Exposing enabled features is acceptable entitlement metadata, but remote surfaces must not gain loopback license-administration access.
- Entitlement checks performed after mutation could create partial sales/ledger effects.
- A Terminal license must not impersonate a Host U1 entitlement.
- License replacement/downgrade must be audited without logging license JSON, signatures, keys, or sensitive customer data.

## Wording risks

- “Upcoming update” without “optional paid add-on” may imply base inclusion.
- “Accounts receivable” can be mistaken for accounting compliance unless consistently described as operational tracking.
- “Official,” “BIR,” “tax report,” “certified,” and “compliant” must not describe U1.
- Locked UI must not promise pricing or availability before the commercial policy is approved.

## Questions

1. Approve `ReceivablesUtangLedger` exactly?
2. Approve hidden-for-users and optionally locked-for-managers/admins behavior?
3. Which roles receive each receivables permission by default?
4. Are current customers grandfathered, separately quoted, or decided case by case?
5. Public wording: “separately quoted” only, or a published price?
6. Can host-entitled U1 be used through LAN terminal checkout, or host shell only?
7. Must an operator be able to add/remove U1 on replacement licenses while retaining all other entitlements?

## Order

1. Approve business policy and exact key.
2. Implement and test LicensingOps allowlist, explicit operator selection, fixtures, and audit presentation.
3. Implement and test POS known-feature recognition.
4. Add backend entitlement checks to checkout, receivables, and reports.
5. Add frontend role-plus-entitlement UX.
6. Run cross-repo signature/fixture, denial, idempotency, downgrade, backup/restore, solution, and web gates.
7. Perform installed-app and, if applicable, two-machine LAN QA.
8. Update public wording.
9. Rebuild/re-hash installer only after acceptance.

## Do not release

Do not package or release U1 as a paid add-on until the signed entitlement is selectable, validated, accepted by POS, enforced server-side across every U1 path, reflected honestly in UI/public wording, and proven through automated and manual gates.

## Protected refs

- `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Neither was touched.

## Promotion

None.

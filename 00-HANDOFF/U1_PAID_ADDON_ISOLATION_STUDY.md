# U1 Paid Add-on Isolation Study

Date: 2026-07-28

## Branch

`KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

## Previous origin HEAD

`e9d8472b036659dc9cfbc98cf5889e84b4b2c547`

## Local HEAD

`0b5a90a05ed186cc550025276ff3dfa6a1f1cc10`

Local is five commits ahead of origin and the worktree was clean before this report was added.

## Push performed

No. This study did not push, merge, promote, issue licenses, rebuild an installer, or modify protected refs.

## New origin HEAD

Unchanged: `e9d8472b036659dc9cfbc98cf5889e84b4b2c547`

## Decision

`U1_PAID_ADDON_ISOLATION_REQUIRED`

U1 is technically present but is not commercially isolated. Permission checks exist; paid license-entitlement checks do not.

## Business position

Internal positioning:

> Utang Ledger is an operational receivables tracker for store management. It is not an accounting compliance system. It is not a BIR registration feature. It is not official tax reporting.

Later public positioning:

> Optional paid add-on for stores that need organized customer credit tracking.

Price and grandfathering policy remain business decisions. Until decided, public wording should use “separately quoted” rather than promise inclusion or a price.

## Paid add-on isolation verdict

The correct authorization rule is an intersection:

`CanUseUtang = License.HasFeature(ReceivablesUtangLedger) && User.HasPermission(required operation)`

Permissions must not substitute for commercial entitlement, and entitlement must not grant a user authority their role lacks.

| Operation | Required license | Required permission |
|---|---|---|
| Cash checkout | Base `Pos` | `Sales.Create` |
| Utang checkout | `Pos` and U1 add-on | `Sales.Create` and `Receivables.CreateCreditSale` |
| Cash + Utang checkout | `Pos` and U1 add-on | `Sales.Create` and `Receivables.CreateCreditSale` |
| View customer ledger/summary | U1 add-on | `Receivables.View` |
| Create/update customer | U1 add-on | `Receivables.ManageCustomers` |
| Collect payment | U1 add-on | `Receivables.Collect` |
| Adjustment/void collection | U1 add-on | `Receivables.Adjust` |
| Receivables section in owner reports | U1 add-on | existing reports/role boundary plus the appropriate receivables view boundary |

## Licensing/entitlement findings

- The product already validates signed feature arrays and provides `ApiLicenseGuard`.
- Known features are currently `Pos`, `Rmo`, `Reports`, `Users`, `Backup`, `Licensing`, and `LanTerminals`.
- Unknown product features are filtered/rejected by the product enforcement path, so adding a signed string in LicensingOps alone will not unlock U1.
- U1 has no feature constant, known-feature registration, host-only classification, entitlement decision, or focused entitlement smoke.
- Development mode currently enables all registered product features. After U1 registration, development behavior must be intentional and tested.

## Frontend gating findings

- `Utang Ledger` is an always-configured navigation item filtered only by `Receivables.View`.
- Checkout exposes Utang modes from permission state only.
- The frontend license API already models `enabledFeatures`, but shell/navigation and checkout do not combine it with U1 permissions.
- Direct page loading must be handled honestly: route/shell gating is UX, not the security boundary.
- Recommended unlicensed UX: hide Utang checkout choices so cash checkout remains simple; show a clearly locked add-on entry only to authorized host managers/admins if a discoverable upsell is desired. Never show actionable controls that the API will reject.
- Terminal behavior must remain role-aware. If remote terminal checkout is intended to support Utang, it needs the same host-entitlement decision and terminal permission boundary; otherwise Utang must stay absent there.

## API gating findings

- `/api/checkout` checks `Sales.Create`, optionally `Receivables.CreateCreditSale`, and base `Pos`; it does not require a U1 entitlement for an `AccountsReceivable` payment.
- Customer, summary, ledger, collection, adjustment, and collection-void endpoints check receivables permissions only.
- Owner reports require `Reports`, but the receivables subsection is assembled without a separate U1 entitlement boundary.
- Backend enforcement must be authoritative on every U1 read and mutation. Frontend hiding is insufficient.
- Fail closed on missing, invalid, expired, wrong-type, or feature-missing licenses.

## Checkout gating findings

- Cash must remain unchanged and require the base POS license.
- Utang and Cash + Utang must require both base POS and the U1 entitlement, plus `Receivables.CreateCreditSale`.
- Mixed-payment detection already distinguishes `AccountsReceivable`; it is the natural point for the additional entitlement decision.
- Durable checkout idempotency must remain unchanged. Entitlement should be decided before mutation and should not alter request-ID semantics.

## Reports/receipt wording findings

- Operational receipt rendering labels the outstanding component as `Utang`.
- Owner reports already carry: “Owner Reports are store operational reports only. Not an official/BIR/RMO report.”
- Current receipt output also distinguishes operational output from an official receipt.
- Preserve plain operational labels such as customer, cash paid, Utang, balance due, collection, and due date.
- Do not use “accounts compliant,” “BIR registered,” “official tax report,” “certified ledger,” or similar claims.
- When unlicensed, U1 report sections should be omitted or explicitly locked; they must not leak receivable totals through a base reports entitlement.

## Public repo wording findings

The clean public documentation repo at `D:\SALRYN STUFF\Salryn-Point-of-Sale` is on `main` at `5e878edf303f9ad9adec28e7c6af6c277081774c`.

Its README and changelog describe an upcoming Utang Ledger update and correctly say it is not in the current installer. They do not identify it as an optional paid/separately quoted add-on. That can imply inclusion in the base product. No public files were changed in this study.

## Stale/dead/placeholder findings

- No U1 `TODO`, `FIXME`, `NotImplementedException`, or fake service was found in the focused runtime paths.
- `Receivables.Export` appears to be permission vocabulary without a corresponding delivered U1 export surface; confirm whether it is reserved or stale before packaging.
- The add-on entitlement itself is the principal placeholder: the license feature architecture exists, but U1 is not registered or consumed.
- Existing migrations 22–24 are active database schema, not dead code.

## Stability risks

- P0 commercial bypass: any user with receivables permission can use U1 under a base Host license.
- P0 API bypass if only UI gating is added.
- P1 information disclosure: owner reports can expose receivable totals without add-on entitlement.
- P1 inconsistent UX if navigation, checkout, direct routes, and API decisions read different entitlement state.
- P1 accidental denial if a new key is issued but not added to the product known-feature allowlist.
- P1 cross-repo drift if LicensingOps and POS use different spelling/casing.
- P1 downgrade/data-loss risk if license removal triggers deletion. It must not; disable access while preserving data.
- P2 misleading public positioning if the upcoming update remains presented as a base feature.

## Necessary before packaging

1. Kirch approves feature key, unlicensed UX, default roles, existing-client policy, and public commercial wording.
2. Implement the same exact feature key in LicensingOps and Salryn POS.
3. Add authoritative API entitlement checks to every U1 read/mutation and Utang checkout.
4. Add role-and-entitlement frontend gating.
5. Add focused denial/allowance, checkout, reports, downgrade, backup/restore, and signature-parity tests.
6. Issue only test fixtures during development; do not issue real customer licenses in implementation work.
7. Complete manual installed-app and role-aware UI QA.
8. Rebuild and rehash the installer only after accepted code is integrated.

## Questions for Kirch

1. Approve `ReceivablesUtangLedger` as the signed feature key, or require another exact key?
2. When unlicensed, should ordinary users see nothing while host managers/admins see a locked “Optional add-on” entry?
3. Which default roles receive View, CreateCreditSale, Collect, ManageCustomers, and Adjust?
4. Do existing clients receive U1 free/grandfathered, or is it separately quoted for everyone?
5. Should public docs state only “separately quoted,” or publish a price?
6. Is Utang allowed on licensed LAN terminals in U1, or host checkout only?
7. Should `Receivables.Export` remain reserved, be implemented later, or be removed in a separately authorized cleanup?

## Recommended implementation order

1. Freeze business decisions and exact key.
2. Extend LicensingOps schema vocabulary, issuer/operator controls, verifier policy, fixtures, and tests.
3. Extend POS known-feature validation/enforcement and focused tests.
4. Add backend gates, beginning with checkout and all receivables endpoints.
5. Add report-section gating.
6. Add frontend entitlement state, navigation, direct-page, and checkout UX gating.
7. Prove backup/restore and entitlement downgrade preserve data.
8. Run builds/smokes, installed-app QA, and two-machine QA if terminal Utang is in scope.
9. Update public wording, rebuild installer, generate a new SHA256, then consider release.

## Do not release because

The paid add-on can currently be used without a paid entitlement. LicensingOps cannot intentionally select the add-on through its normal operator flow, public wording does not disclose the paid boundary, and installed-app/manual role-entitlement proof is absent.

## Manual QA still required

- Base-license cash checkout remains usable.
- Base-license users cannot see or call U1 reads/mutations.
- Add-on license plus each required permission allows only that operation.
- Add-on license without permission remains denied.
- Permission without add-on remains denied.
- Utang and Cash + Utang denial causes no sale, stock, ledger, or idempotency mutation.
- Collections and adjustments fail closed without entitlement.
- Owner reports do not leak receivables when unlicensed.
- Removing/replacing entitlement preserves existing U1 data.
- Re-enabling entitlement restores access to preserved data.
- Backup/restore preserves customers, ledger, collections, request IDs, and balances.
- Host, cashier, and terminal shells show the intended role-aware state.

## Installer rebuild required

Yes, after the future entitlement implementation is accepted. No installer was rebuilt here.

## Protected refs

- `origin/KIRCH-SANDBOX` remained `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`.
- `origin/KIRCH-INSTALLER-M1` remained `6b9c46676390b8a86a989eca600cb02e406ce865`.

## Promotion

None.

# U1 Paid Add-on Acceptance Study

Source branch: `KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

Source HEAD: `3ddce37964ae9b721d4b2f982b96b0035ac226db`

LicensingOps branch: `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF`

LicensingOps HEAD: `3f67ea95070361333c07cda500ce69d35db685d6`

Public docs HEAD: `d78810bdf615c6773d8d1852857598512dbbab12`

Decision: `U1_PAID_ADDON_ACCEPTANCE_BLOCKED_P0_P1`

Business model: U1 Utang / Accounts Receivable Ledger is an optional, separately quoted paid add-on for operational customer-credit tracking. It is not included in the base Salryn POS license, a BIR compliance feature, an accounting compliance system, or an official tax-reporting module.

Entitlement boundary verdict: **Mostly fail-closed, with one confirmed P1 permission bypass in owner reports.** Checkout credit tenders, receivables customer operations, summary, ledger, collections, adjustments, collection void, navigation, direct page access, and checkout choices are guarded by the `ReceivablesUtangLedger` entitlement plus operation-specific permissions. Cash checkout remains under the base `Pos` feature. Unauthenticated requests are rejected, default Cashier permissions do not grant U1, Terminal context is rejected, and disabling the feature retains but blocks access to existing U1 data. However, `/api/owner-reports/summary` requires only `Reports.View`; when the Host has the U1 feature, it includes receivables totals without also requiring `Receivables.View`. A custom report-only user can therefore see AR data outside the stated access rule.

Functional completeness verdict: **Core ledger operations are implemented, but the release UI is not operationally complete.** Customer creation, recurring customer debt, full Utang, Cash + Utang, collection, API-level collection reversal, API-level adjustment, unpaid sale void, fail-closed partially collected void, sale receipt snapshot/reprint, owner reports, backup/restore, and entitlement downgrade/re-enable are present. The main Receivables UI supports search, ledger viewing, and collection. It does not expose collection reversal, adjustment, customer editing, statements, export, or print. Most importantly, an incorrectly entered collection cannot be corrected through the current app UI even though the backend has a reversal operation.

Stability verdict: **The reviewed persistence design is strong but installed/manual proof remains incomplete.** SQLite migrations add durable request identities and uniqueness constraints for checkout debits, collections, and reversals. Checkout and collection paths use transactions and request hashes; repeated requests return the prior result while conflicting payload reuse fails. Existing automated evidence covers concurrent checkout retry, restart recovery, one sale/one stock decrement, collection replay, due/overdue allocation, report cash separation, authoritative receipt customer identity, backup/restore, and entitlement disable/re-enable. This study did not rerun all gates and does not establish installed-app, two-machine, browser, printer, or real operator proof. Customer and ledger queries are unpaged and may become a production scale risk.

Stale/placeholder verdict: **No U1 runtime TODO, FIXME, `NotImplementedException`, mock, fake, or coming-soon implementation blocker was found.** `Receivables.Export` is declared and seeded but has no endpoint/UI consumer. Adjustment and collection-void endpoints are backend-only. Source-string smoke checks exist, but runtime U1 authorization/idempotency tests also exist; the source checks are supplementary, not the only proof. No unused React Receivables page or dead U1 route was identified.

LicensingOps verdict: **The signing boundary is technically safe by default, but operator presentation still needs repair/proof.** The exact case-sensitive feature key is allowlisted. Unknown and duplicate features are rejected; Host licenses require `Pos`; Terminal licenses may contain only `LanTerminals`. Base Host defaults exclude U1. CLI issuance requires the explicit `--utang-ledger` flag, and the localhost checkbox defaults unchecked. The server accepts U1 only when the checkbox value is exactly `true`. Focused tests cover base, paid, and unknown-feature rejection. No committed private signing key or generated real client license was identified. The generated-license review metadata does not visibly list selected features, so an operator can fail to notice whether the paid entitlement was included. Browser/operator QA is still required, and visible feature confirmation should precede paid issuance acceptance.

Public docs verdict: **Pass for pre-release positioning.** `README.md` and `CHANGELOG.md` describe an upcoming, separately quoted optional paid add-on under development/manual QA and explicitly state that it is not included in the currently downloadable installer. They do not present it as available now, included in Salryn Standard, BIR compliant, accounting compliant, or an official accounting/tax module.

Manual QA readiness: **Not ready for final manual acceptance until the P1 report authorization defect is fixed and the correction workflow scope is decided.** After that, the existing branches and automated evidence are suitable for a controlled disposable-license QA pass. Do not use a real client license during acceptance.

## Necessary before packaging

1. Fix the owner-report AR disclosure so receivables data requires both the U1 entitlement and `Receivables.View`. Preserve base report behavior by omitting/nulling the AR section for report-only users.
2. Add a focused runtime authorization test for a user with `Reports.View` but without `Receivables.View`, plus the positive case with both permissions.
3. Decide and implement an accessible collection correction/reversal workflow. A paid receivables product must let an authorized operator correct an erroneous collection without direct API tooling. Include confirmation, reason, actor, timestamp, and idempotent retry behavior.
4. Make the LicensingOps pre-sign or post-sign review visibly show the exact feature list, especially whether `ReceivablesUtangLedger` is included.
5. Run a disposable base-license versus paid-license operator QA script and retain evidence. Confirm checkbox-off, checkbox-on, issued feature lists, unknown-feature refusal, downgrade, and re-enable behavior.
6. Complete installed Host role QA and the U1 matrix below, including receipt/reprint and restored-data behavior.
7. Decide whether collection receipts are part of the paid MVP. If customer proof of payment is promised, implement and verify a collection receipt before packaging; otherwise explicitly exclude it from U1 launch scope.
8. Resolve the misleading customer creation affordance: the checkout button is enabled by `CreateCreditSale`, while the API additionally requires `ManageCustomers`. Either align visibility with both permissions or define the intended permission contract.

## Optional safe-to-defer

- License-page add-on status card or upsell treatment; current entitlement enforcement must remain authoritative.
- Customer statement print/export and customer-ledger export, provided launch wording does not promise them.
- `Receivables.Export` implementation; otherwise remove/reserve it in a later cleanup and do not present it as available.
- Full customer-edit UI and adjustment UI if authorized backend/operator procedures are documented for controlled beta.
- Pagination for a tightly controlled small-store beta; it should be added before broad production scale.
- General audit-feed integration for collections. The ledger and collection records already retain actor/time/note/void metadata, but centralized audit visibility is desirable.
- Phone normalization/format enforcement after policy is decided.
- Credit-limit enforcement if the field is clearly labeled informational and is not marketed as an enforced control.
- Additional source-string smoke cleanup; runtime behavior tests carry higher value.
- Add-on lock/upsell polish beyond a clear unavailable state.

## Questions for Kirch

1. Must Cashiers remain denied by default only, with custom permission grants allowed, or must U1 be hard Manager/Admin-only?
2. Is collection correction/reversal required in the first paid package? This study recommends yes.
3. Must a collection issue its own printable receipt, or is ledger history sufficient for the first controlled beta?
4. Is customer phone optional, required, or required only for credit sales? What normalization policy should apply?
5. Is `CreditLimit` informational, a warning threshold, or a hard checkout block?
6. Should users with `Reports.View` but no `Receivables.View` see reports with the AR section omitted, or receive a full endpoint denial?
7. Are statements/export explicitly out of the first paid scope?
8. Should existing clients receive U1 by commercial exception, or only through separately quoted reissuance?

## Findings by priority

P0 findings: None confirmed.

P1 findings:

- **AR report permission bypass:** `/api/owner-reports/summary` includes receivables totals when the license has U1 but does not require `Receivables.View`. This violates the documented entitlement-plus-operation-permission rule and can disclose customer receivables totals to a custom reports-only role.
- **No app-accessible collection correction:** collection reversal is implemented only at API level. A mistaken collection cannot be safely corrected by an authorized store operator through the release UI. Treat this as an operational acceptance blocker unless Kirch explicitly removes correction from the paid MVP and accepts a controlled support procedure.
- **LicensingOps feature selection is not prominent in output review:** U1 defaults off, but the post-generation metadata does not visibly display the signed feature list. A paid entitlement can be accidentally omitted or included without a clear operator confirmation checkpoint.

P2 findings:

- Checkout exposes inline customer creation based on `CreateCreditSale`, but the endpoint requires `ManageCustomers`, producing a predictable 403 for custom roles.
- Customer search and ledger retrieval are unpaged/unbounded.
- No collection receipt is present; launch scope must not promise one until implemented.
- No customer statement, ledger export, or print workflow is present.
- `CreditLimit` is stored but no checkout enforcement was found; clarify semantics to avoid false operator expectations.
- Sale void safely fails closed when later customer credit activity exists, but it may over-block a legitimate unpaid void after unrelated later credit activity.
- Collection actions are represented in the receivables ledger with actor/time/note and void metadata but are not visibly integrated into the general audit feed.

P3 findings:

- `Receivables.Export` is a declared/default-role permission without an endpoint or UI.
- Phone is optional and has no normalization policy.
- Source-string tests supplement runtime tests and can drift during harmless refactors.
- Add-on status/locked-state presentation can be clearer, but it is not an authorization control.

## Recommended implementation order

1. Repair and runtime-test owner-report receivables authorization.
2. Decide the first paid MVP correction/receipt scope.
3. Add the authorized collection correction UI and align customer-creation permission visibility.
4. Add explicit LicensingOps feature review and focused operator test coverage.
5. Run automated authorization, idempotency, migration, report, backup/restore, and entitlement downgrade gates.
6. Run the disposable-license manual QA matrix on an installed Host.
7. Address agreed launch-scope items such as collection receipts; document deferred statements/export/pagination honestly.
8. Rebuild and rehash the installer only after code and manual acceptance.
9. Update release-facing docs/assets only after the rebuilt artifact is proven.

## Manual QA matrix

| License/state | Role/context | Action | Expected result |
|---|---|---|---|
| Base valid Host | Admin | Cash checkout | Allowed; one sale, correct cash/change and stock decrement |
| Base valid Host | Admin/Manager/Cashier | Utang checkout | U1 option hidden/locked; direct API rejected as feature not licensed |
| Base valid Host | Admin/Manager/Cashier | Cash + Utang | U1 option hidden/locked; direct API rejected |
| Base valid Host with retained U1 data | Admin/Manager | Receivables nav, ledger, collections, AR report | Inaccessible; retained records are not deleted or exposed |
| Host + U1 | Admin | Customer create/search, full Utang, split tender, collection, correction, reports | Allowed according to operation permissions |
| Host + U1 | Manager default | Customer/ledger, Utang, collection, reports | Allowed according to seeded Manager permissions |
| Host + U1 | Cashier default | Navigation, checkout U1 tenders, direct receivables APIs | Denied/hidden by default; Cash checkout remains allowed |
| Host + U1 | Custom `CreateCreditSale` without `ManageCustomers` | Existing-customer Utang and inline customer creation | Existing-customer credit follows permission policy; create affordance must not promise an action the API denies |
| Host + U1 | Custom `Reports.View` without `Receivables.View` | Owner report summary | Base report allowed if intended; AR section omitted/null and no AR totals disclosed |
| Host + U1 | Custom with `Reports.View` and `Receivables.View` | Owner report summary | AR section included |
| Host + U1 | Unauthenticated | Any U1 page/direct API | 401/redirect to login; no data |
| Host + U1 | Terminal context | Any receivables/direct Host endpoint | Denied; only terminal POS checkout surface available |
| Host + U1 | Authorized user | Repeat identical Utang checkout after simulated timeout | Same request ID/result; one sale, one AR debit, one stock decrement |
| Host + U1 | Authorized collector | Repeat identical collection after simulated timeout | Same collection/result; one payment and one AR credit |
| Host + U1 | Authorized collector | Reuse collection request ID with changed amount/customer | Conflict; no additional balance mutation |
| Host + U1 | Authorized manager | Reverse collection twice/retry after timeout | One reversal only; balance restored once; reason/actor/time retained |
| Host + U1 | Authorized user | Receipt and sales-history reprint | Same authoritative customer, cash, Utang, due date, and receipt identity |
| Host + U1 | Authorized user | Partially collected sale void | Fails closed; no balance or inventory drift |
| Host + U1 | Authorized user | Unpaid sale void | Reverses sale/AR/stock exactly once under defined policy |
| Host + U1 | Admin | Backup then restore into U1 license | Customer, ledger, collections, balances, due dates, and receipt links preserved |
| Host + U1 data restored under base license | Admin | Receivables UI/direct API | Data retained but inaccessible |
| Re-enabled Host + U1 | Admin/Manager | Ledger and reports after downgrade | Prior data becomes accessible with unchanged balances and history |
| Invalid/expired Host | Any local role | Cash and U1 operations | Follow global invalid-license policy; U1 never bypasses invalid/expired state |
| LicensingOps checkbox off | Operator | Issue disposable Host license | Signed features exclude U1; visible review confirms exclusion |
| LicensingOps checkbox on | Operator | Issue disposable Host license | Signed features include exact `ReceivablesUtangLedger`; visible review confirms inclusion |
| LicensingOps malformed/unknown key | Operator/test | Attempt issuance/verification | Rejected before signing or rejected by verification; no usable license |

Do not release because: A confirmed P1 receivables report permission bypass remains, the paid workflow lacks an app-accessible collection correction path, LicensingOps feature selection lacks adequate visible operator confirmation, and installed/manual disposable-license acceptance has not been performed.

Installer rebuild: **Required later.** U1 and entitlement work change runtime files. Do not reuse a pre-U1 installer or its SHA.

Release staging: **Not authorized and not started.** Do not create installer, SHA, GitHub Release, real client license, or public availability claim from this study.

Protected refs: `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`; `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`. Read-only verification only; unchanged by this study.

Promotion: **None.** No merge, push, force-push, protected-ref update, installer promotion, or public release action was performed.

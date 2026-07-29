# U1 Installed Manual QA Checklist

Decision: `U1_INSTALLED_MANUAL_QA_CHECKLIST_READY`

Branch: `KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

Source HEAD: `0f4f5c311390ebb6b737d6e2ba8b9aa45fe884ce`

Purpose: Installed-app acceptance of the optional `ReceivablesUtangLedger` paid add-on. This checklist does not authorize release staging, installer rebuild, SHA generation, real client-license generation, merge, or promotion.

## Result record

Tester:

Test date/time:

Host machine:

Terminal device/browser:

Installed build/version:

Installed artifact source:

Base disposable license ID:

U1 disposable license ID:

Invalid/expired disposable license ID:

Backup file used:

Printer or PDF target:

Overall result: `NOT RUN / PASS / FAIL / BLOCKED`

Blocking defect references:

Notes:

## Safety and prerequisites

- [ ] Use a disposable QA store and disposable test licenses only. Do not use a real client license.
- [ ] Do not run this checklist against production client data.
- [ ] Record the installed executable version and source commit before testing.
- [ ] Confirm the installed app launches through the current WebView2 release shell, not the legacy WinForms checkout surface.
- [ ] Prepare three license states without issuing new real licenses:
  - Base valid Host license without `ReceivablesUtangLedger`
  - Valid Host license containing `ReceivablesUtangLedger`
  - Invalid or expired Host license
- [ ] Prepare Admin, Manager, and Cashier users with default seeded permissions.
- [ ] Prepare one paired Terminal device/browser.
- [ ] Configure a safe printer or PDF printer for acknowledgment testing.
- [ ] Record starting database and backup paths.
- [ ] Capture screenshots with secrets, machine codes, signed license JSON, personal data, and passwords redacted.

## Test data

Create or identify:

- [ ] Product A with at least 20 units in stock and a known price.
- [ ] Product B with at least 20 units in stock and a known price.
- [ ] Customer `U1 QA Full Credit`.
- [ ] Customer `U1 QA Split Credit`.
- [ ] Customer `U1 QA Collection`.
- [ ] Customer `U1 QA Reversal`.
- [ ] At least 105 disposable customers for customer-cap testing.
- [ ] One disposable customer with more than 500 ledger entries for ledger-cap testing.
- [ ] A clear collection note/reference such as `QA-COLLECTION-001`.
- [ ] A clear reversal reason such as `QA duplicate collection correction`.

Record starting stock:

| Product | Starting stock | Unit price |
|---|---:|---:|
| Product A |  |  |
| Product B |  |  |

## A. Installed Host launch

| ID | Actor/license | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| A1 | Base Host/Admin | Launch the installed Salryn app. | App starts successfully in the current host shell. No legacy WinForms checkout shell appears. |  |
| A2 | Base Host/Admin | Sign in and inspect navigation. | Host shell is role-aware. Utang Ledger is unavailable because the add-on is not licensed. |  |
| A3 | U1 Host/Admin | Install/select the disposable U1 license and relaunch if required. | App starts successfully. Utang Ledger is available to Admin. |  |
| A4 | U1 Host/Manager | Sign in with default Manager. | Manager sees permitted Host pages and Utang Ledger. |  |
| A5 | U1 Host/Cashier | Sign in with default Cashier. | Cashier remains restricted to Cashier pages; Utang Ledger navigation is absent. |  |

## B. Base Host license

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| B1 | Admin | Inspect license status/features. | `ReceivablesUtangLedger` is not included. |  |
| B2 | Admin | Complete a Cash checkout for Product A. | Cash checkout succeeds; one sale is created; cash/change are correct; stock decrements once. |  |
| B3 | Admin | Inspect checkout payment choices. | Utang and Cash + Utang are hidden or unavailable. |  |
| B4 | Admin | Attempt direct navigation to the receivables page. | Access denied or redirected; no U1 data appears. |  |
| B5 | Admin | Attempt a direct U1 API request using approved diagnostic tooling. | HTTP 403 with feature-not-licensed behavior; no mutation. |  |
| B6 | Manager | Repeat U1 navigation/direct request. | U1 remains unavailable despite Manager Receivables permissions because the license lacks the feature. |  |
| B7 | Cashier | Complete another Cash checkout. | Cash remains usable; U1 remains unavailable. |  |

## C. Valid Host license with U1 entitlement

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| C1 | Admin | Inspect license status/features. | Exact feature `ReceivablesUtangLedger` is enabled. |  |
| C2 | Admin | Open Utang Ledger. | Summary, customer search, customer ledger, and collection controls load. |  |
| C3 | Manager | Open Utang Ledger. | View, customer, credit-sale, and collection actions follow Manager permissions. |  |
| C4 | Cashier | Inspect navigation and checkout. | Default Cashier cannot access Utang Ledger and cannot select Utang tenders. |  |
| C5 | Cashier | Attempt direct U1 API calls. | HTTP 403 permission denial; no data mutation. |  |

## D. Cash, Utang, and Cash + Utang checkout

| ID | Actor/license | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| D1 | Admin/U1 | Complete a normal Cash checkout. | Cash behavior is unchanged; sale/stock/cash totals are correct. |  |
| D2 | Admin/U1 | Sell Product A entirely on Utang to `U1 QA Full Credit`. | One sale, one stock decrement, one AR debit, zero cash received. |  |
| D3 | Admin/U1 | Inspect the customer ledger. | Entry label is `Credit Sale`; customer, amount, due date, note, and balance are correct. |  |
| D4 | Manager/U1 | Sell Product B using Cash + Utang to `U1 QA Split Credit`. | One sale and stock decrement; cash and credit portions match checkout; no duplicate AR debit. |  |
| D5 | Manager/U1 | Inspect the customer ledger. | Entry label is `Partial Credit Sale`; new balance equals only the Utang portion. |  |
| D6 | Authorized operator/U1 | Simulate an ambiguous checkout response if the approved QA harness supports it, then retry. | Same request reconciles to one sale, one stock decrement, and one AR debit. |  |
| D7 | Cashier/U1 | Attempt full Utang and Cash + Utang. | Both are denied by default; Cash checkout remains available. |  |

## E. Collection and payment acknowledgment

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| E1 | Manager | Open a customer with an outstanding balance. Enter a valid amount and `QA-COLLECTION-001`. | Collection succeeds; no second sale is created; one AR credit reduces the balance. |  |
| E2 | Manager | Inspect the new ledger row. | Label is `Payment Collection`; credit, balance, timestamp, and note are correct. |  |
| E3 | Manager | Inspect the generated Collection Receipt/payment acknowledgment. | It shows customer name, amount paid, previous balance, new balance, collected by, date/time, method, and note/reference. |  |
| E4 | Manager | Compare receipt balances with the ledger immediately before and after collection. | Previous and new balances match the authoritative ledger. |  |
| E5 | Manager | Select `Print acknowledgment` and print to the safe printer/PDF target. | A dedicated print view opens and prints readable operational acknowledgment text. |  |
| E6 | Manager | Inspect displayed and printed wording. | Uses `Collection Receipt`/operational acknowledgment wording. It does not claim BIR compliance, accounting compliance, accreditation, or official tax-receipt status. |  |
| E7 | Authorized operator | If the approved harness supports response loss, retry the same collection request ID. | Original collection and acknowledgment reconcile; no duplicate collection or AR credit. |  |

## F. Collection reversal

| ID | Actor/context | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| F1 | Manager default | Open the ledger containing a collection. | Reverse action is not visible because Manager lacks `Receivables.Adjust` by default. |  |
| F2 | Manager default | Attempt the reversal API directly. | HTTP 403 permission denial; collection and balance unchanged. |  |
| F3 | Cashier default | Attempt UI/direct reversal. | No UI action and HTTP 403; collection and balance unchanged. |  |
| F4 | Terminal | Attempt direct reversal against the Host. | Terminal/local-access guard denies the request; no mutation. |  |
| F5 | Admin | Open the original collection row. | Reverse action is visible and enabled only for the active collection. |  |
| F6 | Admin | Select Reverse without a reason. | Confirmation cannot be submitted. |  |
| F7 | Admin | Enter a reason but do not check confirmation. | Confirmation cannot be submitted. |  |
| F8 | Admin | Check confirmation and submit. | Controls lock while pending; exactly one reversal succeeds. |  |
| F9 | Admin | Inspect ledger and balance. | Original `Payment Collection` remains; one `Collection Reversal` is appended; balance is restored once. |  |
| F10 | Admin | Inspect reversal metadata. | Required reason, actor, and timestamp are retained. |  |
| F11 | Admin | Retry the same reversal through a stale page/direct request. | HTTP 409 and exact message `This collection was already reversed.` No second reversal or balance change. |  |
| F12 | Admin | Reload the ledger. | Original collection action shows `Reversed` and remains disabled. |  |

## G. AR report permission gating

Prepare disposable custom roles/users only if supported by the current Users UI or approved QA setup.

| ID | License/permissions | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| G1 | U1 + `Reports.View`, without `Receivables.View` | Open owner reports or call its API. | Base owner report remains available if intended; AR section is absent/null and no AR totals leak. |  |
| G2 | U1 + `Reports.View` + `Receivables.View` | Open owner reports. | AR section appears and totals agree with customer ledgers. |  |
| G3 | Base Host + both permissions | Open owner reports. | AR section is absent because U1 is not licensed. |  |
| G4 | U1 without `Reports.View` | Attempt owner reports. | Report endpoint/page is denied. |  |
| G5 | Terminal | Attempt owner-report/AR access. | Terminal cannot access Host administrative reports. |  |

## H. Invalid or expired license

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| H1 | Admin | Apply the invalid/expired disposable Host license and launch/reload. | App follows global invalid/expired-license policy. |  |
| H2 | Admin | Attempt Cash checkout. | Behavior matches global license policy; record exact denial/status. |  |
| H3 | Admin/Manager | Attempt Utang Ledger and direct U1 APIs. | U1 is not usable; invalid/expired state cannot be bypassed by feature or permission. |  |
| H4 | Cashier/Terminal | Probe their normal surfaces. | Invalid/expired state does not expose U1 or Host administration. |  |

## I. License downgrade and re-enable

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| I1 | Admin/U1 | Record customer, full/split credit, collection, reversal, and notes. Record balances. | Baseline U1 data exists. |  |
| I2 | Admin | Take a verified backup before changing license state. | Backup succeeds and verification reports restorable. |  |
| I3 | Admin | Replace U1 license with the disposable base Host license. | U1 navigation and API access disappear; existing U1 data is not deleted. |  |
| I4 | Admin/Manager | Open owner reports. | AR section is omitted under the base license. |  |
| I5 | Admin | Re-enable the disposable U1 Host license. | U1 navigation returns for authorized roles. |  |
| I6 | Admin/Manager | Reopen customers and ledgers. | All prior customers, balances, due dates, collections, reversals, notes, and labels reappear unchanged. |  |
| I7 | Cashier | Sign in after re-enable. | Cashier remains denied by default. |  |

## J. Backup and restore

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| J1 | Admin/U1 | Create and verify a backup containing representative U1 data. | Backup succeeds and is marked restorable. |  |
| J2 | Admin/U1 | Record a clearly identifiable post-backup collection or adjustment. | Post-backup mutation is visible before restore. |  |
| J3 | Admin/U1 | Restore the verified backup using the required confirmation. | Restore completes successfully. |  |
| J4 | Admin/Manager | Inspect restored U1 data. | Pre-backup customers, balances, ledger entries, collections, reversals, notes, and due dates are intact. |  |
| J5 | Admin/Manager | Search for the post-backup marker. | Post-backup mutation is absent after restore. |  |
| J6 | Admin | Perform a new collection after restore. | Collection and acknowledgment work normally; balance continues from restored state. |  |

## K. Customer search cap

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| K1 | Admin/Manager | Ensure at least 105 disposable customers match an empty or broad search. | Setup count exceeds the backend cap. |  |
| K2 | Admin/Manager | Run the broad customer search. | At most 100 customers are returned/rendered; UI remains responsive. |  |
| K3 | Admin/Manager | Search for a specific customer by distinctive name/phone. | Matching customer appears when within the bounded result set; no crash or unbounded delay. |  |
| K4 | Admin/Manager | Clear and repeat the search several times. | Results remain deterministic and UI remains usable. |  |

Note: The current safeguard is a backend cap, not full pagination. Record any discoverability issue for customers outside the first 100 as a deferred usability item, not a cap failure.

## L. Ledger entry cap and labels

| ID | Actor | Procedure | Expected result | Result/evidence |
|---|---|---|---|---|
| L1 | Admin/Manager | Open the disposable customer with more than 500 ledger entries. | At most 500 entries are returned/rendered; UI remains responsive. |  |
| L2 | Admin/Manager | Confirm entry ordering. | The most recent 500 entries are present and displayed chronologically. |  |
| L3 | Admin/Manager | Confirm all available entry labels. | Labels are operator-facing: Credit Sale, Partial Credit Sale, Payment Collection, Collection Reversal, Credit Sale Reversal, Adjustment. |  |
| L4 | Admin/Manager | Compare final visible balance with customer summary after adding a new entry. | The newest entry and current balance are present despite the cap. |  |

## M. Terminal isolation

| ID | Procedure | Expected result | Result/evidence |
|---|---|---|---|
| M1 | Pair the disposable Terminal to the installed Host. | Pairing succeeds using the existing LAN flow. |  |
| M2 | Open the Terminal shell. | Only permitted remote POS checkout functionality appears. |  |
| M3 | Probe Utang Ledger, customer search, ledger, collection, reversal, owner reports, settings, backup, users, and LAN control. | All Host/U1 administrative surfaces are absent or denied. |  |
| M4 | Attempt Cash checkout through the Terminal according to existing terminal policy. | Existing Terminal Cash behavior remains unchanged. |  |
| M5 | Attempt Utang or Cash + Utang through Terminal UI/devtools/direct calls. | U1 is denied; no AR mutation occurs. |  |

## N. Wording and presentation audit

- [ ] Search visible U1 navigation, checkout, ledger, reports, acknowledgment, print view, and errors for compliance wording.
- [ ] Confirm `Collection Receipt` or `Payment Acknowledgment` is used operationally.
- [ ] Confirm no U1 surface says `official tax receipt`.
- [ ] Confirm no U1 surface claims BIR compliance, accreditation, registration, or approval.
- [ ] Confirm no U1 surface claims accounting compliance or presents U1 as an accounting system.
- [ ] Confirm public/product wording, if shown in-app, remains “optional paid add-on” and operational receivables tracking.
- [ ] Record exact text and screenshot for any questionable wording.

## Exit criteria

The installed U1 manual QA result may be marked PASS only when:

- [ ] Every required row above has a recorded result and evidence reference.
- [ ] No P0/P1 defect remains open.
- [ ] Base, U1, and invalid/expired license states behave as expected.
- [ ] Admin/Manager/Cashier/Terminal boundaries are proven.
- [ ] Cash, Utang, and split checkout persistence is correct.
- [ ] Collection acknowledgment displays and prints correctly.
- [ ] Reversal is Admin-only by default and duplicate-safe.
- [ ] AR report disclosure remains closed.
- [ ] Downgrade/re-enable retains data without exposing it while unlicensed.
- [ ] Backup/restore retains authoritative U1 state.
- [ ] Customer and ledger caps prevent unbounded responses.
- [ ] No BIR/accounting compliance wording is present.

If any required step fails:

1. Stop the affected flow.
2. Record exact role, license state, request/action, expected result, actual result, screenshot/log, and whether data mutated.
3. Classify severity without patching during this checklist run.
4. Do not proceed to release staging.

## Release control

Installer rebuild: Not performed.

Release staging: Not authorized and not performed.

SHA generation: Not performed.

Real client licenses: None generated.

Protected refs: `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`; `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`.

Merge/promotion: None.

# Human QA — 31-case acceptance checklist

Source SHA: ______ Manifest: ______ Tester/date: ______
Environment/profile/license scope: ______

Synthetic data only. Record PASS / FAIL / NOT RUN / HUMAN_REQUIRED, expected/actual ledger values, receipt/operation IDs and artifacts. UI clicks/HTTP200 alone are not financial proof. Restricted core-cashier testing does not waive final C1-C11 scope or independent review.

| Case | Area | Required observation | Verdict / evidence |
|---|---|---|---|
| 01 | Package | Verify matched source SHA and all three checksums. | |
| 02 | Windows install | Install Cafe independently; record exit/version. | |
| 03 | Coexistence | Protected Retail binaries/data/ports remain unchanged. | |
| 04 | Windows startup | Ordinary-user Desktop launches real Cafe workstation. | |
| 05 | Host | Verify ProductId Salryn.Cafe, loopback5399 and guarded HTTP. | |
| 06 | License | Authorized Release entitlement succeeds; absent entitlement fails closed. | |
| 07 | Android install | Install matched QA APK and record identity/version. | |
| 08 | TLS | Valid approved HTTPS succeeds; invalid certificate rejected. | |
| 09 | Invite | Expiring invite works once; expired/reused denied. | |
| 10 | Approval | Unapproved device blocked; admin approves exact identity. | |
| 11 | Scope | Wrong product/tenant/store/pin rejected without writes. | |
| 12 | Operator | Central enrollment then offline local login; invalid credentials denied. | |
| 13 | RBAC | Reduced user cannot credit/discount/refund/manage setup. | |
| 14 | Menu | Published names/prices/variants match authority. | |
| 15 | Modifiers | Required/min/max modifiers and catalog pricing enforced. | |
| 16 | Shift open | Genuinely new drawer with exact opening cash and operation ID. | |
| 17 | Exclusivity | Other device same operator cannot overlap drawer custody. | |
| 18 | Order | Save/preparation/Ready preserve revisions and totals. | |
| 19 | Cash | Cash sale receipt/change/retained cash/stock agree. | |
| 20 | AR | Valid customer credit sale reconciles receivable debit/receipt. | |
| 21 | Mixed | Partial Cash+AR exact; other tenders remain locked. | |
| 22 | Retry | Double submit/retry yields one financial effect. | |
| 23 | Offline | Valid lease permits durable approved offline workflow. | |
| 24 | Restart | Saved orders/intents/IDs/ledger survive restart. | |
| 25 | Reconnect | Actual Central Applied receipt resolves local outbox durably. | |
| 26 | Repeat sync | Repeated reconnect/restart adds no duplicates. | |
| 27 | Revocation | Revoked device/expired or rolled-back lease blocks mutation. | |
| 28 | Blind close | Counted physical cash entered without expected totals; close once. | |
| 29 | Printing | Actual receipt/kitchen output; failed/unknown never claims success. | |
| 30 | Usability | Touch/keyboard/small viewport/errors/recovery usable. | |
| 31 | C1-C11 recovery | Record scope/NeedsReview gaps and multi-client ledger recovery; unresolved gaps block final acceptance. | |

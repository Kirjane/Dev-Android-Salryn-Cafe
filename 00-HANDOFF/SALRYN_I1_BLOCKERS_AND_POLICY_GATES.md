# Salryn I1 Blockers and Policy Gates

Tested SHA: `9bedd0819125ba643badbe6c8c54340b0563ce12`

## Pending authority — not engineering PASS

All five provisional W4 financial policies remain **PENDING KIRCH APPROVAL**:

1. variance classification and tolerance;
2. who may review discrepancies;
3. prohibition/handling of self-review;
4. close-note requirements and retention;
5. escalation and final disposition of reviewed discrepancies.

The dedicated discrepancy-review role is **NOT APPROVED**. No test or UI implementation activates a financial policy. The integration preserves `PendingReview`, immutable audit/replay semantics, and authorization denial without asserting that any reviewer assignment is accepted.

## Genuine external / physical gates

| Gate | Status | Exact human action |
|---|---|---|
| Licensed disposable integrated Host | `BLOCKED_NO_HOST` | Provide the approved dev/test Host and lawful signed test license; run the integrated-host Playwright project without bypassing signature checks. |
| Real physical drawer/operator acceptance | `NOT_RUN` | On disposable store data, open a shift, perform cash/Utang/split/collection events, physically count zero/short/over, close once, retry after induced response loss, and verify cash/audit records do not duplicate. |
| Dedicated discrepancy reviewer | `NOT APPROVED` | Kirch approves the role/permission and self-review policy before a review UI can be accepted. |
| Denied-review integrated screenshot | `NOT_RUN_NO_REVIEW_UI` | After the role policy/UI is approved, capture cashier self-review denial and manager review at required viewports. Backend denial already passes. |
| LicensingOps canonical activation | `NOT_RUN_LAST_GATE` | Run only after the candidate and policy are approved, in controlled LicensingOps infrastructure; do not expose keys/licenses. |

No signing, license issuance, deployment, release, distribution, merge, or production claim is authorized. PR #21, #22, #18, and #10 remain outside this integration branch and must not be modified.

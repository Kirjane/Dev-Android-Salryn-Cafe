# RMO-BIR-M9 Output Engine Decision

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`

**Disclaimer:** Engineering decision only. No legal output. No compliance claim.

---

## Answers

| Question | Answer |
|----------|--------|
| Is Crystal necessary **now**? | **No.** No BIR source text requires Crystal Reports specifically. Not proven necessary. |
| Is HTML/text/JSON enough for internal reports? | **Yes** for M9 internal engineering previews. |
| What would make Crystal necessary? | Explicit customer/host requirement for existing Crystal templates, or a maintained accredited layout stack already in Crystal with no equivalent path — neither proven in-repo. |
| What would make PDF necessary? | Formal examiner package / legal archival layout once official issuance is authorized; not required for internal dry-run. |
| What would make printer integration necessary? | Live issuance of physical SI/OR at checkout after legal + product gates pass. |
| What must be proven before official output implementation? | Legal closure (seal, eSales class path), live posting design implemented & tested, layout field matrix vs S2/S3, watermark/training/reprint rules, host-local only, no remote issuance, backup/restore integrity, and explicit non-claim of compliance until accreditation process. |

---

## Decision for this branch

| Engine | Decision |
|--------|----------|
| Crystal | **STOP** — not implemented |
| PDF legal | **STOP** — not implemented |
| Printer | **STOP** — not implemented |
| Internal JSON / plain text / internal HTML preview | **GO** — watermarked only |
| Official output generation | **false** always |

**Watermark (required):**  
`INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE`

Internal reports are enough for this branch.

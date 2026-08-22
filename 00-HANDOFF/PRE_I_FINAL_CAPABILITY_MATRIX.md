# PRE-I Final Capability Matrix

## Authority and status meanings

This matrix records the bounded capability truth sealed by `PRE_I_FINAL_STACK_SEAL_PASSED`. It does not replace physical-device, package, installer, legal, or production proof.

| Status | Meaning |
| --- | --- |
| Code-proven | Covered by the passing Salryn or LicensingOps automated gate stack. |
| Beta-acceptable | Accepted for the PRE-I beta candidate within the stated limitation. |
| Manual proof required | Code may exist, but the physical/runtime outcome must still be checked by a person. |
| Deferred | Explicitly outside this seal; do not claim it as complete. |

## Sealed capability truth

| Area | Status | Sealed boundary |
| --- | --- | --- |
| Local-first host POS and checkout | Code-proven | Core checkout regression gates pass; no server-side idempotency key exists yet. |
| Barcode cashier flow and unknown-barcode fast add | Code-proven | POS M3 and M4 focused gates pass. |
| Product write management | Code-proven | Product name, price, barcode, and other accepted write paths pass POS M5 and PRE-I contract gates. SKU creation/editing remains deferred. |
| Stock-in and stock adjustment | Code-proven | POS M6 focused gate passes. |
| Client-facing product and shell polish | Code-proven | Product polish, collapsible sidebar, checkout layout, wrapping, and overflow guards pass their PRE-I gates. |
| Action notifications | Code-proven | Registered notification source/runtime gate passes; final visual behavior remains subject to normal browser QA. |
| Audit CSV export and audit coverage | Code-proven | Automated audit gates pass. Evidence is operational, not legal-grade atomic evidence. |
| Backup create, verify, and restore paths | Code-proven | Covered by the full regression stack; physical/operator workflows remain bounded by existing confirmation and restore rules. |
| Canonical Salryn license activation proof | Code-proven | Salryn canonical activation gate passes against the pinned key contract. No production activation was performed. |
| LicensingOps key rotation and cross-repo proof | Code-proven | LicensingOps build, default tests, and M3-M6 suites pass at the pinned LicensingOps candidate. |
| LAN reachable-address and terminal-link truth | Code-proven | UI reflects detected/reachable bind state and restart-required state; no-restart runtime rebind is deferred. |
| LAN terminal activation and management | Beta-acceptable | Terminal lifecycle and management paths are accepted for beta; UI/UX is not fully polished. |
| Physical terminal operation | Manual proof required | Separate-device reachability, activation, and operation remain manual proof. |
| Receipt printer operation | Manual proof required | Software paths are covered by regression evidence; physical printer and paper output remain manual. |
| Package/I1 output | Deferred | No package/I1 proof yet. Recommended next lane is `KIRCH-I1-PACKAGE-REFRESH-PRE-BETA`. |
| Desktop shortcut/WebView2 app shell | Deferred | Not built or proven by this seal. |
| Installer | Deferred | No installer proof; `KIRCH-INSTALLER-M1` was not moved. |
| BIR/eSales readiness | Deferred | No readiness, accreditation, submission, or production claim. |
| Production activation | Deferred | Not performed. |
| Branch promotion | Deferred | No accepted or protected branch was promoted by this seal. |

## Pinned evidence

| Repository role | Branch | SHA |
| --- | --- | --- |
| Final Salryn candidate | `KIRCH-PRE-I-STALE-DOC-REPAIR-FOR-SEAL` | `1a173994dd23f46c62c13fc7b54ca44a6cdf65c0` |
| Functional Salryn candidate | `KIRCH-PRE-I-LAN-REACHABILITY-AND-TERMINAL-COPY-REPAIR` | `373280da909a77277315364a7fa02d814af87291` |
| LicensingOps candidate | `KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF` | `f73bf74a6e68f835640d0ea3942f979f8fc823d9` |

Both repository gate sets passed before this matrix was sealed.

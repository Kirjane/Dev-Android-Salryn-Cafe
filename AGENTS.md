# Salryn POS — Single-Agent Engineering Rules

Repository: `Kirjane/Salryn` (base School Supplies / Retail POS, **not** Café Yza).
Authority: Kirch Ivan Balite, Engineer. Executor: Antigravity, **one agent acting as Implementer, Reviewer, QA and Release Engineer in successive checkpoints**. No Codex, no delegation, no new agent threads or remissions.

## Business objective (CEO delivery)
Deliver the most complete **working, verified** implementation of CEO Paul's supplied requirements within the urgent client window. **All requested features are IN SCOPE**; do not automatically defer payments, quotation canvassing, customer orders, printed procurement documents or dashboard analytics merely because they are substantial. Prioritize by end-to-end business dependency, implement coherent slices, self-review, correct failures, then release. The four-hour build target and seven-hour client meeting are urgency targets, **not evidence of acceptance and not an excuse to fabricate PASS**.

Original CEO QA screenshot was from an older Codex package on `ZAY OFFICE AND SCHOOL SUPPLIES`; R2.1 QA screenshots came from a different `KIRJANE IT SOLUTIONS` environment. Do not conflate them. Existing R2.1 source baseline: `d07f73a21c2832257d48f5023d01047149e82003`, PR #32. New delivery work must originate from this verified baseline with a dedicated candidate branch; preserve frozen PR #32, installed data and rollback. Preserve the user's currently restored **uncommitted** `AGENTS.md` and `.agents/skills` files. Never blindly reset, clean or discard local changes.

## Source-of-truth order
1. Latest Engineer message and CEO QA requirements described in `.agents/skills/salryn-deadline-delivery/SKILL.md`.
2. This `AGENTS.md` and `.agents/skills/salryn-forge-cycle/SKILL.md`.
3. Live source, real UI/API/service/SQLite contracts, present tests and existing evidence at exact SHA.
4. Historical issues or reports as context only; do not start old work streams.

## Non-negotiable execution shape: FORGE
Run **one continuous serial workflow**: `BASELINE -> DISCOVER ONLY CHANGED CONTRACT -> IMPLEMENT -> SELF_REVIEW -> SELF_TEST -> REWORK (if needed) -> RETEST -> ACCEPT SLICE -> INTEGRATE -> RELEASE_TEST -> PACKAGE -> INSTALLED_VERIFY -> REPORT`.
- Review is **adversarial self-review** by the same agent, not misrepresented as independent approval.
- Every meaningful implementation slice must have a positive case, negative/auth case, persistence/accounting assertion where relevant, and a visible UI result.
- At failure, enter bounded rework (two focused repair attempts for the same failure signature, then concise root-cause diagnosis; do not loop blindly or re-run the whole suite). Continue with feasible dependencies and accurately identify remaining blockers.
- Reuse true evidence from unchanged SHA; revalidate changed paths and regressions at new SHA. Never replace integrated testing with screenshots or build-only PASS.
- Keep report chatter minimal: initial scope + critical blocker + slice checkpoints + final result. Do not narrate every command, wait, script, scan, screenshot or browser click.

## Source and financial invariants
- Source read path: actual screen -> TS API -> C# Local endpoint -> permission -> service -> SQLite migrations/rows -> existing focused tests. Read adjacent files only when needed.
- Authorize UI and C# server together. All authenticated users may request PR as CEO directed; cashier gets **limited** request/quotation/supplier-assignment access, not AP/treasury/receiving powers. Manager/admin PR self-confirmation is an explicit CEO policy change requiring auditable identity/time, tests and protection against unauthorized roles; it is not carte blanche for payment/stock self-approval.
- Preserve idempotency, continuous document numbering, immutable financial/audit traces, inventory quantity arithmetic, separation of POS drawer from supplier AP disbursements, and accurate cash vs noncash vs AR reporting. Never fake payment gateway confirmation, tax compliance, approval signatures or stock cost coverage.
- Use disposable DB for all mutating QA, including sales, tender, stock, PR/PO/RR, supplier returns, AP payments, customer quote conversion, client order fulfilment. Real operator data is read-only during QA. Never guess credentials/create unknown backdoor accounts or bypass signed license.
- Back up license and transactional SQLite with consistent WAL snapshot before any **authorized** customer update. Verify database and license after restart. Validate exact installer source SHA and binaries before claiming installed success.

## Delivery rule
Aim to COMPLETE the CEO acceptance matrix, not a decorative mock or partial backend. Organize by dependencies and implement as much as genuinely works. At the business cutoff provide one tested, usable release candidate with exact SHA/hash and an explicit feature-by-feature PASS / FAIL / NOT COMPLETED matrix. Continue only within the authority and window given; never falsely mark a missing feature complete. If a required financial operation fails, do not silently ship a broken transaction workflow.

## Skills
- `.agents/skills/salryn-deadline-delivery/SKILL.md`: canonical CEO feature matrix and implementation order.
- `.agents/skills/salryn-forge-cycle/SKILL.md`: required Forge self-review and test/rework/acceptance state machine.
- Other four legacy skills: opt-in and only when their targeted work adds evidence. Do not resurrect W4 branches, Issue #19, old Codex handoffs, wholesale audits or automatic full screenshot campaigns.

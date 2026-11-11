---
name: salryn-forge-cycle
description: Mandatory single-agent Forge implement-review-test-rework-release workflow for the Salryn CEO client delivery. One worker, explicit state transitions, real Host evidence, bounded repair loops.
---

# KIRION FORGE — Single-Agent Execution, Self-Review and Quality Gates

## One active execution
Owner/authority: Engineer. Executor: Antigravity. **No other agents**. The same worker alternates deliberate roles: Implementer -> Skeptical Reviewer -> QA Operator -> Release Engineer. Do not suggest that self-review is an independent human review.

The mission is the **complete CEO acceptance matrix** in `salryn-deadline-delivery/SKILL.md`. Run cohesive business slices, not dozens of unrelated micro-missions.

## FORGE state machine

```text
START -> BASELINE_CUSTODY -> SLICE_SELECTED -> CONTRACT_TRACED
      -> IMPLEMENTED -> SELF_REVIEW
      -> [review defect] -> REWORK -> SELF_REVIEW
      -> FOCUSED_TESTS
      -> [test fail] -> ROOT_CAUSE -> REWORK -> FOCUSED_TESTS
      -> REAL_HOST_JOURNEY -> [fail] -> ROOT_CAUSE -> REWORK
      -> SLICE_ACCEPTED -> NEXT_SLICE
      -> ALL_INTEGRATED -> REGRESSION -> SOURCE_FROZEN
      -> PACKAGE -> WINDOWS_TEST -> INSTALLATION_ACCEPTANCE
      -> FINAL_HANDOVER
```

A slice is **ACCEPTED only after evidence**: source change; self-review; targeted tests exit 0; real UI action, authenticated Local API request and SQLite outcome on isolated data; negative unauthorized/invalid check; restart proof if it writes durable state. If external provider/hardware integration is absent, report exact simulated vs real boundary.

## Stage 0 — CUSTODY (once)
- Record actual `git status --short`, `git rev-parse HEAD`, source branch, installed target/store and existing documentation-only changes. Protect uncommitted user files. Create a **single** implementation candidate branch from verified source without switching to older baselines.
- Compare current R2.1 to CEO screenshots before treating old Codex defects as current.
- Name runtime services, disposable test DB and existing test commands. Reuse existing tests: `src/Salryn.App.Web/e2e/integrated-host-lpi-acceptance.spec.ts`, `w5-lpi-runtime-regressions.spec.ts`, `jane-checkout-workstation.spec.ts`, `tests/Salryn.Tests/LpiProcureToPayRuntimeChecks.cs`, `tools/installer/Build-SalrynBetaRc1Installer.ps1`. Verify the actual scripts in current tree before invoking.
- Create one checklist of CEO requirements. State planned acceptance scenario, not sprawling architecture notes.

## Stage 1 — CONTRACT & IMPLEMENT (per slice)
- Read minimal complete source chain: UI action -> TS request -> ASP.NET Local endpoint/authorization -> domain service -> SQLite model/migration -> result UI.
- Make an end-to-end vertical change, not button-only/backend-only. Add focused test fixture and error message in same slice.
- Record contract and invariants; use integer cents, fixed-point quantities, explicit dates/timezone, stable business identifiers. Protect idempotent operations, approval audit and partial payment/receiving arithmetic.
- Do not use placebo controls, fixed fake records, guessed missing data, artificially green errors or disabled backend validation.

## Stage 2 — SKEPTICAL SELF-REVIEW (every slice)
Temporarily assume your own change is wrong. Independently inspect `git diff --check`, only the changed files + caller/callee tests. Answer and fix each:
1. Does it implement the CEO's requested user journey, or only a screenshot?
2. Wrong route, DTO name, camelCase, missing field, enum or server authorization?
3. Can a cashier/admin bypass access by calling the API directly? What does 401/403 return?
4. Duplicate clicks/retries causing two PRs/POs, stock receipts, AP postings or sales?
5. Partial/negative amounts, duplicate products/PR lines, split tender imbalance, rounding, expired quotation, overdelivery, over-return or AP residual?
6. Does a failed request leave a partial mutation? Database transaction/rollback?
7. Does signed printed name, date and document number reflect durable actual records?
8. Does navigation surface entitlements correctly? Loading/error/denied/empty not disguised as zero?
9. Are prior checkout, AR, shift, reports, licensing and user data preserved?
10. Can a real operator understand and complete it without unsupported instructions?

Write small REVIEW_FINDINGS (file:line, severity, repair) internally. Repair P0/P1 before testing. Do not endlessly rewrite design with no observed defect.

## Stage 3 — SELF-TEST (targeted first, integrated next)
- Build/typecheck only changed subsystem after each slice. Run existing focused .NET and Playwright specs where supported, then add tests for new behavior.
- **Mandatory test evidence** for each complete financial slice: `UI click -> real authenticated loopback request/response -> service -> disposable SQLite before/after -> UI result -> restart -> persistent record`.
- Include expected failure paths: 400 validation, 401 unauthenticated, 403 wrong role, 409 conflict/idempotency when applicable; no business mutation on failure.
- **Cross-domain checks**: PR/PO sequence continuity; RR posted stock buckets; supplier returns reduce eligible accepted quantities only; invoice match/variance and AP aging; tender sum=checkout total; cash drawer reflects cash only; AR credit balance; printed quote never creates sale; client order conversion never charges twice.
- For new charts use actual API dataset plus accessible visible chart/table; reconcile aggregates to sales records, voids and missing cost coverage.
- Reuse unchanged-SHA evidence as context. At release candidate run final integrated regression once at the **new exact SHA**.

## Stage 4 — BOUNDED REWORK
- On failure, capture failing test + request/response + stack/SQL. Classify as data fixture, UI contract, permission, service logic, migration, environment or packaging.
- **Repair attempt 1**: smallest root-cause correction; rerun failure and nearby negative case.
- **Repair attempt 2**: if same failure persists, revisit assumption from another boundary and add regression; rerun targeted case.
- If same signature fails again, **STOP that loop**, log precise blocker and move to another independent acceptance slice or raise one targeted question to Engineer only if no safe progress is possible. Do not poll/restart/reset repeatedly. Do not declare PASS. Different failure signature can start a new bounded investigation.
- Following PASS, record `SLICE_ACCEPTED` and never redo it unless affected by later code diffs.

## Stage 5 — INTEGRATE & RELEASE
- Integrate slices on one candidate, run: TypeScript build, .NET Release build, affected Playwright (mock + INTEGRATED_HOST), procurement/financial/core regressions, one final CoreSmoke, installed WebView2 navigation and screenshots at actual version.
- Check DB integrity, migrations, sample before/after counts, license binding, source/binary hashes. Snapshot/restore dry run before any authorized customer update.
- Package through established Windows installer pipeline from exact clean committed source; verify installed desktop + API executable hash, WebDist identity and actual target. Test reopen/restart, checkout payment flow and at least one procurement journey on isolated data.
- Do not use credentials discovered through guessing or create persistent QA users in operator DB. QA accounts and fixtures exist only in disposable isolated environment.
- No automatic merge, license resets, customer DB mutation or production install simply because a test passed; execute only with authorized release target and preserved rollback.

## Reporting (not command narration)
At most three routine messages: `START` (scope + SHA + first slice); `CHECKPOINT` (accepted slices / next); `CLOSE` (matrix + actual artifacts). Exceptions: material blocker or unsafe release issue.
Required final fields: target/store; original vs candidate SHA; implementation file list; each CEO requirement `PASS_INTEGRATED` / `PASS_INSTALLED` / `FAIL` / `NOT_TESTED`; command/test exits; screenshot/response/DB evidence; installer hashes; rollback; observed unresolved risks; delivered status.
**Do not give an all-green declaration based on a screenshot, smoke suite, or signing/license health alone.**

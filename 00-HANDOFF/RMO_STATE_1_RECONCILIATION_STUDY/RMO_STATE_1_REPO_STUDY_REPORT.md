# RMO-STATE-1 Repo Study Report

Study only. No runtime code, schema, or behavior was changed by this task. The only files written are the handoff artifacts under `00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/`.

## Source Stamp

| Field | Value |
|---|---|
| Branch (checked out) | `KIRCH-FREEHAND` |
| HEAD | `1d509158b2964d040ddfba195817f1ba0d40ce03` |
| `origin/KIRCH-SANDBOX` | `1d509158b2964d040ddfba195817f1ba0d40ce03` |
| `origin/KIRCH-FREEHAND` | `1d509158b2964d040ddfba195817f1ba0d40ce03` |
| Ahead / behind (Sandbox vs Freehand) | `0 / 0` (all three refs identical) |
| Worktree status before study | clean (`git status --short` empty) |
| Worktree status after study | dirty **only** because of the generated handoff artifacts in this folder |
| Date/time local | 2026-07-01, UTC+8 |
| HEAD commit subject | `Seal official output text file workflow` |

Gate result: **PASS.** Worktree was clean, HEAD matched the expected stable SHA, and `KIRCH-FREEHAND` matched `KIRCH-SANDBOX` exactly. No stop condition was triggered.

## Executive Finding

Salryn is a single-machine, offline Windows POS whose **official-output and official-checkout data spine is materially more complete than the sealed compliance documents claim**, but **nothing is exposed, promoted, or claimed as compliant**. What is genuinely stable and smoke-backed: the native Official Output Text File workflow (RMO-A2..A7), a loopback-only read-only web console, an Admin-only `OfficialOutput.Run` permission, a generic-audit trail, and a fully implemented **official checkout persistence spine** that allocates an official document number, persists the sale/items/payments/stock, and appends an ORIGINAL electronic-journal event inside one shared SQLite transaction with commit/rollback together. What is **implemented but not sealed as a claim** and **not reachable from any UI screen**: reprint, void/refund/cancel/correction journal events (schema + triggers + services + tests), X-reading and Z-reading foundations (`bir_z_readings` + services), a text-preview renderer, and output packaging. What should happen next is a **current-state reconciliation seal** (DATA-A1 / RMO-B1) that brings the compliance matrix and seals into agreement with the source for the official-checkout spine — not new behavior. What should **not** happen next: no LAN, no printer/PDF/Crystal/ESC-POS/scanner, no Web/API official-output, no certification/accreditation claim, no UI exposure of reprint/X/Z, and no promotion of `KIRCH-FREEHAND` to Sandbox.

## Current Implemented Controls

### Web / API / native host
- Local API binds **loopback only** to `http://127.0.0.1:5299` (`src/Salryn.Api.Local/Program.cs`).
- API surface is **read-only**: every mapped operator endpoint is `MapGet` (products, inventory, sales history, users, audit) plus auth/session/dashboard (`src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`). No official-output endpoint exists.
- Web navigation keeps `Official Output Text File` (and POS Checkout, Settings, Backup) `enabled: false`; only read surfaces are enabled (`src/Salryn.App.Web/src/navigation.ts`).
- WebView host restricts navigation to `127.0.0.1`/`localhost` ports `5173`/`5299`, blocks `https`/`file`/`data`/`javascript` and LAN addresses (`src/Salryn.Desktop.WebView/LocalWebOriginPolicy.cs`).
- SEC-A1 smoke is in the CoreSmoke harness (`SecA1LocalApiWebViewSecurityChecks`).

### Official Output Text File workflow
- Native WinForms workflow only, gated at run entry (`src/Salryn.App.WinForms/Features/OfficialOutput/OfficialOutputTextFileWorkflowUiBoundary.cs`).
- Output root policy validates writable roots and rejects drive roots, system/Program Files/app-binary directories, missing roots, and file paths (`OfficialTextFileOutputPathPolicy`).
- Duplicate package refusal: `official-output/{packageHash}` is not overwritten; conflict `OfficialTextFileOutput.OutputPackageAlreadyExists` (`OfficialTextFileExternalOutputAdapter.cs`).
- Adapter writes plain UTF-8 text artifacts only: `manifest.txt`, `package.txt`, `ledger.txt`, `dispatch-plan.txt`, per-payload files, and an execution `receipt`. No PDF/printer/Crystal/ESC-POS.

### Permission / security
- `SalrynPermissions.OfficialOutputRun = "OfficialOutput.Run"` (`src/Salryn.Core/Security/SalrynPermissions.cs`).
- Seeded to **Admin only**; `ManagerPermissions` and `CashierPermissions` do not include it (`src/Salryn.Infrastructure/Data/Seeding/DefaultSeedData.cs`).

### Audit
- Official-output runs recorded through `OfficialOutputTextFileWorkflowAuditRecorder` to the generic `audit_logs` table (no dedicated schema). Sanitized `NewValueJson` only; audit-write failures are converted to controlled UI messages (`AuditRecordFailed`) without leaking SQLite/IO/path/stack text.

### Official checkout persistence (the data spine)
- `SqliteOfficialCheckoutPersistenceService.CompleteOfficialSaleAsync` runs a fail-closed readiness check, then inside one shared transaction: allocate official sequence → build snapshot draft from **trusted persisted** machine profile → build journal append contract → write sale/items/payments/stock → append ORIGINAL journal event (`src/Salryn.Infrastructure/Data/Services/SqliteOfficialCheckoutPersistenceService.cs`).
- Wired to live UI: `CheckoutPresenter` routes completion through `OfficialCheckoutCompletionBoundary` when official mode is selected; selection is only possible when readiness passes and the completion boundary is wired (`src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs`, `OfficialCheckoutCompletionBoundary.cs`, `CheckoutOfficialModeReadinessBoundary.cs`).
- Fail-closed and currently **dormant**: official mode requires an active `ACCREDITED` machine profile with `official_output_enabled = 1` plus exactly one active sequence; **no such profile is seeded**, so default installs use the standard (non-official) path.

### Electronic journal
- Tables: `bir_electronic_journal_entries`, `bir_electronic_journal_entry_events`, `bir_electronic_journal_preservation_batches` (`BirElectronicJournalSchemaMigration.cs`).
- ORIGINAL append implemented and smoke-backed (`SqliteElectronicJournalAppendParticipant.cs`, `SqliteElectronicJournalAppendAtomicPersistenceAdapter.cs`).
- Append-only hardening triggers (no UPDATE/DELETE), single-ORIGINAL-per-entry unique index, reversal-reason-required and preservation-batch-required triggers (`BirElectronicJournalSchemaHardeningMigration.cs`, `BirElectronicJournalCorrectionEventSchemaMigration.cs`).

### Transaction / rollback
- `SqliteSharedTransactionBoundary` opens one connection + transaction; commits on success, rolls back on failure Result or thrown exception (`src/Salryn.Infrastructure/Data/Transactions/SqliteSharedTransactionBoundary.cs`).
- Sequence allocation uses optimistic concurrency (`UPDATE ... WHERE current_number = $currentNumber`, affected-rows check) and exhaustion handling (`SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs`).
- Runtime smoke proves allocation + append commit/rollback together (`J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks`, `...SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks`).

### Reports / readings (implemented foundation, not UI-wired)
- `bir_z_readings` table (`BirZReadingSchemaMigration.cs`); `SqliteOfficialZReadingService`; X-reading foundation services. Exposed via `SalrynAppServices` (`IOfficialZReadingService`, X-reading) but **not invoked by any UI form**. Smoke-backed via `OfficialXReadingSmokeChecks` / `OfficialZReadingSmokeChecks` / `J21Rmo242023OfficialZReadingFoundationChecks`.

### Reprint / correction events (implemented foundation, not UI-wired)
- `SqliteOfficialDocumentReprintService`, `SqliteOfficialDocumentAuditEventService` (VOID/REFUND/CANCEL/CORRECTION), `SqliteOfficialDocumentLifecycleReadService`. Composed in `SalrynAppServices`, exercised by tests/smokes, **not reachable from a UI screen**.

### Backup / retention
- `SqliteBackupRecoveryService` exists and is wired into backup UI (PosStab-B5). Preservation/export journal events have schema + triggers but **no implemented export/preservation workflow**.

## Current Gaps

- **External / legal / BIR review** — none performed; explicit non-claim in the A7 seal.
- **Compliance matrix drift** — `00-COMPLIANCE/RMO_24_2023_COMPLIANCE_MATRIX.md` still states "no checkout wiring", "no X/Z reading", "no reprint", "no renderer" for many J21 controls, which the current source contradicts. This is the single biggest documentation risk.
- **Official checkout sealing** — the spine is implemented, wired (fail-closed), and smoke-backed, but has **no current-state reconciliation seal** equivalent to the A7 seal. Its status is scattered across J21-5x sections that read as "planning/foundation only".
- **X/Z readings** — foundation/service + Z table exist; no UI, no finalized end-of-day workflow, no claim. Missing before claiming readiness: deterministic report binding to live sales + VAT buckets, finalize/preserve workflow, and UI.
- **Reprint / void / refund / cancel journal events** — schema + services + tests exist; no UI, no authorization workflow, no claim.
- **Tax / VAT bucket engine** — only draft contracts + validation (`OfficialDocumentTaxBucketDraft`); checkout uses `VAT_BUCKET_ENGINE_DEFERRED`. No calculator engine.
- **Backend reports** — no formal report package beyond read repositories and the reading foundations.
- **Export / transmission** — preservation/export journal event types exist in schema only; no export layer.
- **LAN / multi-terminal** — none; explicitly out of scope.
- **Printer / PDF / Crystal / ESC-POS / scanner** — none in any official workflow; text-file only.

## Repo Evidence

| Claim | File | Symbol | Proves | Does not prove |
|---|---|---|---|---|
| Branches aligned at stable SHA | git | HEAD/refs | All three refs `1d50915` | n/a |
| Official output is native-only, text-only, gated | `OfficialTextFileExternalOutputAdapter.cs` | `ExecuteAsync` | Writes manifest/package/ledger/dispatch/payload/receipt `.txt` only | Not a certified receipt; no printer |
| A7 internal seal scope | `00-COMPLIANCE/RMO_A7_...SEAL.md` | whole doc | A2–A7 controls sealed as internal engineering only | Not BIR cert/accreditation/legal |
| Official permission Admin-only | `DefaultSeedData.cs` | `AdminPermissions`/`ManagerPermissions`/`CashierPermissions` | `OfficialOutput.Run` only in Admin | n/a |
| API loopback + read-only | `Program.cs`, `ReadOnlyEndpointExtensions.cs` | `UseUrls`, `MapGet` | 127.0.0.1:5299, GET-only operator surface | n/a |
| Web official-output disabled | `navigation.ts` | `navItems` | `enabled: false` | n/a |
| WebView origin lockdown | `LocalWebOriginPolicy.cs` | `IsAllowedLocalUri` | loopback ports 5173/5299 only | n/a |
| Official checkout spine atomic | `SqliteOfficialCheckoutPersistenceService.cs` | `CompleteOfficialSaleInTransactionAsync` | sequence+sale+journal in one transaction | Not certified; dormant without accredited profile |
| Shared transaction commit/rollback | `SqliteSharedTransactionBoundary.cs` | `ExecuteAsync`/`TryRollback` | rollback on failure/throw | n/a |
| Sequence concurrency + exhaustion | `SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs` | `AdvanceSequence` | optimistic guard + affected-rows check | Not multi-process/LAN-safe by design intent |
| Checkout wired fail-closed | `CheckoutPresenter.cs`, `OfficialCheckoutCompletionBoundary.cs`, `CheckoutOfficialModeReadinessBoundary.cs` | `SelectOfficialMode`, `CompleteSaleAsync` | official path runs only when readiness passes | Not active by default (no accredited seed) |
| Journal ORIGINAL append + markers | `SqliteElectronicJournalAppendParticipant.cs` | `InsertOriginalEvent` | ORIGINAL event with reprint/void/refund/cancel marker columns set 0 | Reprint/void/etc. not appended here |
| Append-only + correction schema | `BirElectronicJournalSchemaHardeningMigration.cs`, `BirElectronicJournalCorrectionEventSchemaMigration.cs` | triggers, CHECK | append-only, reason-required, single ORIGINAL | Behavior not UI-exposed |
| Reprint/audit-event/Z-reading composed not UI-wired | `SalrynAppServices.cs` (only WinForms reference) | service properties | services constructed/exposed | Reachable from a UI screen (it is not) |
| All 7 migrations registered | `DefaultDatabaseInitializationFactory.cs` | `migrations[]` | versions 1–7 active incl. Z-reading + correction | n/a |
| Matrix drift | `RMO_24_2023_COMPLIANCE_MATRIX.md` | J21-4/5/6/9 sections | doc says no checkout/X-Z/reprint | contradicted by source above |

## Risk Analysis

- **Overengineering risk — HIGH (already realized).** Reprint, correction events, X/Z foundations, renderers, and output packaging are implemented ahead of any sealed need and ahead of UI. This widens attack/maintenance surface and worsens doc drift. Next step should consolidate/seal, not extend.
- **LAN risk — HIGH if entered now.** Sequence allocation relies on single-process optimistic concurrency; the readiness service uses a separate connection from the allocation transaction (acceptable single-machine, unsafe across terminals). LAN would require terminal identity, clock/order policy, and cross-process sequence ownership.
- **Compliance overclaim risk — MEDIUM.** Source capability could tempt a premature "RMO-ready" claim. Seals correctly disclaim certification; keep it that way.
- **Stale documentation risk — HIGH.** The compliance matrix is the authoritative-looking doc and is wrong about checkout/X-Z/reprint. A reconciliation seal must explicitly supersede those lines.
- **Official-output vs official-checkout terminology risk — MEDIUM.** "Official Output Text File workflow" (export of an already-produced package) is a different subsystem from "Official Checkout persistence" (transactional sale + sequence + journal). They are easy to conflate; the report and diagrams keep them separate.
- **Raw exception / path leakage risk — LOW.** Adapters and audit failures map exceptions to controlled errors; path policy avoids leaking full roots.
- **Transaction / rollback risk — LOW (single machine).** Boundary commits/rolls back together; readiness is checked before any row is touched.
- **Future concurrency risk — MEDIUM.** Optimistic single-row guard is correct for one process but not a substitute for LAN-grade sequence ownership.

## Recommended Next Step

**DATA-A1 / RMO-B1 official checkout data-integrity reconciliation seal.**

Reason: the highest-value, lowest-risk action is to make the documentation tell the truth about what already exists and is smoke-backed. The official-checkout spine is implemented, wired fail-closed, and rollback-protected, but it has no current-state seal and the compliance matrix actively contradicts it. A documentation-and-guard reconciliation seal (like the A7 seal) closes the drift, records the exact non-claims, and establishes the single-machine invariants that must hold **before** any LAN work — without adding behavior. This is explicitly **not** LAN, not UI exposure of reprint/X/Z, and not a certification claim.

## Stop Gate

No implementation should begin until this report and both diagrams are reviewed and verified by a human/assistant reviewer. This study added no runtime code, schema, migration, or behavior.

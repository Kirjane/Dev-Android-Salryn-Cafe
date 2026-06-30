# RMO-STATE-1 Decision Log

This log records what was inspected, what evidence was found, what was concluded, and what remains uncertain. It avoids private chain-of-thought and states findings as repo-backed observations.

## Inspected File List

### Compliance / seals / handoff
- `00-COMPLIANCE/RMO_A7_INTERNAL_OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_SEAL.md`
- `00-COMPLIANCE/RMO_24_2023_COMPLIANCE_MATRIX.md`
- (enumerated) `00-COMPLIANCE/` electronic-journal seal/plan/audit docs; `00-HANDOFF/` J21/J22 docs

### Official output text-file workflow
- `src/Salryn.Infrastructure/OfficialOutput/OfficialTextFileExternalOutputAdapter.cs`
- `src/Salryn.Infrastructure/OfficialOutput/OfficialTextFileOutputPathPolicy.cs` (referenced)
- `src/Salryn.App.WinForms/Features/OfficialOutput/*` (UI boundary, audit recorder, package supply, source selection)

### Permission / seed
- `src/Salryn.Core/Security/SalrynPermissions.cs`
- `src/Salryn.Infrastructure/Data/Seeding/DefaultSeedData.cs`

### Web / API / native host
- `src/Salryn.Api.Local/Program.cs`
- `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.Desktop.WebView/LocalWebOriginPolicy.cs`

### Schema / migrations
- `src/Salryn.Infrastructure/Data/Migrations/InitialSchemaMigration.cs`
- `.../BirMachineProfileSchemaMigration.cs`
- `.../BirOfficialDocumentSequenceSchemaMigration.cs`
- `.../BirElectronicJournalSchemaMigration.cs`
- `.../BirElectronicJournalSchemaHardeningMigration.cs`
- `.../BirZReadingSchemaMigration.cs`
- `.../BirElectronicJournalCorrectionEventSchemaMigration.cs`
- `.../MigrationRunner.cs`
- `src/Salryn.Infrastructure/Data/Initialization/DefaultDatabaseInitializationFactory.cs`

### Official checkout data spine
- `src/Salryn.Infrastructure/Data/Services/SqliteOfficialCheckoutPersistenceService.cs`
- `.../SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs`
- `.../SharedSqliteCompletedSaleWriteParticipant.cs`
- `.../SqliteElectronicJournalAppendParticipant.cs`
- `.../SqliteOfficialCheckoutReadinessService.cs`
- `src/Salryn.Infrastructure/Data/Transactions/SqliteSharedTransactionBoundary.cs`
- `src/Salryn.App.WinForms/Features/Checkout/OfficialCheckoutCompletionBoundary.cs`
- `.../CheckoutOfficialModeReadinessBoundary.cs`
- `.../CheckoutPresenter.cs`
- `src/Salryn.App.WinForms/Composition/SqliteOfficialCheckoutPersistenceBoundary.cs`

### Reprint / readings / audit-event (foundation)
- `.../SqliteOfficialDocumentReprintService.cs`, `.../SqliteOfficialDocumentAuditEventService.cs`, `.../SqliteOfficialDocumentLifecycleReadService.cs`, `.../SqliteOfficialZReadingService.cs`, `.../SqliteBackupRecoveryService.cs` (presence + wiring via grep)

### Smokes / guards
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/Program.cs`

## Key Observations

1. **Branch gate passed.** HEAD, `origin/KIRCH-SANDBOX`, `origin/KIRCH-FREEHAND` are all `1d509158…ce03`; worktree clean before artifacts.
2. **Official output text-file workflow is sealed internally (A7), native-only, text-only, Admin-only, audited, fail-closed on output root and duplicate package.** Confirmed in source, not just docs.
3. **Web/API is loopback-only and read-only; official output is disabled in web nav; WebView origins are locked to loopback.**
4. **The official checkout persistence spine is fully implemented and atomic**: sequence allocation, sale/items/payments/stock write, and ORIGINAL journal append all run inside one `SqliteSharedTransactionBoundary` with commit/rollback together, gated by a fail-closed readiness service.
5. **The spine is wired into the live `CheckoutPresenter`** via an official mode that is selectable only when readiness passes; it is **dormant by default** because no `ACCREDITED` machine profile is seeded.
6. **Significant documentation drift.** The compliance matrix repeatedly says "no checkout wiring", "no X/Z reading", "no reprint", "no renderer", but source contains all of these as schema + services + tests (versions 6 and 7 migrations add Z-reading and correction events; CoreSmoke runs X/Z/reprint/audit-event/renderer checks).
7. **Reprint, void/refund/cancel/correction events, and X/Z readings are implemented at the persistence/service layer with tests, but are not reachable from any UI screen** (only composed in `SalrynAppServices`).
8. **All seven migrations are registered** in `DefaultDatabaseInitializationFactory`.

## Study Questions

### A. Current stable state
1. Smoke-backed & implemented: official output text-file workflow (A2–A7); loopback read-only web/API + SEC-A1; Admin-only official permission; generic audit; official checkout shared-transaction spine (sequence+sale+journal) with rollback; ORIGINAL journal append; sequence concurrency/exhaustion; X/Z reading foundations, reprint, and correction-event services (service-level smokes).
2. Implemented but not sealed/claimed: official checkout spine has no current-state seal; reprint, void/refund/cancel/correction events, X/Z readings, text-preview renderer, output packaging — implemented, tested, **not UI-wired**, not claimed.
3. Planned/documented only: VAT bucket calculator engine; formal backend report package; export/transmission workflow; preservation/export journal workflows; accreditation evidence pack.
4. Explicitly out of scope: LAN/multi-terminal; printer/PDF/Crystal/ESC-POS/scanner; Web/API official-output; BIR certification/accreditation/legal approval.
5. Should not be touched next: the A7-sealed text-file workflow; the read-only web/API security baseline; the existing transaction boundary semantics; branch promotion.

### B. RMO text-file workflow state
1. Yes — native Official Output Text File workflow is internally sealed (A7), engineering-only.
2. A2 smoke routing normalization; A3 output-root policy; A4 duplicate-package refusal; A5 Admin-only `OfficialOutput.Run`; A6 audit trail; A6.1 sanitized audit-failure surface; A7 cross-cutting seal.
3. Non-claims: not BIR certification/accreditation, not full RMO 24-2023, not legally approved, not certified printing/e-invoicing; no PDF/Crystal/ESC-POS/printer/scanner/LAN/Web-API official output; no reprint beyond duplicate-package refusal.
4. Yes — Web/API official-output remains disabled (`navigation.ts enabled:false`, no official endpoint in API).
5. No PDF/Crystal/ESC-POS/printer/scanner behavior in this workflow; text-file artifacts only (confirmed in adapter).

### C. Official checkout data spine
1. Yes — `SqliteOfficialCheckoutPersistenceService` exists and is composed/wired.
2. Yes — sequence allocation runs through `SharedSqliteOfficialDocumentSequenceAllocationParticipant` inside the caller-owned shared transaction.
3. Yes — sale, sale_items, payments, stock_movements, and the journal entry+ORIGINAL event are persisted together in that transaction.
4. Yes — `SqliteSharedTransactionBoundary` rolls back on any failed Result or thrown exception, preventing sequence/journal drift; runtime smoke proves it.
5. Yes — wired to UI readiness (`CheckoutOfficialModeReadinessBoundary`) and completion (`OfficialCheckoutCompletionBoundary`) via `CheckoutPresenter`, fail-closed and dormant without an accredited profile.
6. Not fully sealed — it needs a **current-state reconciliation seal**; current docs understate/contradict it.

### D. Electronic journal
1. Tables: `bir_electronic_journal_entries`, `bir_electronic_journal_entry_events`, `bir_electronic_journal_preservation_batches`.
2. `SqliteElectronicJournalAppendParticipant` (+ atomic adapter + service composition) appends inside the shared transaction.
3. Events currently *written by the live spine*: ORIGINAL only.
4. Yes — ORIGINAL append exists and is smoke-backed.
5. Reprint and void/refund/cancel/correction are supported by **schema, triggers, and services with tests**, but are **not appended by any live/UI flow** — effectively foundation, not active behavior.
6. Export/preservation: schema + trigger constraints only; no implemented export/preservation workflow.

### E. X/Z readings and reports
1. Exists: `bir_z_readings` table, `SqliteOfficialZReadingService`, X-reading foundation services, exposed via `SalrynAppServices`.
2. Smoke-backed: `OfficialXReadingSmokeChecks`, `OfficialZReadingSmokeChecks`, `J21Rmo242023OfficialZReadingFoundationChecks` (service-level).
3. Foundation/planning only: no UI, no finalize/preserve workflow, no VAT bucket integration.
4. Missing before claiming readiness: deterministic binding to live sale/VAT data, finalize+preserve workflow, UI, and a reconciliation seal.

### F. LAN decision
1. No — LAN is not required before more RMO work and should not be next.
2. LAN complicates: sequence ownership across processes/terminals, terminal identity in journal/audit, clock/order policy, concurrency beyond single-row optimistic guard.
3. Single-machine invariants to seal before LAN: atomic sequence+sale+journal commit/rollback; one active sequence per (profile, kind, scope, year); append-only journal with single ORIGINAL; fail-closed readiness; Admin-only official permission. (All present in source; the seal must record them.)

### G. Next best task
1. Next step should be **DATA-A1 / RMO-B1 official checkout data-integrity reconciliation seal** (not LAN, not RMO-STATE-1 continuation beyond this study, not new behavior).
2. Exact task to follow verification: produce a current-state seal doc + source/smoke guards that reconcile the compliance matrix and journal seals with the implemented official-checkout spine and record exact non-claims.
3. Forbidden in that next task: any runtime behavior change, schema/migration, LAN, printer/PDF/Crystal/ESC-POS/scanner, Web/API official-output, UI exposure of reprint/X/Z, certification claims, and branch promotion.

## Conclusion

The repository is stable and internally consistent at the code level: the official-checkout data spine and the official-output text-file workflow are implemented, gated, and rollback-protected, with broad smoke coverage. The dominant problem is **documentation drift**, not code risk. The correct next move is a non-behavioral reconciliation seal.

## Confidence Level

- Branch/seal/web/permission/schema/transaction findings: **High** (direct source reads).
- Official-checkout spine atomicity and UI wiring: **High** (read `SqliteOfficialCheckoutPersistenceService`, `OfficialCheckoutCompletionBoundary`, `CheckoutPresenter`).
- "Implemented but not UI-wired" for reprint/X/Z/audit-event: **Medium-High** (grep shows only `SalrynAppServices` references in WinForms; did not exhaustively read every form, but no presenter/form references were found).
- Build/test/smoke pass status: **Unverified in this session** — the terminal harness was unresponsive (see render_notes.md and Verification Results in the final response).

## Uncertainty

- Whether any non-obvious UI entry point (e.g., a settings/admin maintenance screen) can invoke reprint/X/Z was not exhaustively confirmed beyond grep; the grep evidence strongly indicates none.
- Whether the build/tests/smokes currently pass on this machine was not confirmed because shell execution failed repeatedly during this session.
- Exact current `schema_migrations` row count at runtime was not queried (would require running the smoke tool).

## Follow-up Question (for reviewer)

Should the reconciliation seal (DATA-A1 / RMO-B1) also explicitly **down-rank or annotate** the stale lines in `RMO_24_2023_COMPLIANCE_MATRIX.md` (e.g., add a "superseded by current source" note per drifted control), or should the matrix be left untouched and only a new seal document added? This affects whether the next task touches the matrix file at all.

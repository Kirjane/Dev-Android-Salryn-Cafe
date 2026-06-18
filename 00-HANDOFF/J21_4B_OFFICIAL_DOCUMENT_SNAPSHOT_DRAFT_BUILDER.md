# J21-4B - Official Document Snapshot Draft Builder

Status: builder only.

## Scope

J21-4B adds an in-memory OfficialDocumentSnapshotDraftBuilder.

The builder maps already-provided machine profile, sale, item, payment, and optional allocated document number data into OfficialDocumentSnapshotDraft.

## RMO Controls

- C03 partial: maps required header field shape from explicit input
- C04 partial: maps item/payment/amount shape from existing sale data
- C05 deferred: tax-mode notes only, no VAT bucket engine

## Added

- OfficialDocumentSnapshotDraftMachineProfileInput
- OfficialDocumentSnapshotDraftBuildRequest
- OfficialDocumentSnapshotDraftBuilder
- J21-4B source/compile guard checks
- CoreSmoke registration
- Compliance matrix J21-4B section

## Not Added

- No renderer.
- No printer.
- No checkout wiring.
- No saved sale receipt_snapshot writes.
- No DB/schema migration.
- No database reads.
- No sequence allocation.
- No electronic journal.
- No X/Z reading.
- No VAT bucket engine yet.
- No claim that Salryn can render official receipt/invoice output.

## Guardrail

The builder may create an in-memory draft object only.

It must not render, persist, print, expose, or label the draft as final official receipt/invoice output.

## Next

J21-4C must be confirmed before proceeding.

Safe options include:

- add more validation to the builder
- add a non-official inspector/debug-only view model
- start C05 VAT/non-VAT bucket modeling
- pause compliance spine and return to UI polish

Do not wire this builder into checkout until explicitly approved.
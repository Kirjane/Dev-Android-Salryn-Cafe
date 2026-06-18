# J21-4A - Official Document Snapshot Draft Contracts

Status: contracts only.

## Scope

J21-4A adds neutral data contracts named OfficialDocumentSnapshotDraft.

The term is intentional. It describes a future official-document data shape without implying Salryn can already render, persist, print, or expose official receipt/invoice output.

## RMO Controls

- C03 partial: required header field shape only
- C04 partial: item/payment/amount shape only
- C05 deferred: placeholder/tax-mode notes only

## Added

- OfficialDocumentSnapshotDraft
- OfficialDocumentSnapshotDraftKind
- OfficialDocumentSnapshotDraftHeader
- OfficialDocumentSnapshotDraftLine
- OfficialDocumentSnapshotDraftAmounts
- OfficialDocumentSnapshotDraftPayment
- OfficialDocumentSnapshotDraftTaxMode
- J21-4A source/compile guard checks
- CoreSmoke registration
- Compliance matrix J21-4A section

## Not Added

- No renderer.
- No printer.
- No checkout wiring.
- No saved sale receipt_snapshot writes.
- No DB/schema migration.
- No sequence allocation.
- No electronic journal.
- No X/Z reading.
- No VAT bucket engine yet.
- No claim that Salryn can render official receipt/invoice output.

## Guardrail

The contract may describe future official-document fields, but no code should render, persist, print, or expose it as official output yet.

## Next

J21-4B may add a builder that maps sale data, machine profile data, and allocated document number data into OfficialDocumentSnapshotDraft.

J21-4B must still avoid rendering, printing, persisting sale receipt_snapshot, and checkout wiring unless explicitly approved.
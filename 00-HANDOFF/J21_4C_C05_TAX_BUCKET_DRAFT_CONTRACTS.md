# J21-4C - C05 Tax Bucket Draft Contracts

Status: contracts only.

## Scope

J21-4C adds neutral VAT/non-VAT/exempt/zero-rated tax bucket contracts only.

This phase models C05 bucket shape without adding a calculator engine or any official output behavior.

## RMO Controls

- C05 partial only: VAT/non-VAT/exempt/zero-rated bucket shape

## Added

- OfficialDocumentTaxBucketDraft
- OfficialDocumentTaxBucketDraftTaxpayerMode
- OfficialDocumentTaxBucketDraftKind
- OfficialDocumentTaxBucketDraftBucket
- J21-4C source/compile guard checks
- CoreSmoke registration
- Compliance matrix J21-4C section

## Not Added

- No calculator engine.
- No renderer.
- No printer.
- No checkout wiring.
- No saved sale receipt_snapshot writes.
- No DB/schema migration.
- No sequence allocation.
- No electronic journal.
- No X/Z reading.
- No official output claim.

## Guardrail

These contracts may describe VAT/non-VAT/exempt/zero-rated bucket fields, but no code should calculate, render, persist, print, expose, or label the data as final official receipt/invoice output yet.

## Next

J21-4D must be confirmed before proceeding.

Safe options include:

- strengthen C05 contract validation only
- add a separate tax bucket calculator engine later
- add non-official debug-only inspection contracts
- pause compliance spine and return to POS UI polish

Do not wire this into checkout until explicitly approved.
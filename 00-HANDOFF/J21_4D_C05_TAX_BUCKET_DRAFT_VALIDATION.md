# J21-4D - C05 Tax Bucket Draft Validation

Status: validation only.

## Scope

J21-4D strengthens validation around OfficialDocumentTaxBucketDraft only.

This phase rejects structurally unsafe C05 draft shapes without adding a tax calculator engine or any official output behavior.

## RMO Controls

- C05 partial only: structural validation for VAT/non-VAT/exempt/zero-rated bucket draft shape

## Added

- OfficialDocumentTaxBucketDraftValidator
- J21-4D source/compile guard checks
- CoreSmoke registration
- Compliance matrix J21-4D section

## Validation Coverage

- validates taxpayer mode is known
- validates bucket list is present
- validates bucket kinds are valid
- validates bucket amounts are non-negative
- validates duplicate bucket rules
- validates deferred notes preserve no-engine/no-output boundary
- validates non-VAT draft has non-VAT statement placeholder
- validates VAT draft does not carry non-VAT statement placeholder

## Not Added

- No tax calculator engine.
- No VAT computation rules.
- No checkout wiring.
- No renderer.
- No receipt renderer.
- No printer.
- No saved sale receipt_snapshot writes.
- No DB/schema migration.
- No sequence allocation.
- No electronic journal.
- No X/Z reading.
- No official output claim.

## Guardrail

This validator may reject bad C05 draft shapes, but it must not compute VAT, infer legal tax treatment, render official text, persist anything, or expose final official receipt/invoice output.

## Next

Next phase must be confirmed before proceeding.

Safe options include:

- add a C05 calculator engine later, still with no output
- add more validation around C03/C04 draft builder
- start electronic journal/audit journal planning under J21-5
- pause compliance spine and return to POS UI polish

Do not wire this into checkout until explicitly approved.
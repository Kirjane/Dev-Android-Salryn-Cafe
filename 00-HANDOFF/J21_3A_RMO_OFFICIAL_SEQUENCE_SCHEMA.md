# J21-3A - RMO Official Receipt/Invoice Sequence Schema Foundation

Status: schema foundation only.

## Scope

This commit adds the database foundation for RMO-24-2023-C02 official receipt/invoice sequence ownership.

## RMO Control

- RMO-24-2023-C02 Official receipt/invoice sequence

## Added

- New migration: BirOfficialDocumentSequenceSchemaMigration
- New table: bir_official_document_sequences
- Migration registration in DefaultDatabaseInitializationFactory
- Source/runtime guard checks for the new sequence schema
- CoreSmoke registration for the J21-3A checks
- Compliance matrix J21-3A section

## Sequence Ownership

Official document sequences are tied to bir_machine_profiles through bir_machine_profile_id.

This keeps official numbering owned by a machine/profile boundary instead of a UI form, store setting only, or random runtime value.

## Guardrails

The schema stores:

- document_kind
- sequence_scope_code
- prefix
- year
- start_number
- current_number
- end_number
- padding_digits
- is_active
- is_exhausted

The schema enforces:

- minimum six-digit padding
- start/current/end number consistency
- active scope uniqueness per machine profile, document kind, scope, prefix, and year
- exhausted rows cannot remain active

## Not Added

- No official receipt/invoice number allocation yet.
- No UI.
- No receipt printing.
- No receipt snapshot generation.
- No checkout flow wiring.
- No electronic journal.
- No X-reading/Z-reading.
- No multi-tenant work.
- No accreditation readiness claim.

## Next

J21-3B should add the official sequence allocation service.

J21-3B must:

- advance current_number transactionally
- block allocation without an active machine profile
- block allocation without an active official sequence
- prevent sequence exhaustion overflow
- return deterministic padded values
- not use GUID/random receipt numbers for official output
- not print or render official receipt/invoice output yet
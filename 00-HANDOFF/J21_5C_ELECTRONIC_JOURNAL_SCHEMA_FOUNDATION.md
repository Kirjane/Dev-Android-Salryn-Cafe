# J21-5C - Electronic Journal Schema Foundation

Status: schema foundation only.

## Scope

J21-5C adds DB schema/table structure for future electronic journal storage.

This phase creates tables and indexes only.

## RMO Controls

- C08 partial: electronic journal and audit journal schema foundation
- C07 partial: append-only/event integrity schema foundation
- C12 partial: retention/preservation schema foundation

## Added

- BirElectronicJournalSchemaMigration
- bir_electronic_journal_entries
- bir_electronic_journal_entry_events
- bir_electronic_journal_preservation_batches
- J21Rmo242023ElectronicJournalSchemaFoundationChecks
- CoreSmoke registration
- Compliance matrix J21-5C section
- DefaultDatabaseInitializationFactory migration registration

## Schema Boundaries

bir_electronic_journal_entries is the future journal anchor table.

bir_electronic_journal_entry_events stores linked lifecycle events such as original, reprint, void, refund, cancel, export, preservation, and backup/recovery markers.

bir_electronic_journal_preservation_batches stores future export/preservation batch metadata.

## Relationship Boundaries

sales.receipt_snapshot remains current MVP placeholder, not RMO journal.

audit_logs remains generic audit trail, not RMO electronic journal.

print_jobs remains printer queue boundary, not journal evidence.

official document draft/builder remains in-memory until future approved persistence phase.

sequence allocation remains separate from journal append until a later atomic design is confirmed.

## Not Added

- No checkout wiring.
- No receipt_snapshot writes.
- No official document persistence from checkout.
- No renderer.
- No printer.
- No sequence allocation changes.
- No tax calculator engine.
- No X/Z reading.
- No actual journal append service.
- No UI.
- No official receipt/invoice output claim.

## Guardrail

J21-5C may add schema, but it must not make Salryn write journal rows from live checkout yet.

Schema is allowed.

Behavior is not allowed yet.

No official output. No persistence path. No checkout path. No renderer. No printer.

## Next

J21-5D must be explicitly confirmed before proceeding.

Possible next directions:

- journal append contract planning only
- schema validation hardening only
- C03/C04/C05 validation strengthening
- return to POS UI polish
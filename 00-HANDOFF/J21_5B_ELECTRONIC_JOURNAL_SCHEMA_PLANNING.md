# J21-5B - Electronic Journal Schema Planning

Status: planning and guardrails only.

## Scope

J21-5B plans future electronic journal and audit journal schema shape.

This phase is documentation plus source/guard checks only.

## RMO Controls

- C08 planning only: future electronic journal and audit journal schema shape
- C07 partial: append-only/event integrity planning
- C12 partial: retention/preservation planning

## Added

- RMO_24_2023_ELECTRONIC_JOURNAL_SCHEMA_PLAN.md
- J21Rmo242023ElectronicJournalSchemaPlanningChecks
- CoreSmoke registration
- Compliance matrix J21-5B section

## Future Table Candidates Only

- future electronic_journal_entries
- future electronic_journal_entry_events
- future electronic_journal_exports or preservation batches

These are table candidates only and are not created in J21-5B.

## Future Field Ownership Only

A later implementation must decide ownership for:

- journal entry id
- machine profile id
- sale id
- official document sequence id
- official document number
- document kind
- transaction timestamp
- cashier/user id
- terminal id
- snapshot payload/hash
- event type
- original/reprint/void/refund/cancel markers
- created_at
- preserved/exported metadata

## Relationship Boundaries

sales.receipt_snapshot remains current MVP placeholder, not RMO journal.

audit_logs remains generic audit trail, not RMO electronic journal.

print_jobs remains printer queue boundary, not journal evidence.

official document draft/builder remains in-memory until future approved persistence phase.

sequence allocation must stay separate from journal append until a later atomic design is confirmed.

## Not Added

- No DB/schema migration.
- No journal tables.
- No checkout wiring.
- No receipt_snapshot writes.
- No official document persistence.
- No renderer.
- No printer.
- No sequence allocation changes.
- No tax calculator engine.
- No X/Z reading.
- No official receipt/invoice output claim.

## Guardrail

J21-5B may plan table candidates, field ownership, append-only rules, indexes, and relationship boundaries only.

It must not create tables, change migrations, persist official documents, write checkout paths, render official output, print output, allocate sequences, create X/Z readings, or claim official output readiness.

## Next

J21-5C must be explicitly confirmed before proceeding.

Possible next directions:

- J21-5C schema foundation
- strengthen C03/C04/C05 validations
- return to POS UI polish
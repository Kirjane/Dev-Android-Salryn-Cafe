# RMO 24-2023 Electronic Journal Schema Plan - Salryn

Status: planning and guardrails only.

## Purpose

J21-5B plans the future electronic journal and audit journal schema shape before any database migration, journal table, checkout wiring, official document persistence, renderer, printer, X/Z reading, or official output claim exists.

This document is not a schema implementation.

## RMO Controls

- C08 planning only: future electronic journal and audit journal schema shape
- C07 partial planning: append-only and event integrity planning
- C12 partial planning: retention and preservation planning

## Future Table Candidates Only

These names are future table candidates only. They are not created in J21-5B.

### future electronic_journal_entries

Future purpose:

- one append-only entry per official document lifecycle anchor
- owns official document identity references
- owns snapshot hash references after approved renderer/snapshot persistence exists
- does not replace sales
- does not replace print_jobs
- does not replace audit_logs

Candidate ownership fields:

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
- created_at
- preserved/exported metadata

### future electronic_journal_entry_events

Future purpose:

- append-only events linked to a journal entry
- records original/reprint/void/refund/cancel markers
- records operator/user identity and event timestamp
- records reason text where required
- supports transaction integrity review without editing the original journal entry

Candidate ownership fields:

- journal event id
- journal entry id
- event type
- original marker
- reprint marker
- void marker
- refund marker
- cancel marker
- cashier/user id
- reason or operator note
- created_at

### future electronic_journal_exports or preservation batches

Future purpose:

- records export or preservation batches after export design exists
- records preservation metadata without mutating original official journal evidence
- supports backup/recovery and retention review

Candidate ownership fields:

- export or preservation batch id
- export/preservation type
- date/time range
- journal entry range or count
- file name or storage reference later
- file hash later
- created_by_user_id
- created_at
- preserved/exported metadata

## Future Relationship Boundaries

sales.receipt_snapshot remains current MVP placeholder, not RMO journal.

audit_logs remains generic audit trail, not RMO electronic journal.

print_jobs remains printer queue boundary, not journal evidence.

official document draft/builder remains in-memory until future approved persistence phase.

sequence allocation must stay separate from journal append until a later atomic design is confirmed.

The future journal append design must be confirmed before sequence allocation, snapshot persistence, and journal entry creation are combined into one transaction.

## Future Append-Only Rules

Future journal records should be append-only in design.

Future correction paths should use linked events instead of editing original journal entries.

Future reprints should create linked reprint events.

Future void/cancel/refund actions should create linked reversal events.

Future export or preservation actions should create linked preservation evidence.

## Future Index Planning Only

Future index candidates may include:

- machine profile id and transaction timestamp
- official document number and document kind
- sale id
- cashier/user id
- event type and created_at
- export/preservation batch id

These are index candidates only, not schema.

## What Must Not Happen Yet

J21-5B must not add:

- No DB/schema migration
- No journal tables
- No checkout wiring
- No receipt_snapshot writes
- No official document persistence
- No renderer
- No printer
- No sequence allocation changes
- No tax calculator engine
- No X/Z reading
- No official receipt/invoice output claim

## J21-5B Guardrail

J21-5B is planning and guardrails only.

Do not implement schema migration, journal append services, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z reports, or official output until explicitly confirmed.
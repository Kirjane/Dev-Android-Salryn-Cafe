# J21-5E - Electronic Journal Schema Hardening Foundation

Status: DB hardening only.

## Scope

J21-5E adds a new forward-only version 5 migration for electronic journal schema hardening.

This phase adds database-level triggers and a partial unique index only.

## RMO Controls

- C08 partial: electronic journal immutability hardening
- C07 partial: append-only/event integrity hardening
- C12 partial: preservation/export event evidence hardening

## Added

- BirElectronicJournalSchemaHardeningMigration
- version 5 migration registration
- append-only UPDATE/DELETE blocking triggers
- reversal reason requirement trigger
- preservation batch requirement trigger
- single ORIGINAL event partial unique index
- J21Rmo242023ElectronicJournalSchemaHardeningChecks
- CoreSmoke registration
- Compliance matrix J21-5E section

## Hardening Added

UPDATE and DELETE are blocked on:

- bir_electronic_journal_entries
- bir_electronic_journal_entry_events
- bir_electronic_journal_preservation_batches

Reason is required for:

- VOID
- REFUND
- CANCEL

preservation_batch_id is required for:

- EXPORT
- PRESERVATION
- BACKUP_RECOVERY

One ORIGINAL event per journal_entry_id is enforced by:

- idx_bir_electronic_journal_entry_events_single_original

## Snapshot Boundary

J21-5E does not harden snapshot_payload_json, snapshot_payload_hash, or snapshot_hash_algorithm to NOT NULL.

Reason:

Renderer and official snapshot persistence do not exist yet.

Snapshot evidence must be enforced later by the append service and possibly a later DB trigger/state rule after renderer/snapshot persistence design exists.

## Not Added

- No append service.
- No checkout wiring.
- No journal repository/service behavior.
- No receipt_snapshot writes.
- No official document persistence from checkout.
- No renderer.
- No printer.
- No sequence allocation changes.
- No tax calculator engine.
- No X/Z reading.
- No UI.
- No official receipt/invoice output claim.
- No snapshot hash NOT NULL constraint.

## Guardrail

J21-5E is only DB hardening.

Behavior comes later.

Do not wire checkout.

Do not add append service.

Do not touch renderer or printer.

Do not alter the committed J21-5C version 4 migration.

## Next

J21-5F must be explicitly confirmed before proceeding.

Possible next directions:

- append service contract planning only
- atomic transaction boundary planning
- schema hardening repair only if validation finds an issue
- return to POS UI polish
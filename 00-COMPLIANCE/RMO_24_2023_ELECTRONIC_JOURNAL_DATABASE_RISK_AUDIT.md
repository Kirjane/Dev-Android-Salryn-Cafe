# RMO 24-2023 Electronic Journal Database Risk Audit - Salryn

Status: database risk audit only.

## Purpose

J21-5D audits the J21-5C electronic journal schema foundation before any version 5 hardening migration, trigger, append service, checkout wiring, renderer, printer, or official output work.

This document exists because version 4 has already been committed and may already be applied on development databases.

Do not rewrite the committed J21-5C version 4 migration.

Use a new forward migration for any future schema hardening.

## RMO Controls

- C08 risk audit: electronic journal evidence integrity
- C07 risk audit: append-only/event integrity and reversal event requirements
- C12 risk audit: retention, preservation, export, and backup/recovery evidence

## Scope

This phase is documentation plus source/guard checks only.

J21-5D does not add schema changes.

J21-5D does not add a DB migration.

J21-5D does not create triggers.

J21-5D does not create an append service.

J21-5D does not wire checkout.

J21-5D does not write sales.receipt_snapshot.

J21-5D does not persist official documents.

J21-5D does not render output.

J21-5D does not print output.

J21-5D does not change sequence allocation.

J21-5D does not add a tax engine.

J21-5D does not add X/Z reading.

J21-5D does not add UI.

J21-5D does not claim official output readiness.

## Risk 1 - Append-Only Enforcement

Current state:

- J21-5C comments say journal events are linked instead of editing the original entry.
- The schema has journal anchor and journal event tables.
- The database does not yet block UPDATE or DELETE on journal tables.

Risk:

If later code accidentally updates or deletes journal rows, electronic journal evidence can lose integrity.

Recommended split:

- DB level later: use a forward version 5 migration to add SQLite triggers that block UPDATE and DELETE on journal evidence tables.
- Service level later: append service should expose insert-only operations and should not expose update/delete methods for journal evidence.

Future decision:

Add append-only triggers in J21-5E only after explicit approval.

## Risk 2 - Reversal Reason Requirement

Current state:

- bir_electronic_journal_entry_events has reason TEXT NULL.
- VOID, REFUND, and CANCEL events can currently exist without a reason.

Risk:

Reversal events without a reason are weak audit evidence and can create review pain.

Recommended split:

- DB level later: use a trigger to reject VOID, REFUND, and CANCEL events when reason is NULL or blank.
- Service level later: append service must require operator reason before attempting insert.

Future decision:

Require reversal reasons in both DB triggers and service validation.

## Risk 3 - Preservation Batch Requirement

Current state:

- bir_electronic_journal_entry_events has preservation_batch_id TEXT NULL.
- EXPORT, PRESERVATION, and BACKUP_RECOVERY events can currently exist without a preservation batch.

Risk:

Preservation events without a linked batch weaken C12 export/preservation evidence.

Recommended split:

- DB level later: use a trigger to require preservation_batch_id for EXPORT, PRESERVATION, and BACKUP_RECOVERY events.
- Service level later: export/preservation service must create or reference a preservation batch before inserting linked events.

Future decision:

Require preservation batch link in both DB triggers and service validation.

## Risk 4 - Snapshot Payload/Hash Requirement

Current state:

- snapshot_payload_json, snapshot_payload_hash, and snapshot_hash_algorithm are nullable in J21-5C.
- Nullable is acceptable because no renderer or snapshot persistence exists yet.
- J21-5C already requires snapshot hash and hash algorithm to be paired when either is present.

Risk:

A future append service could create official journal entries without rendered evidence if it does not require snapshot/hash fields at the correct phase.

Recommended split:

- DB level later: defer hard NOT NULL constraints until official renderer/snapshot persistence exists and backfill/transition rules are explicit.
- Service level later: once official output exists, append service must require snapshot payload/hash evidence before official journal entry creation.

Future decision:

Do not harden snapshot fields yet. Harden by event/document readiness state after renderer and snapshot persistence are approved.

## Risk 5 - Atomicity

Current state:

- J21-5C stores journal anchor fields and event fields.
- Sequence allocation remains separate from journal append.
- No append service exists yet.

Risk:

If future sequence allocation, snapshot persistence, journal anchor insert, and ORIGINAL event insert are not one transaction, official numbers and journal evidence can drift.

Recommended split:

- DB level later: preserve foreign keys and unique document identity constraints.
- Service level later: one explicit transaction must contain:
  - official sequence allocation
  - snapshot hash/payload persistence
  - journal anchor insert
  - ORIGINAL event insert
  - sale official snapshot link only if separately approved

Future decision:

Do not design append service until the atomic transaction boundary is written and approved.

## Risk 6 - Boundary Protection

Current boundaries:

- sales.receipt_snapshot is not the RMO journal.
- audit_logs is not the RMO electronic journal.
- print_jobs is not journal evidence.
- official document draft/builder remains in-memory until an approved persistence phase.
- sequence allocation remains separate from journal append until a later atomic design is confirmed.

Risk:

Future shortcut code may try to treat MVP fields or generic audit/printer queues as compliance evidence.

Recommended split:

- DB level later: keep separate tables and avoid merging print queue or audit log data into journal identity.
- Service level later: future services must name boundaries explicitly and avoid reusing print_jobs or audit_logs as journal evidence.

Future decision:

Keep these boundaries as hard guardrails in every future C08/C07/C12 phase.

## J21-5E Recommendation

If approved later, J21-5E should be Electronic Journal Schema Hardening Foundation.

Possible J21-5E scope:

- new version 5 forward migration only
- append-only UPDATE/DELETE blocking triggers
- reason requirement trigger for VOID/REFUND/CANCEL
- preservation_batch_id requirement trigger for EXPORT/PRESERVATION/BACKUP_RECOVERY
- no checkout wiring
- no append service
- no renderer
- no printer
- no official output

## J21-5D Guardrail

Do not add triggers yet.

Do not add version 5 yet.

Do not add append service yet.

Do the risk audit first, then add version 5 hardening only after the DB/service split is explicitly approved.
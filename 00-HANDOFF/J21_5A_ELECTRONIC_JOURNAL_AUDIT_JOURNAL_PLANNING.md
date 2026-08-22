# J21-5A - Electronic Journal / Audit Journal Planning Guardrails

Status: planning and guardrails only.

## Scope

J21-5A defines the future electronic journal and audit journal boundary before any official document persistence or output work.

This phase is documentation plus source/guard checks only.

## RMO Controls

- C08 planning only: electronic journal and audit journal boundary
- C07 partial: transaction integrity, reprint, void, cancel, and refund guardrail planning
- C12 partial: preservation, retention, backup, and recovery guardrail planning

## Added

- RMO_24_2023_ELECTRONIC_JOURNAL_BOUNDARY.md
- J21Rmo242023ElectronicJournalPlanningChecks
- CoreSmoke registration
- Compliance matrix J21-5A section

## Future Electronic Journal Evidence

Future electronic journal evidence should cover:

- official document issuance
- rendered snapshot hash later
- reprint event later
- void/cancel/refund event later
- export/preservation event later
- backup/recovery evidence later where applicable

## Future Audit Journal Evidence

Future audit journal evidence should cover:

- cashier/user id
- action type
- affected entity id
- transaction timestamp
- reason/operator note where required
- before/after metadata where safe
- event source

## Future Required Capture Fields

A later implementation must decide how to capture:

- official document number
- document kind
- machine profile id
- sale id
- cashier/user id
- transaction timestamp
- rendered snapshot hash later
- reprint marker later
- void/cancel/refund event later
- export/preservation metadata later

## Not Added

- No DB/schema migration.
- No new journal tables.
- No checkout wiring.
- No receipt_snapshot writes.
- No official document persistence.
- No renderer.
- No printer.
- No sequence allocation changes.
- No tax calculator engine.
- No X/Z reading.
- No official receipt/invoice output claim.

## Existing Boundaries

Existing sales.receipt_snapshot, print_jobs, and audit_logs remain unchanged.

J21-5A does not write to sales.receipt_snapshot.

J21-5A does not use print_jobs as an electronic journal.

J21-5A does not upgrade audit_logs into an RMO electronic journal.

## Guardrail

J21-5A may describe future journal evidence requirements, but it must not create database tables, persist official documents, write checkout paths, render official output, print output, allocate sequences, create X/Z readings, or claim official output readiness.

## Next

J21-5B must be explicitly confirmed before proceeding.

Possible next directions:

- J21-5B schema planning only
- J21-5B electronic journal schema foundation
- strengthen C03/C04/C05 validations
- pause compliance spine and return to POS UI polish
# RMO 24-2023 Electronic Journal Boundary - Salryn

Status: planning and guardrails only.

## Purpose

This document defines the future electronic journal and audit journal boundary before Salryn persists official document snapshots, wires checkout, renders output, prints output, or claims official receipt/invoice readiness.

J21-5A is not an implementation phase.

## RMO Controls

- C08 planning only: electronic journal and audit journal boundary
- C07 partial planning: transaction integrity, reprint, void, cancel, and refund guardrails
- C12 partial planning: preservation, retention, backup, and recovery guardrails

## Future Electronic Journal Evidence

Future electronic journal evidence means append-only evidence connected to official document lifecycle events.

Future electronic journal evidence should include records for:

- official document issuance
- official document rendered snapshot hash after a renderer exists
- official document reprint event
- official document void event
- official document cancel event
- official document refund event
- export or preservation event
- backup or recovery evidence event where applicable

Electronic journal evidence is not a printer queue and is not a UI preview.

## Future Audit Journal Evidence

Future audit journal evidence means trace evidence for who performed actions and why.

Future audit journal evidence should include records for:

- user/cashier identity
- action type
- affected entity identity
- reason or operator note where required
- timestamp
- before/after metadata where safe
- event source such as checkout, reprint, void, refund, export, backup, or recovery

The existing audit_logs table is a generic audit boundary only. J21-5A does not change it.

## Future Required Capture Fields

A future journal implementation must decide how to capture:

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

These are future capture requirements only.

## Existing Schema Boundaries

Current schema already has:

- sales.receipt_snapshot field placeholder
- print_jobs table
- audit_logs table

J21-5A does not write to sales.receipt_snapshot.

J21-5A does not use print_jobs as an electronic journal.

J21-5A does not upgrade audit_logs into an RMO electronic journal.

## What Must Not Happen Yet

J21-5A must not add:

- DB/schema migration
- new journal tables
- checkout wiring
- receipt_snapshot writes
- official document persistence
- renderer
- printer
- sequence allocation changes
- tax calculator engine
- X/Z reading
- official receipt/invoice output claim

## Future Boundary Rules

Before any future official output or persistence work:

1. Official document rendering must have a deterministic snapshot boundary.
2. Snapshot persistence must be separate from print queue behavior.
3. Print queue behavior must not be treated as journal preservation.
4. Reprints must be separate events and must be marked.
5. Voids, cancels, and refunds must be separate traceable reversal events.
6. Journal entries must be append-only in design.
7. Journal export/preservation must not mutate original official evidence.
8. Production reset and development reset must remain separated.
9. Backup/recovery must preserve machine profile, official sequences, official document snapshots, and journal records.
10. Demo/training output must never be confused with official output.

## J21-5A Guardrail

J21-5A is planning and guardrails only.

Do not implement J21-5B schema, journal append services, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z reports, or official output until explicitly confirmed.
# RMO 24-2023 Compliance Matrix - Salryn

J21-1 status: guardrail only.

This file is the source-of-truth compliance map before implementing BIR/RMO-facing behavior.

This is not a certificate, accreditation approval, legal opinion, or production compliance claim.

## Scope Boundary

J21-1 adds documentation and source guards only.

No schema change.
No migration.
No app run.
No receipt/invoice production claim.
No multi-tenant work.
No stable QA branch changes.

## Current Salryn Baseline

Current MVP already has:

- local/offline Windows desktop POS shell
- first-run admin setup
- login
- products
- cashier shift gate
- POS checkout
- completed sale persistence
- stock reduction
- receipt snapshot field placeholder
- sale read models
- payments
- sale items
- void model fields

Current MVP is not yet BIR/RMO 24-2023 accreditation-ready.

## Build Rule Before Multi-Tenant

Do not resume multi-tenant host/client work until the compliance spine is designed.

Reason:

Each tenant/store/branch may need separate taxpayer registration data, machine profile, serial number, receipt/invoice sequence, PTU/accreditation metadata, electronic journal, X-reading, Z-reading, and reports.

## RMO Control Matrix

### RMO-24-2023-C01 Machine and registration profile

Required design target:

- registered taxpayer name
- business name or style
- registered address
- branch address
- taxpayer identification number
- branch code
- machine identification number or serial number
- accreditation number
- permit to use number
- PTU date issued
- accreditation date or validity metadata
- software version/build identity
- active/inactive registration state

Salryn current state:

- store settings exist
- no dedicated BIR machine registration profile yet
- no PTU/accreditation metadata yet
- no machine serial/MIN boundary yet

Implementation target:

J21-2.

### RMO-24-2023-C02 Official receipt/invoice sequence

Required design target:

- official receipt/invoice number must be sequential
- official receipt/invoice number must be at least six digits where required
- official sequence must not be random/GUID-style
- sequence must support start/end tracking
- sequence must support reset counter strategy where applicable
- sequence must prevent silent reuse
- sequence must support reprint copy tracking
- sequence must support void/cancel/refund traceability

Salryn current state:

- has a ReceiptNumber value object
- current flow can generate receipt-like values
- current flow is not yet official BIR serial control
- current receipt format is not yet a compliance sequence

Implementation target:

J21-3.

### RMO-24-2023-C03 Official receipt/invoice required header fields

Required design target:

- registered taxpayer name
- business name or style
- TIN with branch code
- registered business address
- transaction date
- transaction time
- receipt/invoice number
- machine serial number / MIN where applicable
- accreditation number
- PTU number
- cashier/user identifier
- terminal/register identity where applicable

Salryn current state:

- sale date exists
- cashier display name exists
- receipt number exists
- store settings exist
- BIR header profile not yet modeled

Implementation target:

J21-4.

### RMO-24-2023-C04 Item and sale amount breakdown

Required design target:

- quantity
- description/items or service
- unit cost
- line total
- subtotal
- discount
- tax
- total amount
- amount paid
- change
- payment method

Salryn current state:

- sale model has subtotal, discount, tax, total, amount paid, change
- sale items have product name snapshot, barcode snapshot, unit name, quantity, unit price, discount, line total
- payments have method, amount, and paid timestamp

Implementation target:

J21-4 receipt snapshot formalization.

### RMO-24-2023-C05 VAT, non-VAT, exempt, and zero-rated buckets

Required design target:

- VATable sales
- VAT amount
- VAT-exempt sales
- zero-rated sales
- non-VAT statement where applicable
- VAT/non-VAT classification per store/taxpayer profile
- receipt/invoice output must reflect tax mode correctly

Salryn current state:

- sale has tax field
- store settings has tax mode flag
- no VAT bucket model yet
- no non-VAT/exempt/zero-rated breakdown yet

Implementation target:

J21-4 and J21-6.

### RMO-24-2023-C06 Customer and discount eligibility fields

Required design target:

- customer/buyer name/address/TIN/business style where required
- SC/PWD/NAC/Solo Parent related fields where applicable
- discount type
- discount identifier
- discount amount
- required VAT exemption behavior where applicable

Salryn current state:

- no customer capture yet
- no statutory discount model yet
- generic discount fields exist

Implementation target:

later J21 sub-phase after receipt spine.

### RMO-24-2023-C07 Tamper resistance and official transaction integrity

Required design target:

- completed official sales must not be silently edited
- void/cancel/refund must be a traceable reversal path
- reprint must be marked
- training/demo mode output must never look official
- official sales data must stay preserved
- sales data must not be manually deletable through normal app workflows

Salryn current state:

- completed sale write is transactional
- void model fields exist
- no official transaction lock policy yet
- no reprint marking yet
- no training-mode watermark policy yet

Implementation target:

J21-5 and J21-6.

### RMO-24-2023-C08 Electronic journal and audit journal

Required design target:

- journal must capture every official receipt/invoice output
- journal must capture reprint events
- journal must capture void/cancel/refund events
- journal must capture cashier/user identity
- journal must capture timestamps
- journal must preserve receipt snapshots
- journal must support review/export/backup

Salryn current state:

- audit read model exists
- sale receipt snapshot field exists
- no dedicated electronic journal append model yet
- no dedicated official receipt journal export yet

Implementation target:

J21-5.

### RMO-24-2023-C09 X-reading and Z-reading

Required design target:

- X-reading: inquiry report without resetting totals
- Z-reading: end-of-day/end-of-shift closing report
- report must include sales totals
- report must include VAT/non-VAT/exempt/zero-rated buckets
- report must include discounts, voids, refunds where applicable
- report must include cash/payment totals
- report must include receipt/invoice range covered
- report must include accumulated grand total where applicable
- Z-reading must be preserved

Salryn current state:

- cashier session exists
- sales history exists
- no X-reading model yet
- no Z-reading model yet
- no accumulated grand total model yet

Implementation target:

J21-6.

### RMO-24-2023-C10 Backend reports

Required design target:

- sales book/report
- VAT sales report
- VAT exempt sales report
- zero-rated sales report
- void/cancel/refund report
- cashier accountability report
- end-of-shift report
- product sales report
- payment summary

Salryn current state:

- sale read repository exists
- cashier session read repository exists
- no formal RMO report package yet

Implementation target:

J21-6 and reporting phase.

### RMO-24-2023-C11 Data transmission and export readiness

Required design target:

- structured export format
- JSON-ready sales payload where applicable
- exported records must retain official receipt/invoice identity
- export must include machine/profile identifiers
- export must not mutate original official data

Salryn current state:

- no compliance export layer yet

Implementation target:

later J21 sub-phase after official receipt/journal models.

### RMO-24-2023-C12 Backup, retention, and recovery

Required design target:

- official sales records must be retained
- backup/recovery path must be documented
- local database must not be casually reset in production mode
- development reset must remain separated from production
- restored database must preserve official sequences and journals

Salryn current state:

- development reset/seed tools exist
- production backup/restore policy not yet implemented
- no official retention guard yet

Implementation target:

later J21 sub-phase before installer/production.

### RMO-24-2023-C13 Accreditation evidence pack

Required design target:

- system description
- software version
- user manual
- technical architecture
- database/data flow description
- sample receipt/invoice
- X-reading sample
- Z-reading sample
- e-journal sample
- backend report samples
- test account/setup instructions
- system demonstration checklist
- change log and version control evidence

Salryn current state:

- handoffs exist
- QA handoff exists
- no accreditation evidence pack yet

Implementation target:

after J21-2 through J21-7.

## J21-2 Schema Foundation

J21-2 adds the bir_machine_profiles table for RMO-24-2023-C01, partial C03, and partial C12.

This table is the future source for taxpayer identity, branch identity, machine identity, serial/MIN fields, accreditation/PTU fields, software identity, and official-output gating.

Receipt printer and barcode scanner support remain hardware-boundaries, not compliance blockers:

- receipt printer output must later consume compliant receipt snapshots through print_jobs/printer adapter
- barcode scanner input should continue as text/barcode input through checkout product lookup
- phone-camera barcode scanning can later submit decoded barcode text to the same lookup boundary

J21-2 still does not make Salryn accreditation-ready.

## J21-3A Official Receipt/Invoice Sequence Schema Foundation

J21-3A adds the bir_official_document_sequences table for RMO-24-2023-C02.

This table is the future source for official receipt/invoice sequence ownership, range control, active-scope guarding, and minimum six-digit padding.

J21-3A is still schema foundation only:

- no official receipt/invoice number allocation yet
- no checkout flow wiring
- no receipt snapshot generation
- no receipt printing
- no accreditation readiness claim
- no multi-tenant work

Future J21-3B must add the allocation service that advances current_number transactionally and gates official allocation through an active machine profile.

## J21-3B Official Receipt/Invoice Sequence Allocation Service

J21-3B adds the OfficialDocumentSequenceAllocationService for RMO-24-2023-C02.

This service advances current_number transactionally and returns deterministic padded sequence values.

Allocation is gated by an active bir_machine_profiles row with compliance_mode ACCREDITED and official_output_enabled true.

J21-3B is still allocation service only:

- no checkout flow wiring
- no receipt snapshot generation
- no receipt printing
- no official receipt/invoice rendering
- no electronic journal
- no X-reading/Z-reading
- no accreditation readiness claim
- no multi-tenant work

Future J21-3C or J21-4 must decide how allocated sequence values are consumed by receipt snapshots without bypassing machine/profile and journal controls.

## J21-4A Official Document Snapshot Draft Contracts

J21-4A adds neutral OfficialDocumentSnapshotDraft contracts for RMO-24-2023-C03 and C04 shape planning.

OfficialDocumentSnapshotDraft is a data shape only. It does not render, persist, print, allocate sequence numbers, wire checkout, or claim official output readiness.

C03 partial coverage:

- registered taxpayer name
- business name or style
- TIN and branch code
- registered business address
- transaction date/time shape
- future document number shape
- machine serial/MIN shape
- accreditation/PTU shape
- cashier and terminal identifier shape

C04 partial coverage:

- item description
- quantity
- unit price
- discount
- line total
- subtotal
- tax
- total
- amount paid
- change
- payment method

C05 remains deferred. J21-4A only carries a tax-mode placeholder and explicit VAT bucket deferral flag.

J21-4A is still contracts only:

- no renderer
- no printer
- no checkout wiring
- no saved sale receipt_snapshot writes
- no database migration
- no sequence allocation
- no electronic journal
- no X/Z reading
- no VAT bucket engine
- no official receipt/invoice output claim

Future J21-4B may add a builder that maps existing sale and machine-profile data into the draft without rendering official output.

## J21-4B Official Document Snapshot Draft Builder

J21-4B adds an in-memory OfficialDocumentSnapshotDraftBuilder for RMO-24-2023-C03 and C04 mapping.

The builder maps already-provided machine profile, sale, item, payment, and optional allocated document number data into OfficialDocumentSnapshotDraft.

J21-4B does not read from the database, allocate sequence numbers, render output, print output, wire checkout, write saved sale snapshots, create journals, or claim official output readiness.

C03 partial coverage:

- maps taxpayer/header identity from explicit machine profile input
- maps transaction date/time from sale input
- maps optional document number input without allocation
- maps cashier and terminal identifiers from explicit input

C04 partial coverage:

- maps sale item description, barcode, unit, quantity, unit price, discount, and line total
- maps subtotal, discount, tax, total, amount paid, and change
- maps payment method, amount, and paid timestamp

C05 remains deferred. The builder sets VatBucketEngineDeferred to true and carries tax-mode notes only.

J21-4B is still builder only:

- no renderer
- no printer
- no checkout wiring
- no saved sale receipt_snapshot writes
- no database migration
- no sequence allocation
- no electronic journal
- no X/Z reading
- no VAT bucket engine
- no official receipt/invoice output claim

Future J21-4C must decide whether to add a preview-safe non-official inspector or continue into VAT bucket modeling before any output work.

## J21-4C C05 Tax Bucket Draft Contracts

J21-4C adds neutral OfficialDocumentTaxBucketDraft contracts for RMO-24-2023-C05 shape planning.

These contracts describe VAT/non-VAT/exempt/zero-rated bucket shape only.

J21-4C does not calculate tax buckets, render output, print output, wire checkout, write saved sale snapshots, create database schema, allocate sequence numbers, create journals, or claim official output readiness.

C05 partial coverage:

- VAT-registered taxpayer mode shape
- non-VAT taxpayer mode shape
- VATable sales bucket shape
- VAT amount bucket shape
- VAT-exempt sales bucket shape
- zero-rated sales bucket shape
- non-VAT sales bucket shape
- non-VAT statement placeholder shape

J21-4C is still contracts only:

- no calculator engine
- no renderer
- no printer
- no checkout wiring
- no saved sale receipt_snapshot writes
- no database migration
- no sequence allocation
- no electronic journal
- no X/Z reading
- no official receipt/invoice output claim

Future J21-4D or later phase must decide whether to build a tax bucket calculator engine or keep strengthening validation before any output work.

## J21-4D C05 Tax Bucket Draft Validation

J21-4D adds structural validation for OfficialDocumentTaxBucketDraft.

This phase validates C05 draft shape only. It does not add tax calculator logic, VAT computation rules, product tax inference, legal tax treatment decisions, rendering, printing, checkout wiring, persistence, journals, X/Z readings, or official output claims.

C05 partial validation coverage:

- taxpayer mode must be known
- bucket list must be present
- bucket kinds must be valid
- bucket amounts must be non-negative
- duplicate bucket kinds are rejected
- deferred notes must preserve no-engine and no-output boundaries
- non-VAT drafts must carry a non-VAT statement placeholder
- VAT drafts must not carry a non-VAT statement placeholder

J21-4D is still validation only:

- no tax calculator engine
- no VAT computation rules
- no renderer
- no printer
- no checkout wiring
- no saved sale receipt_snapshot writes
- no database migration
- no sequence allocation
- no electronic journal
- no X/Z reading
- no official receipt/invoice output claim

Future phase must be confirmed before any calculator engine, output renderer, checkout integration, persistence, journal, or reporting work.

## J21-5A Electronic Journal / Audit Journal Planning Guardrails

J21-5A adds planning and guardrails for the future electronic journal and audit journal boundary.

This phase is documentation plus source/guard checks only. It does not add a DB/schema migration, new journal tables, checkout wiring, receipt_snapshot writes, official document persistence, renderer, printer, sequence allocation changes, tax calculator engine, X/Z reading, or official receipt/invoice output claim.

C08 planning coverage:

- defines future electronic journal evidence boundary
- defines future audit journal evidence boundary
- identifies existing sales.receipt_snapshot, print_jobs, and audit_logs boundaries
- blocks using print_jobs as an electronic journal
- blocks upgrading audit_logs into an RMO electronic journal in this phase

C07 partial planning coverage:

- future reprint marker guardrail
- future void/cancel/refund event guardrail
- future transaction integrity guardrail
- demo/training output separation guardrail

C12 partial planning coverage:

- future export/preservation metadata guardrail
- future backup/recovery preservation guardrail
- future production reset separation guardrail

Future required capture fields planned:

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

J21-5A is still planning only:

- no DB/schema migration
- no new journal tables
- no checkout wiring
- no receipt_snapshot writes
- no official document persistence
- no renderer
- no printer
- no sequence allocation changes
- no tax calculator engine
- no X/Z reading
- no official receipt/invoice output claim

J21-5B must be explicitly confirmed before any schema, append model, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z, or official output work.

## J21-5B Electronic Journal Schema Planning

J21-5B plans future electronic journal and audit journal schema shape.

This phase is documentation plus source/guard checks only. It does not add a DB/schema migration, journal tables, checkout wiring, receipt_snapshot writes, official document persistence, renderer, printer, sequence allocation changes, tax calculator engine, X/Z reading, or official receipt/invoice output claim.

C08 planning coverage:

- future electronic_journal_entries table candidate only
- future electronic_journal_entry_events table candidate only
- future electronic_journal_exports or preservation batches candidate only
- future field ownership planning
- future relationship boundary planning

C07 partial planning coverage:

- append-only event integrity planning
- original/reprint/void/refund/cancel marker planning
- linked event correction planning

C12 partial planning coverage:

- retention and preservation batch planning
- export/preservation metadata planning
- backup/recovery evidence relationship planning

Future field ownership planned:

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

Relationship boundaries:

- sales.receipt_snapshot remains current MVP placeholder, not RMO journal
- audit_logs remains generic audit trail, not RMO electronic journal
- print_jobs remains printer queue boundary, not journal evidence
- official document draft/builder remains in-memory until future approved persistence phase
- sequence allocation must stay separate from journal append until a later atomic design is confirmed

J21-5B is still planning only:

- no DB/schema migration
- no journal tables
- no checkout wiring
- no receipt_snapshot writes
- no official document persistence
- no renderer
- no printer
- no sequence allocation changes
- no tax calculator engine
- no X/Z reading
- no official receipt/invoice output claim

J21-5C must be explicitly confirmed before any schema migration, journal table, append model, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z, or official output work.

## J21-5C Electronic Journal Schema Foundation

J21-5C adds schema foundation only for future electronic journal storage.

Schema is allowed. Behavior is not allowed yet.

C08 partial schema coverage:

- bir_electronic_journal_entries table
- bir_electronic_journal_entry_events table
- bir_electronic_journal_preservation_batches table
- machine profile ownership
- sale reference ownership
- official document sequence reference ownership
- official document number and document kind fields
- transaction timestamp, cashier/user id, and terminal id fields
- nullable future snapshot payload/hash fields

C07 partial schema coverage:

- linked journal event table
- original/reprint/void/refund/cancel markers
- event type guardrails
- duplicate document identity guardrail

C12 partial schema coverage:

- preservation/export batch metadata table
- storage reference and file hash placeholders
- created_by_user_id and created_at ownership

Relationship boundaries remain:

- sales.receipt_snapshot remains current MVP placeholder, not RMO journal
- audit_logs remains generic audit trail, not RMO electronic journal
- print_jobs remains printer queue boundary, not journal evidence
- official document draft/builder remains in-memory until future approved persistence phase
- sequence allocation remains separate from journal append until a later atomic design is confirmed

J21-5C is still schema foundation only:

- no checkout wiring
- no receipt_snapshot writes
- no official document persistence from checkout
- no renderer
- no printer
- no sequence allocation changes
- no tax calculator engine
- no X/Z reading
- no actual journal append service
- no UI
- no official receipt/invoice output claim

J21-5D must be explicitly confirmed before any journal append service, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z, UI, or official output work.

## J21-5D Electronic Journal Database Risk Audit

J21-5D audits the J21-5C electronic journal schema foundation before any version 5 hardening migration or journal behavior exists.

This phase is documentation plus source/guard checks only.

C08 risk audit coverage:

- append-only enforcement risk
- snapshot payload/hash requirement risk
- boundary protection for sales.receipt_snapshot, audit_logs, and print_jobs

C07 risk audit coverage:

- reversal reason requirement risk
- linked event integrity risk
- future UPDATE/DELETE blocking trigger decision

C12 risk audit coverage:

- preservation batch requirement risk
- export/preservation/backup recovery evidence link risk
- future preservation metadata enforcement decision

Senior-developer decision:

- do not rewrite the committed J21-5C version 4 migration
- use a new forward migration for any future schema hardening
- do not add triggers yet
- do not add version 5 yet
- do not add append service yet

Future J21-5E candidate, if approved later:

- new version 5 forward migration only
- append-only UPDATE/DELETE blocking triggers
- reason requirement trigger for VOID/REFUND/CANCEL
- preservation_batch_id requirement trigger for EXPORT/PRESERVATION/BACKUP_RECOVERY
- no checkout wiring
- no append service
- no renderer
- no printer
- no official output

J21-5D is audit only:

- no schema changes
- no DB migration
- no trigger creation
- no append service
- no checkout wiring
- no receipt_snapshot writes
- no official document persistence
- no renderer
- no printer
- no sequence changes
- no tax engine
- no X/Z reading
- no UI
- no official output claim

J21-5E must be explicitly confirmed before any version 5 migration, trigger, append service, checkout integration, renderer integration, print integration, snapshot persistence, export, X/Z, UI, or official output work.

## J21-5E Electronic Journal Schema Hardening Foundation

J21-5E adds a new forward-only version 5 migration for electronic journal DB hardening.

This phase is DB-level hardening only. Behavior is not added.

C08 partial hardening coverage:

- append-only UPDATE blocking trigger for bir_electronic_journal_entries
- append-only DELETE blocking trigger for bir_electronic_journal_entries
- one ORIGINAL event per journal entry partial unique index
- no snapshot hash NOT NULL hardening yet

C07 partial hardening coverage:

- append-only UPDATE blocking trigger for bir_electronic_journal_entry_events
- append-only DELETE blocking trigger for bir_electronic_journal_entry_events
- reason required for VOID events
- reason required for REFUND events
- reason required for CANCEL events

C12 partial hardening coverage:

- append-only UPDATE blocking trigger for bir_electronic_journal_preservation_batches
- append-only DELETE blocking trigger for bir_electronic_journal_preservation_batches
- preservation_batch_id required for EXPORT events
- preservation_batch_id required for PRESERVATION events
- preservation_batch_id required for BACKUP_RECOVERY events

Senior-developer decision preserved:

- do not rewrite the committed J21-5C version 4 migration
- use a new forward migration for hardening
- snapshot payload/hash remains nullable until renderer/snapshot persistence design exists

J21-5E is DB hardening only:

- no append service
- no checkout wiring
- no journal repository/service behavior
- no receipt_snapshot writes
- no official document persistence from checkout
- no renderer
- no printer
- no sequence allocation changes
- no tax calculator engine
- no X/Z reading
- no UI
- no official receipt/invoice output claim
- no snapshot hash NOT NULL constraint

J21-5F must be explicitly confirmed before any append service, checkout integration, renderer integration, print integration, snapshot persistence, export behavior, X/Z, UI, or official output work.

## J21-5F Electronic Journal Append Contract Planning

J21-5F adds neutral Core contracts and planning docs for the future electronic journal append boundary.

This phase is contract planning only. Behavior is not added.

C08 partial planning coverage:

- append contract shape for future electronic journal anchor creation
- machine profile, sale, official sequence, document number, and cashier identity ownership
- optional snapshot evidence shape while renderer and snapshot persistence remain deferred

C07 partial planning coverage:

- single-transaction boundary planning for official sequence allocation, snapshot persistence, journal anchor insert, and ORIGINAL event insert
- ORIGINAL event identity shape
- no reversal behavior yet

C12 partial planning coverage:

- preservation of future journal evidence boundary
- deferred export/preservation behavior

J21-5F is contract planning only:

- no append service
- no checkout wiring
- no database migration
- no repository
- no receipt_snapshot writes
- no renderer
- no printer
- no official receipt/invoice output claim

J21-5G must be explicitly confirmed before append service, checkout integration, renderer integration, print integration, export behavior, X/Z, UI, or official output work.
## J21-5G Electronic Journal Append Service Boundary

J21-5G adds the Core append service boundary and contract validator for future electronic journal ORIGINAL append behavior.

This phase is boundary and validation only.

Coverage:

- C08 partial: append service contract shape
- C07 partial: append contract validation before transaction implementation
- C12 partial: snapshot evidence validation boundary when evidence is supplied

Not added:

- no append persistence
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

## J21-5H Electronic Journal Append Atomic Boundary Rules

J21-5H adds Core atomic boundary validation rules for the future electronic journal append flow.

This phase is validation rules only.

Coverage:

- C08 partial: append boundary integrity
- C07 partial: official sequence, snapshot evidence, journal anchor, and ORIGINAL event ordering
- C12 partial: snapshot preservation boundary when evidence is supplied

Not added:

- no append persistence
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

## J21-5I Electronic Journal Append Persistence Ports

J21-5I adds Core repository and atomic persistence port contracts for the future Infrastructure append implementation.

This phase is port contracts only.

Coverage:

- C08 partial: journal append persistence contract shape
- C07 partial: atomic persistence boundary shape
- C12 partial: preservation handoff shape

Not added:

- no append persistence implementation
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

## J21-5J Electronic Journal Append Orchestration Shell

J21-5J adds the Core orchestration shell behind IElectronicJournalAppendService.

This phase coordinates validation, journal entry id creation, append timestamp creation, and atomic persistence port handoff only.

Coverage:

- C08 partial: ORIGINAL append orchestration shape
- C07 partial: validation before persistence handoff
- C12 partial: evidence preservation request handoff

Not added:

- no append persistence implementation
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

## J21-5K Electronic Journal Append Registration Boundary

J21-5K adds a Core-safe registration boundary that creates the append service behind interfaces.

This phase proves app composition can construct the service without Infrastructure wiring.

Coverage:

- C08 partial: append service registration boundary
- C07 partial: explicit atomic persistence port requirement
- C12 partial: preservation handoff remains behind a Core port

Not added:

- no append persistence implementation
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

## J21-5L Electronic Journal Append Boundary Documentation Seal

J21-5L documents and seals J21-5G through J21-5K.

This phase is documentation seal and smoke guard only.

Coverage:

- C08 partial: append boundary documentation
- C07 partial: transaction integrity boundary documentation
- C12 partial: preservation handoff documentation

Not added:

- no new append behavior
- no append persistence implementation
- no SQLite access
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

Future Infrastructure persistence, checkout integration, renderer integration, Crystal Reports wiring, X/Z reading, UI, and official output must be separately confirmed.
## J21-5N Shared SQLite Transaction Boundary Contract

J21-5N adds Infrastructure contracts for sharing one SQLite connection and active transaction across future compliance-sensitive persistence participants.

This phase is contract-only.

Coverage:

- C08 partial: electronic journal append persistence transaction boundary
- C07 partial: official sequence allocation and journal evidence atomicity boundary
- C12 partial: preservation handoff remains inside explicit transaction ownership

Risk addressed:

- the existing OfficialDocumentSequenceAllocationService currently opens and commits its own transaction
- a future electronic journal append adapter must not consume an official number in one transaction and append journal evidence in another transaction
- shared transaction participants are required before a safe adapter implementation

Not added:

- no shared transaction implementation
- no sequence service rewrite
- no SQLite adapter implementation
- no append persistence implementation
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

Future implementation must keep official sequence allocation, optional snapshot persistence, journal anchor insert, and ORIGINAL event insert inside one explicit transaction.
## J21-5O Shared SQLite Transaction Boundary Implementation

J21-5O implements the shared SQLite transaction boundary introduced in J21-5N.

This phase owns transaction execution only.

Coverage:

- C08 partial: future electronic journal append work can share one SQLite transaction
- C07 partial: future official sequence allocation and journal evidence can be committed as one unit
- C12 partial: preservation handoff can remain inside explicit transaction ownership

Proven:

- successful delegated work commits
- failed Result delegated work rolls back
- thrown delegated work rolls back
- missing delegated work fails validation

Not added:

- no sequence allocation participant
- no sequence service rewrite
- no SQLite journal adapter implementation
- no append persistence implementation
- no database migration
- no checkout wiring
- no renderer
- no printer
- no Crystal Reports wiring
- no official output claim

Future implementation must add shared-transaction participants before the electronic journal SQLite append adapter is built.
## First-Try Best-Practice Guardrails

1. Do not call any output "Official Receipt" or "Official Invoice" until C01-C05 are implemented and tested.
2. Do not print or export production-style receipts from random/GUID receipt numbers.
3. Do not implement multi-tenant until machine/profile/sequence ownership is modeled.
4. Do not add a receipt UI before the receipt snapshot generator exists.
5. Do not add Z-reading UI before the report model is deterministic and test-backed.
6. Do not allow completed sale edits; use reversal/void/refund paths only.
7. Do not allow training/demo outputs to be confused with official outputs.
8. Every compliance feature must include a smoke/check file and handoff.
9. Every compliance commit must reference at least one RMO control ID from this matrix.
10. Every compliance-visible field must be testable from repo source or smoke output.

## J21 Build Order

- J21-1: RMO 24-2023 compliance matrix and guardrails only.
- J21-2: machine/POS registration profile domain planning and schema.
- J21-3: official receipt/invoice sequence service.
- J21-4: receipt snapshot generator with required fields.
- J21-5: electronic journal/audit journal append model.
- J21-6: X-reading and Z-reading domain/report generation.
- J21-7: compliance smoke tests and evidence pack.
- J21-8: UI screens for machine profile, receipt preview, X/Z reports.

## J21-1 Non-Goals

- no schema migration
- no database write change
- no app UI change
- no receipt printing
- no X-reading/Z-reading generation
- no e-journal implementation
- no accreditation claim
- no multi-tenant work

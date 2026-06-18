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

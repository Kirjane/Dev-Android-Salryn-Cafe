# KIRCH-SANDBOX RMO 24-2023 Readiness Plan

Date: 2026-06-24
Scope: RMO readiness planning only.

This is not a BIR accreditation claim, certificate, legal opinion, or final compliance declaration.

## Source Status

Local file studied:

- `C:\Users\kirch\Downloads\RMO No. 24-2023.pdf`

Extraction result:

- The PDF is image-only in this environment.
- Machine text extraction produced blank pages.
- Official web search was attempted but did not return usable source text in this environment.

Therefore this plan uses the repo's existing RMO matrix and schema work as the technical control map. Before any public claim, the matrix must be manually verified against the official RMO text and BIR registration/accreditation requirements.

## Current Compliance Standing

Salryn is not RMO 24-2023 complete today.

Strong foundations already exist:

- BIR machine/POS registration profile table.
- BIR official document sequence table.
- Official sequence allocation service and shared-transaction participant.
- Official document snapshot draft model and builder.
- Tax bucket draft and validation shell.
- Electronic journal tables.
- Append-only triggers for electronic journal entries, events, and preservation batches.
- Electronic journal append service boundary.
- SQLite electronic journal append participant and atomic adapter.
- Smoke coverage proving sequence allocation and journal append can share a transaction.

Still missing:

- machine profile UI
- official sequence setup UI
- official checkout integration
- official snapshot renderer/persistence
- tax bucket calculation policy
- print renderer
- e-journal append from live checkout
- reprint, void, cancel, refund event paths
- X-reading and Z-reading
- preservation/export workflow
- accreditation evidence pack

## Control Readiness Matrix

### C01 Machine And Registration Profile

Current:

- `bir_machine_profiles` exists with taxpayer, branch, machine, PTU, accreditation, software, compliance mode, and official output gate fields.

Required:

- UI to create/edit/deactivate profile.
- validation for TIN/branch/machine serial/MIN/PTU/accreditation fields.
- active profile selection.
- official output gate requiring `compliance_mode = ACCREDITED` and `official_output_enabled = 1`.
- audit logs for profile changes.

### C02 Official Receipt/Invoice Sequence

Current:

- `bir_official_document_sequences` exists.
- allocation service exists.
- shared transaction participant exists.

Required:

- sequence setup UI.
- range exhaustion handling.
- sequence deactivation and history.
- official checkout wiring.
- no random/GUID official numbers.
- no silent reuse after exhaustion.

### C03 Required Header Fields

Current:

- machine profile carries taxpayer/machine/PTU/accreditation fields.
- official document snapshot draft has header shape.

Required:

- renderer consumes machine profile and transaction data.
- terminal identifier included.
- cashier/user identifier included.
- timestamp and official document number included.
- output cannot be generated from ad hoc UI labels.

### C04 Item And Sale Amount Breakdown

Current:

- sale, sale item, and payment models carry quantity, item snapshots, price, discount, subtotal, tax, total, paid, and change.

Required:

- service-owned product snapshots.
- official snapshot generator.
- consistent rounding and quantity formatting.
- receipt/invoice payload hash.
- saved snapshot before printing.

### C05 VAT, Non-VAT, Exempt, Zero-Rated Buckets

Current:

- tax bucket draft and validation exist.
- no calculator or tax policy engine exists.

Required:

- taxpayer mode configuration.
- non-VAT official wording policy.
- VAT/exempt/zero-rated bucket calculator if VAT mode is supported.
- product/category tax classification if needed.
- tests against legal/compliance expected examples.

### C06 Customer And Statutory Discount Fields

Current:

- no complete customer/statutory discount workflow.

Required:

- buyer/customer capture where required.
- SC/PWD/NAC/Solo Parent data model if applicable.
- statutory discount validation.
- VAT exemption handling if applicable.
- receipt output fields.

### C07 Tamper Resistance And Transaction Integrity

Current:

- sales are transactional.
- electronic journal rows are append-only.
- void fields exist.

Required:

- completed sale lock policy.
- void/reversal service and UI.
- reprint markers.
- training/demo watermark rules.
- official sale delete/edit prevention through services and UI.

### C08 Electronic Journal

Current:

- journal tables, triggers, append contracts, append service, SQLite adapter, and smoke coverage exist.

Required:

- live checkout append integration.
- ORIGINAL event from sale completion.
- REPRINT event path.
- VOID/CANCEL/REFUND event path.
- export/preservation event path.
- review UI or export.

### C09 X-Reading And Z-Reading

Current:

- cashier sessions exist.
- no X/Z models or reports exist.

Required:

- X-reading report without reset.
- Z-reading close report.
- sequence range coverage.
- tax bucket totals.
- void/reprint/refund totals.
- payment breakdown.
- accumulated grand total if required.
- preserved Z-reading record.

### C10 Backend Reports

Current:

- sale read models and dashboard foundations exist.

Required:

- sales book/report.
- VAT/non-VAT/exempt/zero-rated reports.
- void/cancel/refund report.
- cashier accountability report.
- product sales report.
- payment summary.

### C11 Data Transmission And Export Readiness

Current:

- no compliance export layer.

Required:

- structured export.
- includes machine/profile identifiers.
- includes official document identity.
- export does not mutate original official data.
- export/preservation batch evidence.

### C12 Backup, Retention, Recovery

Current:

- backup logs exist.
- electronic journal preservation table exists.
- production backup/restore service is not implemented.

Required:

- SQLite online backup API.
- encrypted backup option.
- backup manifest with hashes.
- restore verification.
- pre-restore backup.
- official sequence/journal preservation checks.

### C13 Accreditation Evidence Pack

Current:

- many planning/handoff docs exist.

Required:

- system description.
- version/build identity.
- architecture and database/data-flow docs.
- user manual.
- sample official receipt/invoice.
- sample X/Z reports.
- e-journal sample.
- backend report samples.
- setup and test accounts.
- change log and smoke results.

## Official Mode Gate

Official output must be impossible unless all are true:

- active BIR machine profile exists
- profile compliance mode is ACCREDITED
- official output enabled is true
- active official sequence exists for document kind
- sequence has remaining range
- snapshot renderer is enabled
- tax mode is configured and validated
- electronic journal append service is available
- official checkout transaction can append journal evidence atomically
- printer/output template is marked official-capable

If any gate fails:

- produce non-official/training output only
- mark preview clearly as non-official
- do not allocate official number
- do not append official journal entry

## First RMO Coding Sequence

1. Machine profile management UI and repository.
2. Official sequence management UI and repository.
3. Official snapshot generator.
4. Tax bucket policy for non-VAT first.
5. Atomic official checkout service.
6. Electronic journal append from checkout.
7. Official receipt renderer and print job integration.
8. Reprint and void event paths.
9. X/Z report models.
10. Export/preservation workflow.
11. Evidence pack.

## Compliance Exit Criteria

Salryn can be called RMO-ready only after:

- official source text is manually verified
- compliance matrix is updated with citations/sections
- all C01-C13 controls have implemented evidence
- official checkout transaction passes smoke and negative tests
- X/Z reports are deterministic
- electronic journal export/preservation works
- evidence pack is generated from a known test database
- legal/compliance reviewer signs off

# KIRCH-SANDBOX Schema Plan

Date: 2026-06-24
Scope: schema planning only, no migrations in this phase.

## Current Schema Standing

Existing migrations create these groups:

- app metadata
- roles, users, permissions
- store settings
- product catalog
- suppliers, units, brands, categories
- products
- receipt sequences
- cashier sessions
- sales, sale items, payments
- stock movements
- custom fields
- print jobs
- audit logs
- error logs
- import/export jobs
- backup logs
- BIR machine profiles
- BIR official document sequences
- BIR electronic journal entries, events, and preservation batches
- append-only triggers for electronic journal evidence

This is a strong foundation. The missing work is not mostly table creation. The missing work is:

- service orchestration
- production workflow ownership
- official snapshot persistence
- terminal/server-client persistence
- backup manifests
- report/X/Z tables
- UI consumption

## Required Forward Schema Additions

### deployment_profiles

Purpose:

- Persist whether this install is standalone Host, cashier PC Host, admin PC Host, dedicated Host, or receiver/client.

Fields:

- id
- tenant_id
- store_id
- mode
- machine_role
- host_id
- host_machine_name
- host_address
- host_port
- uses_loopback
- is_active
- created_at
- updated_at

Rules:

- one active deployment profile per installation
- receiver/client mode must not create local production database

### terminal_registry

Purpose:

- Track devices allowed to connect to the Store Host.

Fields:

- id
- tenant_id
- store_id
- terminal_id
- device_key_hash
- machine_name
- machine_role
- display_name
- status
- paired_at
- revoked_at
- last_seen_at
- created_by_user_id
- created_at

Rules:

- device key is never stored plain text
- revoked terminal cannot submit commands
- terminal id must be included in Host request context

### pairing_codes

Purpose:

- Allow receiver terminals to join a Store Host.

Fields:

- id
- tenant_id
- store_id
- code_hash
- expires_at
- max_uses
- used_count
- status
- created_by_user_id
- created_at
- consumed_at

Rules:

- pairing codes expire
- code values are not stored plain text
- pairing creates a terminal registry entry

### receipt_snapshots

Purpose:

- Store stable receipt payloads separately from mutable sale reads.

Fields:

- id
- sale_id
- receipt_no
- snapshot_kind
- payload_format
- payload_text
- payload_json
- payload_hash
- hash_algorithm
- created_at
- created_by_user_id

Rules:

- created once for original sale
- reprint reads saved snapshot
- official mode links to BIR electronic journal evidence

### official_document_snapshots

Purpose:

- Store rendered or structured official receipt/invoice evidence after official mode is approved.

Fields:

- id
- sale_id
- bir_machine_profile_id
- official_document_sequence_id
- official_document_number
- document_kind
- payload_json
- payload_text
- payload_hash
- hash_algorithm
- transaction_timestamp
- cashier_user_id
- terminal_id
- created_at

Rules:

- official snapshot must be created in the same transaction as sequence allocation and journal append
- payload hash required once renderer is implemented
- not used in non-official mode

### print_job_attempts

Purpose:

- Preserve multiple attempts for one print job.

Fields:

- id
- print_job_id
- attempt_number
- status
- error_message
- started_at
- finished_at

Rules:

- sale commit creates print job
- each print/reprint attempt is tracked
- printer failure does not roll back sale

### x_readings

Purpose:

- Store non-reset inquiry reports.

Fields:

- id
- bir_machine_profile_id
- terminal_id
- cashier_user_id
- range_started_at
- range_ended_at
- receipt_from
- receipt_to
- gross_sales_cents
- discount_cents
- void_cents
- tax_cents
- net_sales_cents
- payment_totals_json
- tax_buckets_json
- generated_at
- payload_hash

Rules:

- does not close/reset period
- source rows are deterministic

### z_readings

Purpose:

- Store end-of-day or end-of-shift close reports.

Fields:

- id
- bir_machine_profile_id
- terminal_id
- closed_by_user_id
- cashier_session_id
- business_date
- range_started_at
- range_ended_at
- receipt_from
- receipt_to
- gross_sales_cents
- discount_cents
- void_cents
- refund_cents
- tax_cents
- net_sales_cents
- payment_totals_json
- tax_buckets_json
- accumulated_grand_total_cents
- generated_at
- payload_hash
- status

Rules:

- append-only after close
- range cannot overlap for same machine/profile and period
- links to electronic journal preservation/export later

### backup_manifests

Purpose:

- Make backups restorable and supportable, not just copied files.

Fields:

- id
- backup_log_id
- tenant_id
- store_id
- app_version
- schema_version
- database_file_hash
- backup_file_hash
- backup_type
- encrypted
- created_at
- notes

Rules:

- backup hash is required
- restore checks manifest before replacing database
- production restore requires pre-restore backup

### support_bundles

Purpose:

- Collect logs, version data, and sanitized diagnostics for support.

Fields:

- id
- created_by_user_id
- bundle_path
- app_version
- schema_version
- included_sections_json
- file_hash
- created_at

Rules:

- no password hashes
- no full database unless explicitly approved as backup/support package

## Changes To Existing Tables

### sales

Needed additions:

- receipt_snapshot_id nullable
- official_document_snapshot_id nullable
- bir_machine_profile_id nullable
- terminal_id nullable
- sale_mode text, values: NON_OFFICIAL, OFFICIAL, TRAINING
- locked_at nullable

Rules:

- completed sales cannot be edited
- official sales require official document snapshot and journal entry
- training sales must never look official

### print_jobs

Needed additions:

- receipt_snapshot_id nullable
- official_document_snapshot_id nullable
- is_reprint integer default 0
- requested_by_user_id nullable
- attempt_count integer default 0

### audit_logs

Needed additions:

- terminal_id nullable
- request_id nullable
- correlation_id nullable
- ip_or_machine_name nullable

### store_settings

Needed additions:

- legal_taxpayer_mode text, values: NON_VAT, VAT, UNKNOWN
- receipt_mode text, values: NON_OFFICIAL_ONLY, OFFICIAL_ALLOWED
- default_printer_profile_id nullable

Do not let store settings alone enable official output. Official output must require active BIR machine profile plus active official sequence.

## Data Flow: Non-Official Checkout

Single transaction:

1. Re-read products.
2. Validate stock and discounts.
3. Allocate non-official receipt sequence.
4. Build sale.
5. Insert sale.
6. Insert items.
7. Insert payments.
8. Insert stock movements.
9. Update products.
10. Save receipt snapshot.
11. Insert print job.
12. Insert audit log.
13. Commit.

After commit:

1. Try print.
2. Update print job status and attempt history.

## Data Flow: Official Checkout

Single transaction:

1. Re-read products.
2. Validate machine profile official gate.
3. Validate active official sequence.
4. Validate tax mode and tax buckets.
5. Allocate official document number.
6. Build official document snapshot.
7. Hash snapshot.
8. Build sale.
9. Insert sale and lines.
10. Insert payments.
11. Insert stock movements and product updates.
12. Insert official document snapshot.
13. Insert electronic journal entry.
14. Insert ORIGINAL event.
15. Link sale to snapshot.
16. Insert print job.
17. Insert audit log.
18. Commit.

After commit:

1. Try print.
2. Update print job attempt state.

## Schema Guardrails

- Do not rewrite existing migrations.
- Add forward-only migrations.
- Do not remove current RMO append-only triggers.
- Do not make official snapshot payload/hash nullable after renderer is approved.
- Do not store receiver/client terminal device keys plain text.
- Do not share a SQLite file over LAN.
- Do not use `sales.receipt_snapshot` as the only official evidence once official mode is added.

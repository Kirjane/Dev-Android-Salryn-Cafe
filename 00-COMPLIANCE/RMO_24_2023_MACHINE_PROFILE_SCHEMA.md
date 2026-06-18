# RMO 24-2023 Machine / POS Registration Profile Schema

J21-2 status: schema foundation.

RMO controls:

- RMO-24-2023-C01 Machine and registration profile
- RMO-24-2023-C03 Official receipt/invoice required header fields, partial
- RMO-24-2023-C12 Backup, retention, and recovery, partial

## Scope

This phase adds the database foundation for machine/POS registration identity.

No UI.
No receipt printing.
No official receipt/invoice output.
No X-reading or Z-reading.
No electronic journal implementation.
No multi-tenant work.
No hardware dependency.

## New Table

bir_machine_profiles

Purpose:

This table stores the local machine/POS registration identity that future receipt/invoice, journal, X-reading, Z-reading, and accreditation evidence features must read from.

## Key Fields

Registration ownership:

- registration_scope_code
- store_settings_id
- is_active

Taxpayer and branch identity:

- registered_taxpayer_name
- business_name
- registered_address
- branch_address
- taxpayer_identification_number
- branch_code

Machine / POS identity:

- machine_identification_number
- machine_serial_number
- pos_terminal_number

Accreditation / permit identity:

- accreditation_number
- permit_to_use_number
- permit_to_use_issued_at
- accreditation_issued_at
- accreditation_valid_until

Software identity:

- software_name
- software_version
- software_build

Compliance gating:

- compliance_mode
- official_output_enabled

Audit timestamps:

- created_at
- updated_at
- deactivated_at

## Official Output Guard

official_output_enabled must stay false unless compliance_mode is ACCREDITED.

This prevents future UI or receipt code from accidentally treating test/development output as official.

## Hardware Boundary

Receipt printer:

- No physical printer is required for this phase.
- Existing print_jobs table already has printer_name, payload_text, status, and printed_at.
- Future receipt printing should consume finalized receipt snapshots through a printer adapter.
- Do not bind receipt compliance to a specific printer model yet.

Barcode scanner:

- No schema is required for basic barcode scanner support.
- USB barcode scanners usually behave like keyboard input.
- Current checkout product lookup already accepts typed/scanned barcode text.
- Future phone-camera scanning should submit decoded barcode text into the same checkout search boundary.

## Multi-Tenant Boundary

This schema uses registration_scope_code to keep the single-store/local model explicit while leaving a controlled migration path for future tenant/store/branch ownership.

Do not resume multi-tenant host/client work until receipt sequence ownership and machine profile ownership are modeled together.
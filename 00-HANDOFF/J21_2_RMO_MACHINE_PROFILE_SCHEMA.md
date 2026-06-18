# J21-2 - RMO Machine / POS Registration Profile Schema

Status: schema foundation only.

## Scope

This commit adds the BIR/RMO machine profile schema foundation.

RMO controls:

- RMO-24-2023-C01 Machine and registration profile
- RMO-24-2023-C03 Official receipt/invoice required header fields, partial
- RMO-24-2023-C12 Backup, retention, and recovery, partial

## Added

- New migration: BirMachineProfileSchemaMigration
- New table: bir_machine_profiles
- Migration registration in DefaultDatabaseInitializationFactory
- Source/runtime guard checks for the new schema
- Strict Kirch development comments inside the migration and initialization wiring
- Machine profile schema documentation

## Not Added

- No UI.
- No receipt printing.
- No official receipt/invoice output.
- No X-reading/Z-reading.
- No electronic journal.
- No multi-tenant work.
- No physical printer dependency.
- No physical barcode scanner dependency.

## Hardware Prep Decision

Receipt printer:

Existing print_jobs remains the future printer queue boundary. Physical printer support should come later through an adapter after receipt snapshots are compliant.

Barcode scanner:

No schema is needed yet. Keyboard-emulated scanners and typed barcodes should use the current checkout product lookup path. Phone-camera scanning can later decode barcode text and submit it to the same lookup boundary.

## Compliance Guard

official_output_enabled is blocked unless compliance_mode is ACCREDITED.

This prevents accidental official-output behavior while Salryn is still below full RMO 24-2023 readiness.

## Next

J21-3 must build the official receipt/invoice sequence service.

It must not use random/GUID-style receipt numbers for official output.
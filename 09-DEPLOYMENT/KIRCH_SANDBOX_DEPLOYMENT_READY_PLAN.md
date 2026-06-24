# KIRCH-SANDBOX Deployment Ready Plan

Date: 2026-06-24
Scope: deployment planning only.

## Release Target

First production target:

- Windows 10 or Windows 11
- single PC standalone Host
- local SQLite database
- keyboard-wedge barcode scanner
- receipt printer support
- local backups
- admin and cashier accounts

Second production target:

- LAN Host/Receiver mode after standalone mode is stable.

## Production Paths

Production data should live under:

```text
C:\ProgramData\Salryn\
  data\
  backups\
  exports\
  logs\
  receipts\
  config\
```

Rules:

- database never lives in Downloads
- database never lives beside a temporary executable
- updates never delete data
- backups never overwrite each other silently
- restore always creates pre-restore backup

## Build And Publish

Required release build:

- clean restore
- clean build
- smoke tools pass
- vulnerability warnings resolved or formally accepted
- version/build stamped
- installer or reproducible publish folder

Required commands:

```text
dotnet restore Salryn.sln
dotnet build Salryn.sln --configuration Release --no-restore
dotnet run --configuration Release --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj
dotnet run --configuration Release --project tools/Salryn.Tools.InfrastructureSmoke/Salryn.Tools.InfrastructureSmoke.csproj -- full
dotnet run --configuration Release --project tools/Salryn.Tools.CheckoutLocalHostSmoke/Salryn.Tools.CheckoutLocalHostSmoke.csproj -- database
```

The infrastructure `full` smoke resets the dev database folder. Run it only against a disposable dev path.

## Installer Requirements

Installer must:

- install app binaries
- install or require correct .NET Desktop Runtime
- create ProgramData folders
- create desktop shortcut
- preserve existing database during update
- run migrations safely
- backup before production migration
- never wipe client data

## Backup Requirements

Backup engine must:

- use SQLite online backup API, not raw file copy while writing
- write timestamped file
- write manifest
- calculate hash
- verify file exists and size/hash match
- log backup result
- support external folder
- optionally encrypt backup

Restore engine must:

- require admin
- verify manifest
- create pre-restore backup
- stop app writes during restore
- replace database safely
- log restore result
- preserve official sequence and journal evidence

## Support Requirements

Support bundle should include:

- app version
- schema version
- machine role
- deployment mode
- recent error logs
- recent audit summaries
- printer settings
- backup health
- host connection diagnostics
- no password hashes

## Host/Receiver Deployment Requirements

Host mode:

- owns SQLite
- owns migrations
- owns backups
- owns receipt sequence allocation
- owns official journal append
- exposes LAN API

Receiver mode:

- does not open SQLite
- stores only local terminal config and safe cache if needed
- sends commands to Host
- handles Host unavailable state clearly

Network requirements:

- manual host IP entry
- pairing code
- terminal revoke
- firewall diagnostics
- connection test
- clear message when Host PC is off

## Client Handoff Checklist

Before any client deployment:

- install app
- run first setup
- create admin user
- create cashier user
- configure store profile
- configure printer
- test barcode scanner
- add sample product
- open shift
- complete sample sale
- print receipt
- reprint receipt
- void sample sale
- close shift
- run daily report
- export CSV/PDF
- create backup
- verify backup
- document restore process

For RMO mode:

- do not enable official output until accreditation evidence is complete and reviewed.

## Production Exit Criteria

Salryn is deployment-ready only when:

- no high-severity dependency warning remains unresolved
- Release build passes
- smoke tools pass
- checkout completion is service-owned
- receipt snapshot and print job flow exists
- backup/restore exists and is tested
- first-run setup is complete
- standalone mode has no hard-coded production identities
- support logs exist
- installer/update preserves data

LAN deployment-ready only when:

- second PC can pair
- second PC can login
- second PC can lookup product
- second PC can complete sale through Host
- Host serializes write operations
- client cannot access SQLite directly
- Host unavailable state is friendly and recoverable

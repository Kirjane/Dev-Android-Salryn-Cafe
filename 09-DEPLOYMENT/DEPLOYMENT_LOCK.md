# Salryn POS Deployment Lock

Status:
PLANNING LOCK ONLY.
No installer yet.
No app code yet.

Goal:
Define how Salryn should eventually be installed, backed up, configured, and handed over to clients.

---

# 1. V1 Deployment Model

Salryn v1 deployment target:
- One Windows PC or laptop
- One local SQLite database
- One cashier station
- One receipt printer
- One barcode scanner
- Local backup folder

Not included in v1:
- Multi-terminal sync
- Cloud database
- Branch sync
- Web dashboard
- Mobile app

---

# 2. Target Client Setup

Minimum hardware:
- Windows 10 or Windows 11 PC/laptop
- 4GB RAM minimum
- SSD recommended
- USB barcode scanner in keyboard-wedge mode
- ESC/POS thermal receipt printer
- Stable power source

Preferred printer:
- LAN/Ethernet ESC/POS printer

Fallback printer:
- Windows-installed receipt printer

---

# 3. Production Data Path

Recommended production path:

C:\ProgramData\Salryn\
├─ data\
│  └─ salryn.db
├─ backups\
├─ exports\
├─ logs\
├─ receipts\
└─ config\

Rules:
- Database should not live inside Downloads.
- Database should not live inside temporary folders.
- Backups should not overwrite each other.
- Exports should be separate from database backups.

---

# 4. Installer Goal

Future installer should:
- Install Salryn app
- Create C:\ProgramData\Salryn folders
- Create desktop shortcut
- Install required runtime if needed
- Preserve existing database during update
- Never delete client data during update

Avoid:
- Installer that wipes old data
- Installer that stores database beside EXE only
- Manual copy-paste deployment forever

---

# 5. First-Run Setup Wizard

First launch should ask for:
1. Store name
2. Business name optional
3. Address
4. Contact number
5. Receipt width: 58mm or 80mm
6. Default currency label: PHP
7. Admin username
8. Admin password
9. Printer mode:
   - File test mode
   - LAN printer
   - Windows printer
10. Backup folder

After setup:
- Create default roles
- Create admin user
- Create default category
- Create default units
- Save store settings
- Run test receipt option

---

# 6. Backup Policy

V1 required:
- Manual backup button
- Timestamped backup file
- Backup log
- Backup verification

Recommended backup filename:
salryn-backup-YYYYMMDD-HHMMSS.db

Recommended backup locations:
- Local backups folder
- Optional external USB drive
- Optional client-chosen folder

Rules:
- Backup before restore.
- Restore requires admin.
- Restore should be logged.
- App should warn user before restore.

---

# 7. Update Policy

Future update rules:
- Never overwrite database.
- Run migrations safely.
- Backup before migration.
- Keep migration version in app_metadata.
- If migration fails, do not launch partially upgraded app.

---

# 8. Client Handoff Checklist

Before handing over to client:
- Store profile configured
- Admin account created
- Cashier account created
- Printer test passed
- Barcode scanner test passed
- Sample product added
- Sample sale completed
- Receipt printed
- Reprint tested
- Void tested
- Backup tested
- Daily report tested
- Monthly PDF tested

---

# 9. Support Notes

Support should ask:
- Windows version
- Printer model
- Printer connection type
- Barcode scanner model
- Salryn version
- Database backup date
- Screenshot of error
- Log file if available

---

# 10. Legal/Compliance Note

Salryn v1 should be positioned as:
Native sales and inventory POS software.

Do not claim:
- BIR-accredited
- BIR-compliant official receipt system
- Official invoice system

Unless:
- The proper compliance, registration, and accreditation workflow is actually completed.

---

# FINAL REVIEW PATCH: Deployment Additions

## Client Setup Additions

Before handoff:
- Create first cashier shift test.
- Close cashier shift test.
- Export product CSV test.
- Backup after setup.

---

## Support Additions

When debugging, also ask:
- Was shift mode enabled?
- Was the cashier shift open or closed?
- Last receipt number printed
- Last backup file path
- Error log file/table entry if available


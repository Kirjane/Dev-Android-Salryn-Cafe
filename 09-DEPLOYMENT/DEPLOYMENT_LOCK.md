# Salryn POS Deployment Lock

Status:
BETA / RELEASE SCOPE LOCK.
No installer proof yet.

Goal:
Define how Salryn should be installed, backed up, configured, and handed over without adding hardware-drift to beta scope.

---

## 1. Beta deployment model

Salryn beta/release deployment target:

- One Windows PC or laptop.
- One local SQLite database.
- One cashier/operator workstation.
- Windows print queue internal receipt printing only if manually proven on that host.
- Keyboard-wedge barcode scanner if scanner flow is tested.
- Local backup folder once Backup UI is implemented and verified.

Not included in beta/release scope:

- Multi-terminal production sync.
- Cloud database.
- Branch sync.
- Web dashboard.
- Mobile app.
- Hardware peripherals beyond the Windows print queue.
- Hardware bundle/support claim.

---

## 2. Target client setup

Minimum host:

- Windows 10 or Windows 11 PC/laptop.
- 4GB RAM minimum.
- SSD recommended.
- Stable power source.

Printer boundary:

- Windows print queue only.
- Internal receipt only.
- Manual printer proof required before any local paper-output claim.
- No official/BIR output.
- No ESC/POS.
- No cutter.
- No cash drawer.
- No thermal width/profile support.
- No hardware bundle/support claim.

Barcode boundary:

- Keyboard-wedge scanner mode only.
- No scanner driver or HID integration claim.

---

## 3. Production data path

Recommended production path:

```text
C:\ProgramData\Salryn\
├─ data\
│  └─ salryn.db
├─ backups\
├─ exports\
├─ logs\
├─ receipts\
└─ config\
```

Rules:

- Database should not live inside Downloads.
- Database should not live inside temporary folders.
- Backups should not overwrite each other.
- Exports should be separate from database backups.

---

## 4. Installer goal

Installer remains outside this lane.

Future installer proof should:

- Install Salryn app.
- Create `C:\ProgramData\Salryn` folders.
- Create desktop shortcut.
- Install required runtime if needed.
- Preserve existing database during update.
- Never delete client data during update.

Avoid:

- Installer that wipes old data.
- Installer that stores database beside EXE only.
- Manual copy-paste deployment forever.

---

## 5. First-run setup boundary

First-run/client setup may ask for:

1. Store name.
2. Optional business name.
3. Address.
4. Contact number.
5. Default currency label: PHP.
6. Admin username.
7. Admin password.
8. Printer mode: Disabled or Windows Printer.
9. Exact Windows printer name when Windows Printer mode is selected.
10. Backup folder only after Backup UI is implemented and verified.

Do not add first-run controls for cash drawer, cutter, ESC/POS, thermal profiles, hardware receipt width/profile, or hardware bundle selection.

---

## 6. Backup policy

Backup UI remains a separate release blocker/lane.

Required when implemented:

- Manual backup button.
- Timestamped backup file.
- Backup manifest/verification.
- Restore warning.
- Restore requires admin.
- Restore should be logged.
- Backup before restore.

---

## 7. Update policy

Update rules:

- Never overwrite database.
- Run migrations safely.
- Backup before migration.
- Keep migration version in app metadata.
- If migration fails, do not launch partially upgraded app.

---

## 8. Client handoff checklist

Before handing over to client:

- Store profile configured.
- Admin account created.
- Cashier account created.
- Barcode scanner test passed if scanner is used.
- Sample product added.
- Sample sale completed.
- Internal receipt snapshot confirmed.
- Windows print queue manual test passed only if a Windows printer is configured.
- Reprint tested.
- Void tested.
- Backup tested only after Backup UI is implemented.
- Detailed Reports tested.

Do not add cash drawer, cutter, ESC/POS, thermal profile, hardware bundle, or hardware support validation to beta handoff.

---

## 9. Support notes

Support may ask:

- Windows version.
- Windows printer name/model if Windows printing is configured.
- Printer connection type only for Windows queue troubleshooting.
- Barcode scanner model if scanner is used.
- Salryn version.
- Database backup date after Backup UI exists.
- Screenshot of error.
- Log file if available.

No beta support claim is made for cash drawer, cutter, ESC/POS, thermal profiles, or bundled POS hardware.

---

## 10. Legal/compliance note

Salryn beta/release should be positioned as:

Native sales and inventory POS software with internal receipt snapshots and optional Windows print queue submission.

Do not claim:

- BIR-accredited.
- BIR-compliant official receipt system.
- Official invoice system.
- eSales production transmission.
- Hardware bundle/support.

Unless the proper compliance, registration, accreditation, hardware, and acceptance workflows are actually completed in separate approved lanes.

Status: `SCOPE_LOCKED_OUT_OF_BETA_HARDWARE_EXTRAS`

# Salryn POS Hardware Lock

Status:
PLANNING LOCK ONLY.

Hardware target:
Small retail/general merchandise POS setup.

---

# 1. Barcode Scanner

V1 decision:
Use keyboard-wedge barcode scanner mode.

Meaning:
The scanner behaves like a keyboard and types the barcode into the focused input box, then sends Enter.

Required behavior:
- Cashier scan box must stay focused.
- Scanner suffix should be Enter.
- Raw input should be trimmed.
- Unknown barcode opens Fast Add Product.

Avoid in V1:
- Direct USB HID API
- Bluetooth special handling
- Scanner driver dependency
- Complex device enumeration

Why:
Keyboard-wedge mode is the simplest and most reliable for small stores.

---

# 2. Receipt Printer

V1 decision:
Support ESC/POS thermal receipt printer.

Preferred:
- LAN/Ethernet thermal printer

Fallback:
- Windows-installed printer

Why LAN first:
- Can print to IP address and port.
- Less driver pain than USB.
- Easier to test with raw ESC/POS commands.

Default LAN port:
- 9100

---

# 3. Printer Adapter Design

Interface:
IReceiptPrinter

Implementations:
- EscPosTcpPrinter
- WindowsPrintPrinter
- FileReceiptPrinter

Purpose:
The app should not care which printer type is used.

ReceiptService calls:
- IReceiptPrinter.PrintAsync(receiptText)

---

# 4. FileReceiptPrinter

Purpose:
Development and testing.

Behavior:
- Saves receipt text to a .txt file.
- Does not need physical printer.

Use:
- Early development
- Debugging
- Client demo without hardware

---

# 5. Printer Failure Rules

If printing fails:
- Sale remains saved.
- Print job marked FAILED.
- User sees warning.
- User can reprint receipt.

Never:
- Roll back completed sale just because printer failed.
- Lose receipt snapshot.
- Create duplicate sale on reprint.

---

# 6. Cash Drawer

V1:
Optional / not required.

Future:
- Trigger via receipt printer ESC/POS pulse command.

Do not block v1 on cash drawer.

---

# 7. Hardware Setup Checklist

For client deployment:
- Windows PC or laptop
- Barcode scanner in keyboard mode
- ESC/POS thermal printer
- Printer paper width: 58mm or 80mm
- Stable power
- Local backup location
- Optional external drive for backups

---

# 8. Test Cases

Barcode scanner:
- Scan known barcode
- Scan unknown barcode
- Scan with Enter suffix
- Scan with weird spaces
- Manual barcode typing

Printer:
- Test print
- Print receipt
- Reprint receipt
- Printer offline
- Wrong printer IP
- Paper out scenario if detectable

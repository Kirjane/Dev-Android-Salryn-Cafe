# PRE-BETA-G — Printer Hardening Report

## 1. Branch / base

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-G-PRINTER_HARDENING` |
| Base | `KIRCH-PRE-BETA-F-SCANNER-FIRST-CHECKOUT` @ `6ae2255feb2c9a8e54b363aecc2e0a98c6949f42` |
| Final SHA | `b1956500e230b64bbd31bb85127f92901d07d50a` |

## 2. Decision

**`PRINTER_HARDENING_PASSED`**

## 3. Delivered

- Store Settings: **Windows print queue only** copy; exact printer name helper; **Test print** button
- Explicit non-claims: No ESC/POS, cash drawer, cutter, thermal-profile
- Print/reprint non-mutation stated in UI
- Automated behavioral proofs via `PosPrinter1WindowsPrinterWiringChecks` + `PosM2PrinterReprintFailureChecks` (disabled, missing name, simulated failure, sale saved, snapshot reprint, no ledger mutation)

## 4. Gates

| Gate | Result |
| --- | --- |
| focused G smoke | PASS |
| full CoreSmoke | PASS |
| web build | PASS |

## 5. DO LATER

- Actual paper print proof
- Thermal-as-Windows-printer path (if any) hardware review
- Receipt paper appearance review

## 6. Non-claims

- Not hardware-proven paper output
- Not ESC/POS / drawer / cutter / thermal profile / hardware bundle
- Not BIR/official receipt output

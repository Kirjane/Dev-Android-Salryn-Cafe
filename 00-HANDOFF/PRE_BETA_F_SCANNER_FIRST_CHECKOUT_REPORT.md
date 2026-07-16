# PRE-BETA-F — Scanner-First Checkout Report

## 1. Branch / base

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-F-SCANNER-FIRST-CHECKOUT` |
| Base | `KIRCH-PRE-BETA-D-PORTABLE-BACKUP-IMPORT-EXPORT` @ `6f40eaa1b3a03e7ab7632be6e38a917b870046d1` |
| Final SHA | *(branch tip after push)* |

## 2. Decision

**`SCANNER_FIRST_CHECKOUT_PASSED`**

## 3. Scope delivered

### Host checkout

- Dedicated **Scan / search** field (existing, hardened)
- Enter **and Tab** suffix submit barcode
- Exact barcode match adds product; increments existing cart line (no duplicate line bug)
- Unknown barcode → Fast Add flow
- Focus returns to scan field after add / Fast Add
- Manual typing fallback retained

### LAN terminal checkout

- Dedicated **Scan / search** field as primary cashier path
- Host API `POST /api/remote-pos/cart/add-line-by-barcode`
- Exact host barcode lookup; unknown barcode safe message (no silent product create on LAN)
- Enter and Tab submit; focus returns to scan field
- Product GUID path moved under **Advanced** (not normal cashier path)

### Hardware boundary (honest)

- Keyboard-wedge only (keyboard input into focused field)
- **No** USB HID API, Bluetooth special handling, device enumeration, or scanner SDK
- **No physical scanner hardware proof** in this lane

## 4. Gates

| Gate | Result |
| --- | --- |
| build | PASS |
| `pre-beta-f-scanner-first-checkout` | PASS |
| full CoreSmoke | PASS |
| web build | PASS |

## 5. DO LATER

- Physical keyboard-wedge scanner hardware proof

## 6. Non-claims

- Not hardware-proven scanner support
- Not HID/Bluetooth/universal SDK support

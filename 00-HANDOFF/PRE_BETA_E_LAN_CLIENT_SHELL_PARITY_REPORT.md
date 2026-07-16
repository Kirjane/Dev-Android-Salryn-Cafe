# PRE-BETA-E — LAN Client Shell Parity Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-E-LAN-CLIENT-SHELL-PARITY` |
| Base | `KIRCH-PRE-BETA-B-POS-ACTIVATION-COPY-CLEANUP` @ `dcf08417362f8e158af671807a5632ce3ddde32a` |
| Final SHA | (branch tip after commit) |
| Protected SANDBOX | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| Protected INSTALLER-M1 | `6b9c46676390b8a86a989eca600cb02e406ce865` |

## 2. Stage 0 Lane B verification

| Gate | Result |
| --- | --- |
| SHA match | `dcf0841…` |
| `dotnet build` | PASS |
| **full CoreSmoke** | **PASS** |
| web build | PASS |
| Protected refs | Unchanged |

## 3. Lane B repair

**None.** Decision: **`POS_ACTIVATION_COPY_CLEANUP_FULLY_VERIFIED`**

## 4. Source studied

App shell, RemoteClientApp, RemotePosClientPage, RemoteClientLandingPage, useRemotePosClient, RemotePosOperatorSessionService, PRE-I1/LAN-M16 tests, Lane B activation pages.

## 5. Current LAN UI problem (before)

- After login, Remote POS was a card stack under a thin “Terminal client” header.
- Landing dual-promoted “Remote POS Client” and “Remote Read-Only Client” as equal choices.
- Felt like a separate diagnostic app, not Salryn POS.

## 6. Shell parity design

- **`TerminalShell`**: Host visual language (navy header/rail, StatusPills, footer).
- **Terminal Mode** + role badges.
- Nav: **POS Checkout only** (Cashier and Manager same safe surface; no invented manager admin pages).
- **`TerminalEntryFrame`**: pairing/login pre-shell with Salryn branding.
- Connected operator session wraps checkout in TerminalShell.

## 7. Cashier terminal shell

- After login: TerminalShell + POS Checkout.
- No Host admin nav.
- Server still limits login to Cashier/Manager.

## 8. Manager terminal shell

- Same shell/nav as Cashier (no fake manager-only host pages).
- Same remote-safe checkout APIs.

## 9. Admin/Owner LAN block

- Unchanged server: `{ "Cashier", "Manager" }` + host-local message.
- Landing copy states Admin/Owner remain host-local.

## 10. Remote Read-Only handling

- Removed as primary landing CTA.
- Gated under **Internal support surfaces** → “Open Remote Read-Only (support only)”.
- App still can render it for support.

## 11. Intentionally not changed

- Scanner (Lane F), printer (G), logo (C), backup portable (D).
- License validation / terminal license storage / pairing APIs.
- Lane B activation copy (Host License ID not asked of terminal users).
- Host AppShell / Terminal Management.

## 12. Automated tests/gates

| Gate | Result |
| --- | --- |
| `dotnet build` | PASS |
| `pre-beta-e-lan-client-shell-parity` | PASS |
| focused lic/pre-i1/web | PASS |
| **full CoreSmoke** | **PASS** |
| web build | PASS |
| Updated older LAN-M16 / V2 source guards for product language | Yes |

## 13. Manual proof

Source + full CoreSmoke. Physical second-device recheck **recommended** after shell change (not rerun in this session).

## 14. Post-write study

| Check | Result |
| --- | --- |
| No host admin nav on terminal shell | PASS |
| Cashier/Manager restrictions | PASS (server + UI) |
| Admin/Owner LAN block | PASS |
| Lane B activation copy | PASS |
| Remote APIs not loosened | PASS |
| No fake manager pages | PASS |
| No scanner/printer/logo/backup creep | PASS |
| Full CoreSmoke | PASS |
| Web build | PASS |

## 15. Remaining risks

1. Product entry still Product ID (Lane F scanner).
2. Manager has no extra remote-safe pages beyond checkout (by design).
3. Physical multi-device UX recheck recommended.

## 16. Final decision

**`LAN_CLIENT_SHELL_PARITY_PASSED`**

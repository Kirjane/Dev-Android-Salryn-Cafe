# LAN-M4 — Terminal Registry and Pairing Preparation

Status: **LAN-M4 complete** on `KIRCH-FREEHAND`.

## Implemented

- `SalrynTerminalRegistration` and `SalrynTerminalPairingInvite` models with distinct `TerminalId`, `StoreId`, and `HostId`
- SQLite tables `salryn_terminal_registrations` and `salryn_terminal_pairing_invites` (migration v10)
- `ITerminalRegistryService` and `ITerminalPairingPreparationService`
- Permissions: `StoreTerminal.View`, `StoreTerminal.Manage`, `StoreTerminal.PairingIssue`, `StoreTerminal.PairingRevoke`
- Host commands: `terminal.registry.list`, `terminal.registry.disable`, `terminal.registry.revoke`, `terminal.pairing.issue`, `terminal.pairing.revoke`, `terminal.pairing.list`
- Salryn Store page terminal registry and pairing preparation UI
- Same-PC loopback proof via `terminal-registry-pairing` infrastructure smoke

## Security

- Raw pairing codes/tokens are **not persisted**; only `PairingCodeHash` is stored
- Raw pairing secret is returned once at invite creation for operator display
- Expired, revoked, and consumed invites fail validation
- Disabled and revoked terminals fail host command acceptance when registered

## Not Implemented (by design)

- **No cross-PC LAN** — no remote client connection or execution
- No QR pairing
- No network discovery
- No remote pairing validation endpoint
- No remote cashier checkout or remote admin client
- No `0.0.0.0` or LAN IP bind
- No shared SQLite over network
- No client-side official journal writes or sequence allocation
- No mobile/cloud endpoints

## Store Host Doctrine

- Store Host owns terminal registry and pairing preparation
- `ClientTerminal` cannot manage registry or pairing
- Loopback transport may exercise host commands locally only
- `LanTransportEnabled` remains false

## Next Step (LAN-M5)

Proceed with controlled remote-client connection design or local-host endpoint hardening depending on repository readiness. Do not enable cross-PC transport until pairing consumption and terminal registry enforcement are wired to remote transport safely.

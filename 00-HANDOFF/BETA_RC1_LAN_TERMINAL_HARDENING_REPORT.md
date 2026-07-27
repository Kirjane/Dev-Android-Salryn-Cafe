# Beta RC1 LAN Terminal Hardening Report

## Scope

Branch: `KIRCH-BETA-RC1-INSTALLER-APP-SHELL`

Starting SHA: `60ca767a7734f493a3e11550951d27d02368e354`

This pass hardens remote checkout retry behavior and unifies the logged-in LAN
terminal shell with the host app shell. It does not expose host administration
routes to a terminal and does not change the trusted host-owned checkout rules.

## Backend hardening

- Replaced the thread-affine monitor held across asynchronous checkout work with
  per-request `SemaphoreSlim` serialization.
- Added migration 21 and a durable SQLite request ledger keyed by store,
  terminal, and client request id.
- Records a stable host-owned receipt before sale posting and reconciles an
  interrupted request against the authoritative sale row.
- Persists completed results for safe replay after API restart.
- Releases short-lived keyed serialization state after all renters finish,
  avoiding an unbounded process-lifetime request cache.
- Retains the existing host-side pricing, shift validation, sale transaction,
  stock decrement, and best-effort post-commit audit boundaries.

## LAN client retry behavior

- Keeps one client request id while checkout confirmation is unresolved.
- Stores pending confirmation state in session storage.
- Queries checkout status after an ambiguous submit response and after session
  restoration.
- Prevents the UI from silently creating a second request while the original
  sale may already exist.

## Terminal shell parity

- Host and logged-in LAN terminals now render the same
  `RoleAwareAppShell` implementation.
- Branding, header, sidebar behavior, responsive layout, user identity, role,
  and sign-out treatment come from the shared shell.
- Host navigation remains permission-filtered.
- Terminal navigation remains restricted to POS Checkout; host-only routes and
  administration actions are not mounted for the terminal role.
- Removed the duplicate terminal device-state clear action.

## Automated evidence

- `dotnet build Salryn.sln --no-restore`
- `npm run build`
- CoreSmoke: `lan-m16-remote-pos`
- CoreSmoke: `pre-beta-e-lan-client-shell-parity`
- CoreSmoke: `pre-beta-c-store-logo-branding`
- CoreSmoke: `pre-i-shell-sidebar-collapse-pos-usability`
- InfrastructureSmoke: `remote-pos-checkout-idempotency`
- InfrastructureSmoke: `remote-pos-post-commit-audit-safety`
- `git diff --check`

The idempotency infrastructure smoke uses an isolated development root and
proves concurrent same-key submission creates one sale, duplicate replay does
not decrement stock twice, and a fresh checkout service recovers the original
receipt and sale from SQLite.

## Manual QA still required

Automated gates do not replace a two-machine LAN walkthrough. Before promotion,
manually verify pairing, operator login, narrow viewport behavior, navigation,
checkout success, simulated response loss/reconnect, sign-out, and host terminal
revocation using a real host and terminal. No browser or live two-device proof
was performed in this pass.

## Decision

`BETA_RC1_LAN_TERMINAL_HARDENING_CODE_COMPLETE_MANUAL_QA_REQUIRED`

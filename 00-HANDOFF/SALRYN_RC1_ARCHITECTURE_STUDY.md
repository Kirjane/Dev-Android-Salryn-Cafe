# Salryn RC1 Architecture Study

## Document control

| Field | Value |
|---|---|
| Repository | `Kirch-Nairu/Salryn` |
| Studied branch | `KIRCH-RC1-INTEGRATE-SECURITY-LAN-HARDENING` tracking `origin/KIRCH-BETA-RC1-INSTALLER-APP-SHELL` |
| Studied HEAD | `c25824fc466e67c5303a4b443e140c765906bbf7` |
| Study date | 2026-07-28 |
| Decision | `BETA_RC1_ARCHITECTURE_STUDY_DOCUMENTED` |
| Scope | Architecture documentation and QA preparation only; no runtime code, installer rebuild, promotion, or public release |

## Evidence labels

This study deliberately separates five evidence states:

1. **Runtime-proven** - exercised by the combined integration gates or pre-existing installed-app evidence explicitly identified below.
2. **Source/compile-integrated** - present in source and included in a successful full build, but not fully exercised at runtime.
3. **Runtime-proof-pending** - implemented, but a required runtime gate has not been completed.
4. **Manual-QA-pending** - requires installed hardware, a second LAN device, browser interaction, or visual operator confirmation.
5. **Legacy/non-release** - present or compiled in the repository but not staged as the RC1 user-facing application.

## 1. Executive architecture verdict

The current RC1 release path is:

`Inno Setup installer -> Salryn.Desktop.WebView.exe -> WebView2 -> Salryn.Api.Local.exe -> API-served React dist -> SQLite under C:\ProgramData\Salryn`.

`Salryn.Desktop.WebView` is a thin native Windows host. It checks WebView2 availability, starts the adjacent local API executable in installed layouts, waits for `/health`, and navigates WebView2 to the local site. It deliberately contains no Salryn business-project references.

`Salryn.Api.Local` owns HTTP routing, local/LAN trust enforcement, server sessions, composition, SQLite initialization, application services, and React static-file delivery. React is a client of this API; it is not the durable authority for pricing, stock, sales, permissions, pairing, or checkout completion.

The LAN terminal loads the same React bundle from the host through the configured LAN listener, but `App.tsx` chooses the remote-client entry for non-loopback browser locations. The authenticated terminal uses the shared `RoleAwareAppShell` with only the POS Checkout navigation item. This is a UI boundary only; server-side terminal, handshake, session, origin, role, permission, and command checks remain authoritative.

`Salryn.App.WinForms` still compiles and has its own login/shell entrypoint, but the RC1 installer build script publishes only `Salryn.Api.Local` and `Salryn.Desktop.WebView`, then stages React `dist`. The old WinForms shell is therefore **legacy/non-release for RC1** and must not be presented as the current client architecture.

## 2. Top-level runtime and ownership map

| Layer | Primary components | Ownership |
|---|---|---|
| Distribution | `tools/installer/Build-SalrynBetaRc1Installer.ps1`, `installer/SalrynSetup.iss` | Builds/stages the Windows application and installer |
| Native entry | `Salryn.Desktop.WebView` | WebView2/runtime check, local API process lifecycle, local navigation policy |
| Web UI | `Salryn.App.Web` | Host console and LAN terminal presentation, state/query orchestration |
| HTTP boundary | `Salryn.Api.Local` | Routing, authentication, authorization, LAN trust, static React delivery |
| Composition | `Salryn.Composition.Local` | Production/development path selection and repository/service wiring |
| Domain | `Salryn.Core` | Checkout, receipt, inventory, licensing, reporting contracts and rules |
| Persistence | `Salryn.Infrastructure` | SQLite repositories/migrations, backup/restore, file paths, license storage |
| Durable state | `C:\ProgramData\Salryn` and `%LOCALAPPDATA%\Salryn` | Database, receipts, backups, exports, logs, config, license, WebView profile |

## 3. Installed startup flow

1. Inno Setup installs under `C:\Program Files\Kirjane Labs\Salryn POS`.
2. Desktop and Start Menu shortcuts target `Salryn.Desktop.WebView.exe`.
3. `Program.Main` calls `CoreWebView2Environment.GetAvailableBrowserVersionString`.
4. Missing runtime or `WebView2Loader.dll` produces a controlled warning and stops startup.
5. `LocalApiProcessManager.IsHealthyAsync` probes `http://127.0.0.1:5299/health`.
6. If unhealthy, installed layout starts the adjacent `Salryn.Api.Local.exe` with the local URL.
7. The manager polls health for up to 40 seconds.
8. Installed `DevHostConfig` resolves the production web URL to `http://127.0.0.1:5299/`.
9. `MainForm` creates WebView2 using `%LOCALAPPDATA%\Salryn\WebView2`.
10. `LocalWebOriginPolicy` permits local HTTP navigation only on ports 5173 and 5299 and blocks external/new-window navigation.
11. `Salryn.Api.Local` initializes the production database, maps HTTP endpoints, serves `dist`, and provides SPA fallback.
12. React loads and calls same-origin `/api/...` endpoints.
13. Closing the native form disposes the manager and best-effort terminates the API process that it started.

Installed startup, health, login, dashboard, checkout, receipt, shortcut, and WebView2Loader evidence in the pre-hardening installer report applies to the older installer artifact. It does not prove that the old setup includes HEAD `c25824f`; a rebuild and installed re-QA are required.

## 4. React and local API relationship

### 4.1 Base URL and delivery

Production React uses relative `/api/...` calls against the origin from which `Salryn.Api.Local` served `dist`. Vite development may use local port 5173 under the configured CORS allowlist. `index.html` receives no-cache headers; hashed JS/CSS assets are static.

### 4.2 Host entry and route protection

`App.tsx` chooses:

- `ConsoleApp` for loopback host locations.
- `RemoteClientApp` for non-loopback browser locations.

Host startup order is license status, bootstrap status, authentication, then permission-aware page rendering. `canAccessPage` and `getNavItems` prevent disallowed pages from rendering or appearing in the host shell. Server endpoint guards independently enforce loopback, authentication, permissions, and licensed features.

### 4.3 Major API groups

| Concern | Representative endpoints | Server boundary |
|---|---|---|
| Auth/session | `/api/auth/login`, `/api/auth/logout`, `/api/session` | Loopback guard; HttpOnly SameSite Strict cookie; server store |
| Products | `/api/products`, product detail/catalog/write endpoints | `Products.View` / `Products.Manage` |
| Inventory | `/api/inventory/low-stock`, `/movements`, `/stock-in`, `/adjustment` | Inventory view/adjust permissions |
| Host checkout | `/api/checkout`, host shift endpoints | `Sales.Create`, open shift, server product validation |
| Sales/receipts | `/api/sales/history`, `/api/sales/{id}`, `/api/receipts/{saleId}/print` | Sales history/create permissions |
| Backup/restore | `/api/backups`, verify, restore, import, download | `Backup.Manage` plus licensed feature |
| Audit/export | `/api/audit/logs`, `/export`, `/errors` | `Audit.View` |
| Store/settings | `/api/settings/store`, branding | `Settings.Manage` for settings writes/reads as mapped |
| Official setup | `/api/official/setup...` | `Settings.Manage`; host-local |
| Reports | Owner/traditional/production report endpoints | Report/auth boundaries; some advanced surfaces internal |
| LAN control | Host terminal management endpoints | Host-local terminal permissions |
| Remote POS | `/api/remote-pos/...` | Terminal trust, handshake, session, origin, role/permission matrix |

## 5. Host shell versus terminal shell

`RoleAwareAppShell` is the shared visual implementation for the authenticated host and terminal:

- branding/logo and store name;
- ready-state label;
- signed-in user and role;
- collapsible responsive sidebar;
- grouped navigation;
- sign-out confirmation;
- footer and optional banner/header action.

`AppShell` supplies the permission-filtered host navigation and pending terminal-request badge. The normal host navigation can include checkout, sales, dashboard, reports, products, inventory, users, audit, store settings, license, backup/restore, and terminal management according to permissions. Advanced report artifacts are additionally gated by an internal build flag.

`TerminalShell` supplies exactly one navigation item: `checkout`. Host routes are not mounted inside the terminal shell. Before authentication, `TerminalEntryFrame` provides pairing/login framing.

This separation is defense-in-depth, not the trust boundary. A terminal that manually requests host-local API URLs must still be rejected by `LocalApiLocalAccessGuard`, permission guards, and remote POS authorization.

## 6. LAN terminal flow

1. A terminal browser loads the host’s explicitly configured LAN URL.
2. `App.tsx` recognizes the non-loopback location and enters `RemoteClientApp`.
3. Terminal activation/license state is checked before normal client entry.
4. The terminal submits a pairing request including device identity.
5. Host terminal management reviews and accepts or rejects it.
6. Accepted pairing yields a one-time activation code and durable terminal credential/handshake records in SQLite.
7. Subsequent calls carry terminal ID and handshake session proof.
8. Remote operator login is limited to Cashier or Manager.
9. The server stores only a token hash, binds the 30-minute operator session to the server-observed origin, and revalidates terminal/license/handshake/session/permission on commands.
10. Capabilities explicitly list host-local-only actions. Settings, backup, user administration, LAN control, official setup, and normal host admin navigation are not terminal commands.
11. The terminal’s cart is held on the host and priced from persisted host product data.
12. Checkout uses a stable client request ID and the host status endpoint for reconciliation.

Automated source and functional smokes cover pairing/trust boundaries and checkout idempotency. Real two-machine browser behavior and visual restrictions remain manual-QA-pending.

## 7. Remote checkout idempotency and reconciliation

### 7.1 Client behavior

`useRemotePosClient` creates one `clientRequestId` for a pending checkout. It stores the ID, normalized host URL, and payment request in session storage under `salryn.remote-pos.pending-checkout.v1`. An ambiguous network response does not automatically create a new request ID. The hook calls `/api/remote-pos/checkout/status/{clientRequestId}` and restores pending confirmation after session restoration.

### 7.2 Server serialization

`RemotePosCheckoutService` rents a per-key `SemaphoreSlim` gate keyed by store, terminal, and client request. Concurrent same-key calls serialize asynchronously. The reference-counted gate is removed and disposed when all renters finish, avoiding a process-lifetime request-result cache.

### 7.3 Durable ledger

Migration 21 creates `remote_pos_checkout_requests` with:

- primary key `(store_id, terminal_id, client_request_id)`;
- request hash;
- pending/completed state;
- unique host-owned receipt number;
- optional sale ID, totals, change, message, and timestamps.

The server records a pending ledger row and stable receipt before posting. If a process interruption occurs, status lookup or retry checks the authoritative sale by receipt. A committed sale repairs/completes the ledger; no sale returns pending confirmation so the same request may be retried safely.

### 7.4 Proven properties

The isolated SQLite smoke proved:

- two concurrent same-key submissions create one completed sale;
- stock is decremented once;
- duplicate replay returns the original sale/receipt;
- a fresh checkout-service instance recovers the result from SQLite;
- post-commit audit failure does not make the committed sale retry as a new sale.

## 8. Checkout transaction flow

Both host and remote checkout converge on host-owned application/domain services.

1. Validate operator identity, request lines, quantities, and payments.
2. Require the operator’s open cashier session.
3. Reload each product from the host database.
4. Reject missing/inactive products and use authoritative host price, unit, barcode, cost, tax classification, and stock-tracking facts.
5. Allocate a host receipt number.
6. `TrustedPosCheckoutSaleBuilder` validates totals and builds:
   - completed sale;
   - immutable sale-item snapshots;
   - payments;
   - checkout stock movements;
   - client-facing receipt snapshot with cashier display name.
7. `PosCheckoutService` revalidates the open session.
8. `SqliteSaleWriteRepository.WriteCompletedSale` writes sale, items, payments, and stock movements inside one SQLite transaction.
9. Constraints/validation reject duplicates, invalid references, mismatched totals, insufficient payment, change mismatch, and negative tracked stock.
10. Host checkout reloads the persisted sale and writes a receipt text file after commit.
11. Receipt-file or audit failure does not roll back or duplicate the committed sale.

## 9. Inventory architecture

The product catalog carries active status, stock tracking, stock on hand, selling price, and low-stock threshold data.

- **Stock-in:** creates a positive `StockIn` movement and audit record.
- **Adjustment add/remove:** validates direction and quantity; removal cannot exceed tracked stock.
- **Checkout:** creates a negative `Sale` movement for tracked products in the same transaction as the sale.
- **Void:** uses `VoidSale` movements to restore stock under the void workflow.
- **Read models:** expose movement history and low-stock products.
- **Persistence boundary:** repositories re-check product activity/tracking and prevent negative stock; UI calculations are not authoritative.

## 10. Sales and receipt architecture

`sales`, `sale_items`, `payments`, and `stock_movements` are linked durable records. Sales history and detail read from SQLite snapshots rather than current product names/prices.

The persisted `receipt_snapshot` contains the client-facing `SALES RECEIPT`, receipt number, human cashier display name, item snapshots, and money values. Tests reject GUID/developer wording and later product-name mutation.

`FileReceiptWriter` writes the snapshot to `C:\ProgramData\Salryn\receipts` after commit using a sanitized receipt/sale filename. A file write failure is reported but the sale remains committed.

`PosReceiptPrintService` prints/reprints from the persisted snapshot. Its failure messages explicitly state that the sale was not modified. Reprint therefore consumes immutable evidence; it is not a sales mutation path.

Official document lifecycle endpoints are a separate host-local official/reporting lane and should not be confused with ordinary sales receipt file/print behavior.

## 11. Security hardening map

### Runtime-proven on the integrated candidate

- `Origin: https://evil.test` rejected with HTTP 403.
- `Sec-Fetch-Site: cross-site` rejected with HTTP 403.
- local `Origin: http://127.0.0.1:5299` passed the local boundary.
- Admin/Settings.Manage could access official setup.
- Cashier was rejected from official setup with HTTP 403.

### Source/compile-integrated

`LocalApiLocalAccessGuard` requires loopback, rejects remote bridge headers on host-local calls, rejects cross-site fetch metadata, and accepts only HTTP/HTTPS origins whose host is localhost or loopback.

`InMemoryLocalApiSessionStore` uses `ConcurrentDictionary`. Created sessions have an eight-hour server expiry; `Get` removes and rejects expired entries. The auth cookie is HttpOnly, SameSite Strict, path `/`, with an eight-hour maximum age.

### Runtime-proof-pending

The server-side eight-hour host-session expiry path has not been exercised with a valid runtime clock/expiry test. It must remain labeled `SOURCE/COMPILE INTEGRATED - RUNTIME PROOF PENDING`.

### Cashier/admin boundaries

React navigation filters pages by permissions, but API guards are authoritative. `Settings.Manage` protects official setup. Other host admin functions use specific permissions such as `Users.Manage`, `Audit.View`, `Backup.Manage`, and terminal-management permissions. Remote POS has a separate command matrix and does not expose normal host admin routes.

## 12. Backup and restore

Backups are rooted under `C:\ProgramData\Salryn\backups` and require `Backup.Manage` plus the licensed backup feature.

Creation uses SQLite online backup, then builds a controlled package directory with `salryn.db`, `manifest.json`, schema version, size, and SHA-256. Portable export contains only the manifest and database.

Verification checks:

- safe relative manifest path;
- exactly the expected database entry;
- file presence, size, and SHA-256;
- readable SQLite database;
- schema migration compatibility;
- required critical tables through the recorded migration version;
- no future migration version.

Portable import is capped at 100 MB and rejects corrupt, empty, nested, path-traversal, unexpected-entry, hash-mismatch, malformed-manifest, and future-schema packages.

Restore requires exact confirmation text `RESTORE SALRYN BACKUP`, repeats verification, creates a safety backup, clears connection pools/checkpoints WAL, and requires exclusive live-database access. If exclusive access is unavailable, it returns a restart/manual-step requirement rather than replacing unsafely.

## 13. Licensing

Host license state is stored at `%LOCALAPPDATA%\Salryn\Licensing\license.json`; replacement creates timestamped `.bak.json` files. The file store rejects rooted paths, traversal, separators, and invalid filenames.

The application embeds only a public RSA verification key and its expected SPKI SHA-256. Signature verification uses SHA-256 with PKCS#1 padding. The source explicitly states that private signing material remains in external LicensingOps secrets.

The installer and staging output must not contain:

- private signing keys or operational signing secrets;
- runtime `license.json`;
- installation identity state;
- `salryn.db`;
- QA/development databases or client data.

License status gates host entry, and feature enforcement separately controls functions such as backup and LAN terminal entitlements.

## 14. Data storage map

| Path | Purpose | Update/uninstall policy |
|---|---|---|
| `C:\ProgramData\Salryn\data\salryn.db` | Production SQLite database | Preserve; never ship in installer |
| `C:\ProgramData\Salryn\backups` | Controlled/safety/portable backups | Preserve |
| `C:\ProgramData\Salryn\exports` | Audit/report exports | Preserve |
| `C:\ProgramData\Salryn\logs` | Runtime logs | Operational |
| `C:\ProgramData\Salryn\receipts` | Post-commit receipt text files | Preserve |
| `C:\ProgramData\Salryn\config` | Runtime/LAN configuration files | Preserve |
| `%LOCALAPPDATA%\Salryn\Licensing` | Host license, backups, installation identity | Preserve; never stage |
| `%LOCALAPPDATA%\Salryn\WebView2` | Native host WebView profile/cache | Per-user runtime |
| `%LOCALAPPDATA%\Salryn\Reports\NativePdf` | Native PDF report output | Preserve by data contract |
| `C:\Program Files\Kirjane Labs\Salryn POS` | Replaceable installed binaries/assets | Removed/replaced by installer |

Development uses `<repo>\.salryn-dev`; isolated smokes can redirect through `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT`.

## 15. Installer and package architecture

`Build-SalrynBetaRc1Installer.ps1`:

1. optionally builds React;
2. publishes framework-dependent `Salryn.Api.Local`;
3. publishes `Salryn.Desktop.WebView`;
4. merges the native shell into staging;
5. stages `WebView2Loader.dll` beside the executable and under runtime RID paths;
6. copies React `dist` and branding;
7. writes a staging stamp;
8. optionally invokes Inno Setup;
9. computes a setup SHA-256 sidecar.

`SalrynSetup.iss` installs the entire staging tree under Program Files, creates shortcuts to `Salryn.Desktop.WebView.exe`, preserves ProgramData on uninstall/upgrade, checks WebView2 registration, and runs Microsoft’s Evergreen bootstrapper if required.

The public release asset is the compiled setup executable plus its new SHA sidecar and updated release documentation.

### Critical old-artifact warning

SHA-256 `9f6e583ca5d3d8a4754b476849ed9590595535fc93aba19054d44ac278834d6c` identifies the older pre-hardening installer. It does **not** include integrated runtime HEAD `c25824f`. If the integrated candidate is accepted:

- rebuild the installer from `c25824f` or its accepted descendant;
- rerun forbidden-content and installed-app checks;
- compute a new SHA-256;
- update manifest/README/release notes;
- publish only the rebuilt asset.

## 16. Legacy and non-release surfaces

`Salryn.App.WinForms` remains a buildable Windows executable with its own database initialization, authentication form, and `SalrynShellForm`. It shares Core/Infrastructure concepts and remains useful as historical/legacy code and possibly test support.

It is **not** published or copied by `Build-SalrynBetaRc1Installer.ps1`; `SalrynSetup.iss` shortcuts target `Salryn.Desktop.WebView.exe`. Therefore:

- current RC1 user entry is Desktop WebView + React + Local API;
- old WinForms checkout/shell screenshots or code must not be treated as current RC1 UX;
- its presence in `Salryn.sln` does not make it a release surface;
- future removal/refactoring is outside this documentation-only task.

Smoke tools, DevData, checkout host smoke, build staging, and `.salryn-dev` are also non-client verification/development surfaces.

## 17. Evidence and remaining gates

### Already proven on integrated code

- `git diff --check`;
- full solution build;
- React production build;
- focused LAN/shell CoreSmoke gates;
- isolated SQLite concurrent checkout/idempotency/restart recovery;
- post-commit audit-failure safety;
- hostile Origin and cross-site fetch rejection;
- local Origin allowance;
- Admin official-setup allowance;
- Cashier official-setup rejection.

### Runtime proof pending

- server-side host session expiry.

### Manual QA pending

- installed host launches from the rebuilt installer;
- real second device pairs over LAN;
- terminal checkout page loads and remains usable;
- terminal cannot use settings, backup, users, LAN control, official setup, or admin-report routes;
- ambiguous network failure retains the same request ID and reconciles status;
- retry does not duplicate the sale or inventory decrement;
- host shell remains role-aware;
- cashier/terminal shell remains restricted;
- visual LAN/browser behavior is confirmed.

### Release work pending

- acceptance of combined runtime candidate;
- installer rebuild;
- forbidden-content scan;
- installed upgrade/fresh-install QA;
- new SHA-256;
- README/manifest/release-note update;
- public release publication.

## 18. Release-control conclusion

The repository architecture is coherent around a host-owned local API and SQLite database, a WebView-hosted React console, and a separately authorized LAN terminal surface. The integrated security and LAN changes are present at `c25824f`. They do not authorize reuse of the old setup artifact.

Decision after documentation: `BETA_RC1_ARCHITECTURE_STUDY_DOCUMENTED`.

No runtime code, installer, public release, protected ref, or promotion action is part of this study.

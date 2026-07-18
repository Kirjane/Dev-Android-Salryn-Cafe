# PRE-I Action Notification System Report

## 1. Branch / base / final SHA

- Branch: `KIRCH-PRE-I-ACTION-NOTIFICATION-SYSTEM`
- Base branch: `KIRCH-PRE-I-LAN-SETUP-UX-CLARITY`
- Base SHA: `beca543273570a2dc85b106ad415f5ed73c1577d`
- Final implementation SHA: `e8f1e8400e0c5b039195becefb5596af7782470f`
- The delivery summary records the final branch SHA after this report commit.

## 2. Notification infrastructure added

- A dependency-free `NotificationProvider` owns the app-wide notification queue.
- `useNotifications` exposes `notify.success`, `notify.error`, `notify.warning`, and `notify.info`, plus dismissal.
- `NotificationViewport` renders above both Host and remote-client app modes.
- The responsive viewport is top-aligned, full-width with safe margins on mobile, and top-right with a bounded width on desktop.
- The visible stack is capped at four notifications.
- Success/info auto-dismiss after 4-4.5 seconds; warning/error stay for 7-9 seconds. Every notification has a manual close button.
- The viewport uses `aria-live="polite"`; error notifications use `role="alert"`.
- Notification text has no heavy animation. A safe-description helper rejects long or internal-looking exception text before it reaches a toast.

## 3. Actions wired

- License: Machine Code copy, install, replace, rejection/import failure, and terms feedback.
- Terminal Management: terminal-link copy; LAN start/stop/repair; Terminal POS allow/stop; join-code creation; pairing accept/reject; terminal disable/enable/remove; clear operator sessions; license-request copy.
- Backup & Restore: create, import, verify, invalid/non-restorable verification, wrong restore phrase, restore success/failure.
- Products: create/update, validation/save failure, deactivate, Product Catalog Stock-In and adjustment.
- Inventory: Stock-In, adjustment, validation, and API failure.
- Checkout: shift open/close, Fast Add, product/shift constraints, sale completion/failure, receipt print success/unavailable/failure.
- First-run setup: validation, completion, and failure.
- Sales & Receipts: reprint success/unavailable/failure and sale void success/failure.

No backend routes, API payloads, sale/payment/stock rules, printer implementation, license validation/signing, database paths, or LAN/pairing semantics changed.

## 4. Success notifications

Success results include License installed/replaced, Product created/updated, Stock-In completed, Stock adjustment completed, Sale completed, Receipt printed/reprinted, Backup created/verified/imported, Restore completed, LAN access started, Terminal POS access allowed, join code created, pairing accepted, terminal enabled/removed, setup completed, and sale voided.

Notifications are emitted only after the corresponding API or mutation success callback returns. Sale completion says the receipt was saved; it does not claim printing succeeded. Printer success is notified separately from sale commit.

## 5. Error notifications

Concise failure notifications cover license rejection/import, product save, stock writes, sale commit, printing/reprinting, backup create/import/verify/restore, setup, LAN actions, pairing actions, terminal lifecycle actions, and clipboard failures.

Existing friendly domain-error translators remain in use. The notification error helper bounds descriptions and filters stack traces, SQL/SQLite wording, framework names, file/line details, and other internal-looking text. Existing inline errors remain available for persistent context.

## 6. Warning / info notifications

- Warning: terms required, validation failures, unavailable stock movement, sale not ready, printer unavailable, LAN access stopped, Terminal POS access stopped, and terminal disabled.
- Info: Machine Code copied, Terminal link copied, pairing request rejected, operator sessions cleared, terminal license request copied.

Confirmations and notifications remain separate: destructive/disruptive ConfirmDialogs still occur before the LAN or terminal mutation; the notification reports the confirmed result afterward.

## 7. Pages changed

Product/runtime source:

- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/ui/Notifications.tsx`
- `src/Salryn.App.Web/src/components/ui/index.ts`
- `src/Salryn.App.Web/src/utils/notificationError.ts`
- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `src/Salryn.App.Web/src/pages/BootstrapSetupPage.tsx`
- `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx`

Focused proof and callback-aware source-guard updates:

- `tests/Salryn.Tests/PreIActionNotificationSystemChecks.cs`
- `tests/Salryn.Tests/PreILanSetupUxClarityChecks.cs`
- `tests/Salryn.Tests/PreICodeProvenClientSurfaceGuards.cs`
- `tests/Salryn.Tests/LanM16RcBEntryBoundaryChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## 8. What remains inline-only and why

- Form field validation and dialog validation stay inline so the operator can see which active form needs correction; key submit attempts also notify.
- Persistent license, backup/restore, checkout, printer, terminal, and stock status Alerts remain because they carry durable context that should not disappear with a toast.
- Initial page/query loading failures remain in `ErrorState` or inline Alert. They are page-state failures rather than an operator action result.
- Informational state such as current receipt details, backup metadata, terminal readiness diagnostics, and movement history remains inline because it is reference content.
- Some disabled-button prerequisites remain inline-only until an action is possible; the lane does not make invalid actions executable merely to produce a toast, except the wrong restore phrase path, which now has explicit feedback.

## 9. Tests / gates

Final results:

- `dotnet build Salryn.sln` - PASS, 0 warnings, 0 errors.
- `pre-i-action-notification-system` - PASS.
- `pre-i-lan-setup-ux-clarity` - PASS.
- `pre-i-license-page-minimalist-rebuild` - PASS.
- `pre-i-code-proven-qa-hardening` - PASS.
- `pre-i-dev-state-reset-truth` - PASS.
- `pre-i-p1-copy-guard-honesty` - PASS.
- `pre-i-automated-qa-proof` - PASS.
- Additional `lan-m16-remote-pos` boundary gate - PASS.
- Full `Salryn.Tools.CoreSmoke` - PASS.
- `npm --prefix src\Salryn.App.Web run build` - PASS.
- `git diff --check` - PASS.

The first broad run exposed old source assertions that assumed mutation calls had no React Query callbacks and that App returned Host/remote modes without a provider wrapper. Those assertions were repaired to preserve their payload, confirmation, and entry-boundary proofs. The final focused and full runs pass.

The production web build emits only the known Vite advisory for a minified chunk larger than 500 kB.

## 10. Manual QA checklist for Kirch

Automated build and structural source proof are complete. Browser/manual notification interaction was not executed in this lane. Kirch should verify:

1. Copy Machine Code shows a top notification.
2. Bad license import shows an error notification.
3. Product create shows success and Stock-In guidance.
4. Stock-In or adjustment shows success.
5. Sale completion shows success without claiming print success.
6. Receipt print/reprint shows the correct success, unavailable, or error notification.
7. Backup create shows success.
8. Wrong restore phrase shows explicit error feedback.
9. LAN access start/stop shows result notifications after confirmation where applicable.
10. Terminal link copy shows an info notification.
11. Terminal disable/enable/remove shows result notifications after confirmation.
12. Notifications stack cleanly, auto-dismiss at the expected pace, and close manually.
13. At mobile width, the stack does not permanently cover primary controls.

## 11. Remaining risks

- Visual placement, timing feel, screen-reader behavior, rapid multi-action stacking, and mobile obstruction remain manual QA items because this repo has no React component/browser harness.
- The focused gate is structural source proof; it does not simulate clicks or timers.
- The existing Vite chunk-size advisory remains; bundle redesign was outside scope.
- Receipt/printer notifications reflect adapter responses only and do not claim physical paper output.
- No package/I1, production activation, BIR/eSales, hardware certification, cloud activation, or branch promotion was performed or claimed.

## 12. Final decision

`PRE_I_ACTION_NOTIFICATION_SYSTEM_REPAIRED_AND_PASSED`

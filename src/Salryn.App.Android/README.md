# Salryn Cafe — Native Android Operational Cashier (`Salryn.App.Android`)

## Current Verified Architecture
`Salryn.App.Android` (`ph.kirjanelabs.salryncafe`) runs on the shared canonical Cafe SQLite schema through `MobileProductionComposition` (`files/SalrynCafe/salryn-cafe.db`, reconciled Cafe schema v43). The legacy prototype `mobile_*` business ledger is excluded from production runtime and preserved only under `tests/Salryn.Cafe.I6BTests/Prototypes` for historical provenance.

### Supported & Verified Capabilities (40/40 `Salryn.Cafe.I6BTests` PASS)
- **Fail-Closed Device Enrollment & Lease Guard:** `ProtectedMobileEnrollmentWriter` / `ProtectedMobileEnrollmentReader` (`salryn.cafe.approved_enrollment.v1`) requires an independently pinned server public key, `Salryn.Cafe` product identity, scope/epoch match, and `MobileOfflineLeaseGuard` (UTC + `ElapsedRealtime` + `BootCount` anti-rollback). Unauthenticated lease responses never pin a server key.
- **Independent Operator Authorization:** Operator login and current-permission revalidation are separate from device trust. Deactivation or permission revocation blocks mutations immediately.
- **Canonical Cashier Workflows (`MobileCashierController` & `MainActivity`):**
  - Published menu with variants and required modifiers (catalog-authoritative pricing).
  - Order creation, preparation, and completion with Cash, AR, and partial Cash+AR tenders (non-Cash/AR tenders remain locked).
  - `operationId`-backed shift open/close (`MobileShiftOpenIntent` / `MobileShiftCloseIntent`, zero `Guid.NewGuid()` fallbacks).
  - Canonical refund request/resolve/commit and delivery creation/advancement.
  - Receipt and kitchen printing via `MobilePrinterTransportAdapter` with same-job retry; failed or `OutcomeUnknown` transport states never fabricate print success.
- **Durable Pending-Intent Recovery (`MobileCashierIntentStore`):** Retains original `ActorUserId` and `OperationId` before dispatch across restarts and writes an audit record (`MobilePendingIntentResolutionRecord`) before clearing rejected intents.
- **Sync Transport:** `HttpSyncOutboxTransport` enforces HTTPS, `X-Salryn-Product-Id: Salryn.Cafe`, device bearer auth, and `X-Salryn-Operator-Token`.

## Build & Verification Commands
```powershell
dotnet test tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj
dotnet build src/Salryn.App.Android/Salryn.App.Android.csproj -c Release
```
Hosted Android CI (`salryn-cafe-android.yml`) runs all 40 `Salryn.Cafe.I6BTests` and publishes a dual-ABI (`arm64-v8a` + `x86_64`) test-signed Release APK (`ph.kirjanelabs.salryncafe-Signed.apk`).

## Verification Boundaries (Honest Status)
- **Verified (Portable + Hosted CI):** 40/40 `Salryn.Cafe.I6BTests` PASS; signed Release APK builds cleanly.
- **Verified (Android API 35 `x86_64` Emulator):** APK installation, launch, and force-stop/restart recovery with `0` fatal crashes.
- **NOT RUN on Emulator UI:** Full interactive UI-driven checkout, AR, shift, offline, and reconnect flows on the emulator (covered canonically in `I6BTests` against real SQLite).
- **`HUMAN_REQUIRED`:** Physical Android tablet UX, real Bluetooth thermal printer output, and production release signing/deployment. Do not install test-signed APKs on production cashiers or live customer data.

## Device setup and transport wiring (Codex continuation)

The sign-in and cashier pages now expose **Device setup and synchronization**. Supply the administrator-approved HTTPS origin, tenant/store IDs, and independently supplied Central signing public key (SPKI base64). The app retains its asymmetric signing key and configuration through Android Keystore encrypted storage, submits the one-time join code, and requires administrator approval before challenge authentication. Normal TLS validation remains enabled. Only a signed, scope-matching lease verified against the independently supplied key is accepted. Revocation fences the local lease. Existing device scope cannot be silently replaced.

The setup page applies base bootstrap plus scoped Cafe menu and recipe publications, renews the device lease, and enrolls the operator for offline sign in only after successful Central operator authentication on the approved device. Runtime stores a scoped local password hash; raw passwords and Central access tokens are not retained. Reconnect requires Central credentials for the same local operator and sends only that actor's pending canonical envelopes. Validated Applied resolution receipts are persisted through the canonical outbox store; partial or NeedsReview outcomes remain pending. Reconnect also accepts and projects change-feed pages through the shared canonical stores.

**Acceptance status:** Native build and full cashier emulator E2E for this continuation remain pending actual execution. The Debug build may trust explicitly installed user CAs through Android network-security debug-overrides for disposable HTTPS acceptance; Release trusts system CAs only. TLS verification and independent signing-key verification remain enabled. These implementation statements are not acceptance or release approval.

# U1 Paid Add-on License Entitlement Implementation Report

Date: 2026-07-28

## Source branch

`KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

## Source base HEAD

`d63f4f982a5c842bfaf27a81aef690cffe8ce262`

This is the preservation commit containing the accepted U1 P0/P1 work and paid-add-on studies. It was pushed to the source feature branch before implementation.

## Source final HEAD

Implementation candidate is uncommitted on `d63f4f982a5c842bfaf27a81aef690cffe8ce262`. No implementation commit or push was authorized.

## LicensingOps repo path

`D:\SALRYN STUFF\Salryn.LicensingOps`

## LicensingOps branch

`KIRCH-LICOPS-M6-CROSS-REPO-ACTIVATION-PROOF`

## LicensingOps base HEAD

`f73bf74a6e68f835640d0ea3942f979f8fc823d9`

## LicensingOps final HEAD

Unchanged; implementation candidate is uncommitted.

## Decision

`U1_PAID_ADDON_LICENSE_ENTITLEMENT_IMPLEMENTED_MANUAL_QA_PENDING`

## Feature key

`ReceivablesUtangLedger`

## Business model

Optional separately quoted paid add-on for operational Accounts Receivable / customer credit tracking. It is not accounting compliance, BIR registration, official accounting, or tax reporting.

## Public docs updated

Yes, locally in `D:\SALRYN STUFF\Salryn-Point-of-Sale`:

- `README.md`
- `CHANGELOG.md`

They now say upcoming optional paid add-on, separately quoted, under development/manual QA, and not included in the current installer. `SHA256.txt` was not changed.

## License allowlist implemented

Yes. LicensingOps rejects unsupported feature names before signing and verifies feature policy on signed documents.

## License generator updated

Yes. Base Host remains U1-disabled by default. CLI and localhost operator UI require explicit U1 selection.

## Runtime entitlement implemented

Yes. The source product registers U1 as a known Host-only feature and uses the existing license enforcement decision path.

Development mode follows the existing explicit CoreSmoke/development convention: registered features are enabled only when explicit development licensing mode is active.

## Checkout entitlement enforced

Yes.

- Cash continues through base `Pos`.
- Any `AccountsReceivable` payment requires `ReceivablesUtangLedger`.
- Utang/Cash + Utang still require `Receivables.CreateCreditSale`.
- Authentication and license checks occur before operation-specific permission and mutation.
- Missing entitlement returns HTTP 403 with `Receivables.FeatureNotLicensed`.
- Existing checkout idempotency and customer-required domain checks remain in place.

## API entitlement enforced

Yes, for customer search/create/update, receivables summary, customer ledger, collections, adjustments, and collection void.

Required order is authentication, entitlement, then operation-specific permission. No fake empty U1 response is returned.

## Frontend entitlement enforced

Yes.

- Utang Ledger navigation requires enabled feature plus `Receivables.View`.
- Checkout Utang choices/customer query require enabled feature plus `Receivables.CreateCreditSale`.
- Direct receivables-page state is denied when the feature is absent.
- Terminal shell remains POS Checkout only and exposes no U1 navigation.

## Reports entitlement enforced

Yes. Base owner reports still load, while the Accounts Receivable section is omitted unless U1 is licensed. The backend does not query or fabricate an empty U1 summary when unlicensed.

## Receipt wording checked

Yes. Existing operational U1 receipt wording remains limited to customer/cash/Utang/balance/due-date concepts and retains the non-official receipt boundary. No compliance claim was added.

## Backup/restore behavior

Existing backup/restore continues to carry the complete SQLite database, including U1 tables. The entitlement implementation does not delete, migrate, or rewrite U1 data on license changes.

## Data preservation behavior

The runtime smoke proves data created before entitlement denial remains stored, becomes inaccessible through the U1 API in base state, and becomes visible again when U1 entitlement is enabled. Existing backup/restore U1 proof also passes.

## LicensingOps tests

- `npm run build`: PASS.
- `npm run test:u1-paid-addon`: PASS.
- Base Host excludes U1: PASS.
- Selected Host includes U1 and verifies: PASS.
- Unknown feature rejection: PASS.

## Source tests

- `u1-entitlement-gating`: PASS.
- `u1-accounts-receivable`: PASS, including HTTP entitlement transition, role denial, terminal denial, idempotency, receipt identity, aging, net cash, and backup/restore.
- `v2-host-checkout`: PASS.
- `pos-m6-stock-in-stock-adjustment`: PASS.
- `pos-surface1-role-page-lock-store-nav`: PASS.
- `pre-beta-e-lan-client-shell-parity`: PASS after updating its stale source assertion to the entitlement-aware navigation call.

## Gates

- `dotnet build Salryn.sln --no-restore`: PASS.
- Web production build: PASS.
- LicensingOps TypeScript build: PASS.
- Focused smoke set: PASS.
- `git diff --check`: PASS in source, LicensingOps, and public documentation repositories.

## Remaining P2/P3

- Operator UI requires visual/manual proof.
- Admin add-on status is visible only through existing license feature/status surfaces; no new upsell polish was added.
- `Receivables.Export` remains reserved/unused permission vocabulary.
- Public price and existing-client grandfathering remain undecided.

## Questions for Kirch

1. Should existing customers receive U1 free, be grandfathered selectively, or be separately quoted?
2. Should public wording remain “separately quoted,” or later publish a price?
3. Should the existing License page receive an explicit locked add-on status card in a separately authorized UI task?

## Manual QA still required

- LicensingOps checkbox defaults off and issues correct disposable test feature lists.
- Installed Host launch with a base signed Host license.
- Cash checkout under base Host license.
- Installed Host launch with a U1-enabled signed test Host license.
- Utang and Cash + Utang checkout by permitted user.
- Licensed but unauthorized cashier denial.
- Collections, ledger, and report visibility by role.
- Base-license U1 navigation/control hiding.
- License replacement/downgrade/re-enable data visibility.
- Host/cashier visual inspection.
- Real terminal remains U1-free over two-machine LAN.

## Installer rebuild required

Yes, only after Kirch accepts this runtime candidate and separately authorizes release staging.

## Release staging

Not performed. No installer commands, package handoff, GitHub Release handoff, SHA update, or release asset was prepared.

## Protected refs

- `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

## Promotion

None.

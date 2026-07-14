# POS-SURFACE2 Dashboard / Advanced Investigation Report

Date: 2026-07-15

Branch investigated: `KIRCH-POS-PRINTER1-WINDOWS-PRINTER-WIRING`

Mode: report-only investigation. No code, UI, API, migration, test, printer, backup, licensing, installer, beta, or branch-promotion work was performed.

Status: `REPORT_ONLY_READY_FOR_KIRCH_REVIEW`

---

## Scope

Investigated current branch surfaces only:

- Dashboard / Owner Reports relationship
- Advanced navigation group
- Host LAN Control
- Official Setup
- Traditional Reporting
- Production Reports

This report intentionally does not mutate any runtime behavior.

---

## 1. Dashboard / Owner Reports

### Findings

Dashboard already pulls owner report summary through `fetchOwnerReportsSummary`.

Evidence reviewed:

- `DashboardPage.tsx` imports `fetchOwnerReportsSummary`.
- Dashboard creates a separate `ownerReportsQuery` with key `dashboard-owner-report-summary`.
- The query is enabled only when the session is authenticated and `canOpenReports` is true.
- Dashboard uses owner summary data for Today Sales, Transactions, Low Stock, and Voided Today when available, with dashboard summary fallback values.
- Dashboard shows an `Owner report summary` card and leaves detailed tables in Owner Reports.
- Dashboard shows a warning if owner report summary fails while core dashboard metrics remain loaded.

Owner Reports remains a separate navigation item.

Evidence reviewed:

- `navigation.ts` still declares `{ id: "owner-reports", label: "Owner Reports", group: "Manager" }`.
- `App.tsx` still renders `OwnerReportsPage` for `owner-reports`.
- Dashboard still opens the separate page with `onOpenReports={() => onNavigate("owner-reports")}`.
- `OwnerReportsPage.tsx` is a dedicated read-only detailed reporting page.

### Permission / gating review

Current gating is safe enough for the existing structure:

- Dashboard is authenticated manager/admin-style host UI.
- Owner report summary query is only enabled when `canOpenReports` is true.
- `owner-reports` requires `Reports.View`.
- Dashboard only shows the `Open Owner Reports` action when `canOpenReports` is true.

Do not merge Owner Reports into Dashboard unless the same `Reports.View` permission boundary remains intact for detailed report tables.

### Recommendation

Recommended direction: **keep separate sections**.

Do not fully merge Owner Reports into Dashboard. Dashboard should remain the operational summary surface; Owner Reports should remain the detailed read-only reporting surface.

Recommended rename: **Owner Reports -> Detailed Reports** or **Store Reports**.

Best option for current product clarity:

`Owner Reports` -> `Detailed Reports`

Reason:

- Dashboard already has the summary.
- The separate page contains detailed tables: daily/weekly/monthly, top products, cashier totals, category totals, low stock, inventory value.
- `Owner Reports` sounds role-specific and can confuse Manager/Admin role boundaries.
- `Detailed Reports` describes the page without implying a separate owner-only entitlement model.

Do not rename by code in this lane. This is an investigation-only recommendation.

---

## 2. Advanced tab inventory

Advanced nav currently lists:

- Host LAN Control
- Official Setup
- Traditional Reporting
- Production Reports

Advanced group copy says it contains host setup, reporting utilities, and LAN controls, and also states Backup & Restore UI is planned but not exposed.

---

## 3. Advanced page classifications

### A. Host LAN Control

Path reviewed: `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`

Classification:

- Client-facing now: **No**
- Admin-only: **Yes / host-operator admin surface**
- Compliance/future: **No**
- Dev-only / should hide: **No, but should remain permission-gated**
- Risky wording: **Low**

Current purpose:

- Host-local controls for LAN connection, pairing, terminal trust, remote POS runtime, pairing approvals, terminal visibility, and troubleshooting.
- Page copy explicitly says remote clients cannot open the page.
- It is guarded by StoreDeployment / StoreTerminal permissions through page gating.

Risk notes:

- No BIR-ready, official receipt ready, eSales production ready, or compliance-guaranteed wording found in this page.
- `Ready` language exists for LAN readiness states only. That is acceptable if understood as network readiness, not business/compliance readiness.
- Advanced diagnostics expose bind configuration and listen URLs. This is appropriate for host/admin operators, not cashiers or client devices.

Recommendation:

- Keep in Advanced.
- Keep permission-gated.
- Do not expose to remote clients.
- No rename required.

### B. Official Setup

Path reviewed: `src/Salryn.App.Web/src/pages/OfficialSetupPage.tsx`

Classification:

- Client-facing now: **No**
- Admin-only: **Yes, but should be restricted further than normal settings for production use**
- Compliance/future: **Yes**
- Dev-only / should hide: **Recommended hide or clearly mark internal until compliance lane resumes**
- Risky wording: **Medium to High because of label density, despite strong disclaimers**

Current purpose:

- Internal future official-output configuration.
- Business/taxpayer profile.
- Machine/POS profile.
- Receipt profile.
- Document series setup.
- Official numbering preview.
- Integrity/spine status.
- Internal invoice projection.
- Dry-run packages.
- Source-sale read-only dry-run.
- Internal previews.
- Live posting candidate status.

Safety copy currently present:

- `Not compliance-ready`
- `Official output is not implemented`
- `does not make Salryn BIR-compliant`
- `Live posting blocked`
- `Internal dry-run only`
- `Not an official receipt or invoice`
- `No official number is issued`
- `No compliance claim is made`
- `Production unwatermarked legal issuance is not enabled`

Flags:

- The page name `Official Setup` is risky because a non-technical user may read it as production official receipt setup.
- Multiple sections use official/invoice/receipt/series/legal wording. The disclaimers are strong, but the surface is too compliance-heavy for normal operator UI.
- It includes metadata fields such as accreditation/PTU numbers. Even with optional metadata wording, this belongs in an internal/compliance setup lane, not normal client-facing UI.

Recommendation:

- Do not merge with Dashboard.
- Do not show to cashier/client flows.
- Keep behind Settings/Admin permission at minimum.
- Prefer hiding this page from general production navigation until the compliance lane is active again.
- If retained, consider renaming later to `Internal Compliance Setup` or `Official Output Lab` to reduce false production interpretation.

### C. Traditional Reporting

Path reviewed: `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx`

Classification:

- Client-facing now: **No**
- Admin-only: **Should be admin/report-only**
- Compliance/future: **Yes**
- Dev-only / should hide: **Recommended hide from normal production navigation**
- Risky wording: **High**

Current purpose:

- Host-only X/Z/EJ/eSales draft surface.
- Provides preview X-reading, finalize Z-reading, export EJ, and export eSales draft actions.

Safety copy currently present:

- `Does not submit to BIR`
- `Activation OFF`
- `Production official activation remains OFF`
- `eSales format is an internal draft`
- `authoritative BIR schema is unconfirmed`
- `Manual acceptance remains pending`

Flags:

- Page title `Traditional official reporting` is risky.
- Button `Finalize Z-reading` is risky because it sounds operationally final even though copy says host-only engineering finalize.
- `Export eSales draft` is acceptable only because it says draft and no BIR submit, but it still belongs behind a strict reporting/compliance permission.
- This page is not suitable for client-facing or ordinary cashier use.

Recommendation:

- Do not merge with Dashboard.
- Hide from standard production navigation until compliance/reporting acceptance is intentionally resumed.
- If retained later, rename to `Internal Traditional Reporting Drafts` or `Compliance Draft Reports`.
- Keep `Reports.View`, `OfficialReading.View`, and `OfficialEvidence.Export` gating or stronger.

### D. Production Reports

Path reviewed: `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`

Classification:

- Client-facing now: **No**
- Admin-only: **Yes / reporting-admin surface**
- Compliance/future: **Partly; report artifact pipeline overlaps compliance language**
- Dev-only / should hide: **Not purely dev-only, but should be renamed or hidden until product release posture is clear**
- Risky wording: **Medium**

Current purpose:

- Native PDF export readiness for host-local operators.
- Controlled report artifact generation.
- Report catalog.
- Dataset generation.
- Native PDF artifact export.
- Artifact metadata display.

Safety copy currently present:

- `Controlled artifacts only`
- `no Crystal activation`
- `No printer integration`
- `does not enable Crystal Reports, SAP runtime, printer integration, arbitrary output paths, cloud upload, BIR approval, eSales production transmission, or legal production submission`
- `No caller path input is exposed`

Flags:

- Page label `Production Reports` is risky because it sounds release-ready / production-ready.
- `Native PDF primary` is fine technically, but paired with `Production Reports` it may sound more final than the current release posture.
- Copy explicitly avoids BIR/legal/eSales claims, which is good.

Recommendation:

- Do not merge with Dashboard.
- Rename later to `Report Artifacts` or `Internal Report Exports` unless/until production reporting is accepted as a released product surface.
- Keep behind `Reports.View` / official reading permissions.
- Leave in Advanced only if the user understands it is internal/admin-oriented.

---

## 4. Readiness / false-claim scan

### BIR-ready flag

No page should be considered BIR-ready from this investigation.

Observed source copy repeatedly states that official output is not implemented, compliance is not claimed, BIR submission is not active, or production/legal issuance remains disabled.

### Official receipt / invoice ready flag

Official Setup and Traditional Reporting expose official receipt/invoice terminology, but they repeatedly state internal dry-run/preview only and not an official receipt or invoice.

Flag: **wording-sensitive, not production-ready**.

### eSales production-ready flag

Traditional Reporting includes eSales draft/export language but also states eSales format is internal draft and BIR schema is unconfirmed.

Flag: **not eSales production-ready**.

### Compliance guaranteed flag

No compliance-guaranteed claim found in reviewed surfaces.

The risk is not explicit false claim; the risk is that ordinary operators may misunderstand page labels such as `Official Setup`, `Traditional official reporting`, and `Production Reports`.

---

## 5. Recommended surface strategy

### Dashboard / reports

- Keep Dashboard as summary.
- Keep detailed reports separate.
- Rename `Owner Reports` to `Detailed Reports` in a future UI cleanup lane.
- Preserve `Reports.View` gating.
- Do not duplicate detailed report tables into Dashboard.

### Advanced pages

Recommended future cleanup order:

1. Keep `Host LAN Control` visible only to host/admin operators.
2. Hide or relabel `Official Setup` as internal compliance setup/lab.
3. Hide or relabel `Traditional Reporting` as internal compliance draft reports.
4. Rename `Production Reports` to `Report Artifacts` or `Internal Report Exports` unless release posture changes.

### Permission gating

Do not weaken gating.

Minimum recommended gates:

- Host LAN Control: StoreDeployment / StoreTerminal permissions.
- Official Setup: Settings.Manage plus stricter admin/compliance role if introduced later.
- Traditional Reporting: Reports.View plus official reading/evidence permissions.
- Production Reports: Reports.View plus official reading permission.

---

## 6. Investigation verdict

Dashboard already has a lightweight Owner Reports summary. Owner Reports should remain a separate detailed surface, with a future rename preferred over merging.

Advanced contains a mix of valid host/admin operations and compliance/future/internal reporting surfaces. The implementation contains many safety disclaimers and does not obviously claim BIR readiness, official receipt readiness, production eSales readiness, or guaranteed compliance. The main cleanup need is surface naming and visibility, not backend mutation.

No code change was made.

No PRINTER1 file was touched.

No USER-M1 repair branch was touched.

No Backup UI, Licensing, I1, installer, beta, or branch promotion work was started.

Status: `REPORT_ONLY_READY_FOR_KIRCH_REVIEW`

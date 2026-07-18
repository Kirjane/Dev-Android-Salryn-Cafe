# PRE-I Audit CSV Export Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-AUDIT-CSV-EXPORT` |
| Base branch | `KIRCH-PRE-I-AUDIT-TRAIL-COVERAGE` |
| Base SHA | `d5b45317541f77e7e1dd8888a513f3581859d398` |
| Final tip SHA | *(set at commit)* |
| Protected | No promotion; `KIRCH-SANDBOX` / `KIRCH-INSTALLER-M1` untouched |

## 2. Export endpoint added

| Item | Value |
| --- | --- |
| Method / path | `GET /api/audit/logs/export` |
| Content type | `text/csv; charset=utf-8` |
| Download name | `salryn-audit-YYYYMMDD-HHMMSS.csv` (local time) |
| Response headers | `X-Salryn-Audit-Export-Count`, `X-Salryn-Audit-Export-Total-Matching`, `X-Salryn-Audit-Export-Truncated` (`1`/`0`), `X-Salryn-Audit-Export-Max-Rows` |

Implementation path:

- `IAuditReadRepository.ExportAuditLogsAsync` → `SqliteAuditReadRepository`
- `OperatorReadService.ExportAuditLogsCsvAsync` + `AuditCsvFormatter`
- `ReadOnlyEndpointExtensions` maps the route next to existing audit list

## 3. Authorization behavior

- Same guard as audit list: `Audit.View` via `LocalApiPermissionGuards.RequirePermission`
- Unauthenticated → `401`
- Authenticated without `Audit.View` → `403`
- Admin (or any session with `Audit.View`) may export

## 4. Filters supported

| Filter | Query param | Behavior |
| --- | --- | --- |
| Search text | `search` | Same LIKE fields as list: action, entity type/id, user display name, reason |
| Date from | `from` | Inclusive lower bound on `created_at` |
| Date to | `to` | Inclusive upper bound on `created_at` |
| Error-logs tab | — | **Out of scope** — export is audit logs only; UI states error-log CSV is not included |

Ordering: `ORDER BY created_at DESC, id DESC` (newest-first, stable; matches list + tie-break).

## 5. CSV columns

Stable headers:

1. Date/Time (UTC ISO-8601 `O`)
2. User
3. Action
4. Entity Type
5. Entity ID
6. Reason
7. Details (sanitized optional metadata from `new_value_json`)

## 6. CSV escaping / safety behavior

- UTF-8 with BOM for Excel
- RFC-style quoting: fields with comma, quote, CR, or LF are quoted; quotes doubled
- Empty values emit empty fields (still keep column alignment)
- Details pass through `SqliteAuditReadRepository.SanitizeAuditDetails` (blank entire details when secret-like)

## 7. Export row limit

| Item | Value |
| --- | --- |
| Cap | **25,000** matching rows (`DefaultExportMaxRows` / `AuditCsvFormatter.ExportMaxRows`) |
| Truncation disclosure | Response headers + UI helper copy + warning notification when truncated |
| Silent truncate | **No** — headers and UI report exported vs total matching |

## 8. Frontend button / notification behavior

- Audit page (audit tab only): **Download audit CSV** near Refresh
- Disabled while exporting (`Exporting...`)
- Helper: exports current **filtered** audit trail, not only the visible page; discloses 25,000 cap
- Download: `arrayBuffer` → `File` → `URL.createObjectURL` → revoke after click
- Notifications:
  - Success: **Audit CSV downloaded**
  - Truncated success: same title with warning body (exported of total, limit)
  - Failure: **Audit export failed**

## 9. Secret-safety checks

Export does **not** ship:

- password hashes / password-bearing JSON
- signed license JSON / signature payloads
- private keys
- session tokens
- raw stack traces / exception dumps
- backup contents / backup byte blobs
- connection-string style filesystem paths (`Data Source=`)

Unsafe details are blanked (null Details cell) rather than partially redacted, to avoid leaking substrings.

## 10. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.Core/Repositories/ReadRepositoryInterfaces.cs` | `ExportAuditLogsAsync` |
| `src/Salryn.Core/ReadModels/InventorySystemReadModels.cs` | Export row/filter models |
| `src/Salryn.Infrastructure/.../SqliteLogReadRepositories.cs` | Export query, sanitize, stable order |
| `src/Salryn.Api.Local/Services/AuditCsvFormatter.cs` | CSV builder + filename |
| `src/Salryn.Api.Local/Services/OperatorReadService.cs` | Export orchestration |
| `src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs` | `GET /api/audit/logs/export` |
| `src/Salryn.App.Web/src/api/audit.ts` | `downloadAuditCsv` |
| `src/Salryn.App.Web/src/pages/AuditPage.tsx` | Button, copy, notifications |
| `tests/Salryn.Tests/PreIAuditCsvExportChecks.cs` | Focused gate |
| `tests/Salryn.Tests/PreIClientFacingProductPolishChecks.cs` | Require export button |
| `tests/Salryn.Tests/WebA3ShellDesignSystemChecks.cs` | Approved audit download surface |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register `pre-i-audit-csv-export` |
| `00-HANDOFF/PRE_I_AUDIT_CSV_EXPORT_REPORT.md` | This report |

## 11. Gates run

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | Passed |
| `pre-i-audit-csv-export` | Passed |
| `pre-i-audit-trail-coverage` | Passed |
| `pre-i-client-facing-product-polish` | Passed |
| `pre-i-action-notification-system` | Passed |
| `pre-i-code-proven-qa-hardening` | Passed |
| Full `Salryn.Tools.CoreSmoke` | Passed |
| `npm --prefix src/Salryn.App.Web run build` | Passed (known Vite chunk-size advisory non-failing) |
| `git diff --check` | Passed |

## 12. Manual QA checklist (for Kirch)

1. Open Audit page.
2. Confirm **Download audit CSV** is visible on Audit logs tab.
3. Apply search filter; download CSV; confirm only matching rows.
4. Apply date from/to; download; confirm date bounds.
5. Confirm row count can exceed one visible page (page size 25).
6. Open CSV in Excel/LibreOffice; confirm columns and escaping.
7. Confirm no password hashes, license JSON, private keys, or stack traces.
8. Confirm success notification.
9. (Optional) Force failure (stop API) and confirm **Audit export failed**.
10. Error logs tab: confirm no CSV export button / copy says not included.

## 13. Remaining risks

| Risk | Notes |
| --- | --- |
| Cap at 25k | Very large histories need multiple filtered downloads or a future async export job |
| Details blanked when any secret-like token present | Safe but may drop adjacent safe metadata in the same JSON blob |
| Error-log CSV | Still not implemented (intentional) |
| Browser download UX | Relies on standard anchor download; rare browser policies may still block |

## 14. Final decision

**PRE_I_AUDIT_CSV_EXPORT_PASSED**

Audit risk #2 (missing complete filtered audit CSV export) is closed on this branch. No package/I1 work. No branch promotion.

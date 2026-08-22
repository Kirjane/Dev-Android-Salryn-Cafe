# I1 Package Refresh Pre-Beta Report

## Decision

`I1_PACKAGE_REFRESH_PRE_BETA_BROWSER_PACKAGE_ONLY`

The controlled browser package passed package launch and isolated workflow proof. The existing WebView2 project remains a development host and was not claimed as a packaged desktop shell.

## Branch and provenance

| Item | Value |
| --- | --- |
| Branch | `KIRCH-I1-PACKAGE-REFRESH-PRE-BETA` |
| Base branch | `KIRCH-PRE-I-CODE-QUALITY-RELIABILITY-AUDIT` |
| Base SHA | `07c643f1d9f341132cdf19e80e8176e66b40e5a1` |
| Package source commit | `0f456fd6df459168e3b918d6f1f99f87653d7321` |
| PRE-I audit decision | `PRE_I_CODE_QUALITY_RELIABILITY_AUDIT_P2_ONLY` |

The final branch SHA is the tip containing this report and cannot be embedded in the same commit that creates it.

## Package identity

| Item | Evidence |
| --- | --- |
| Packaged executable | `Salryn.Api.Local.exe` |
| Publish model | Framework-dependent .NET 10 Release publish |
| Package folder | `C:\Users\kirch\Salryn\artifacts\i1-package\Salryn` |
| Web dist | `C:\Users\kirch\Salryn\artifacts\i1-package\Salryn\dist` |
| Archive | `C:\Users\kirch\Salryn\artifacts\i1-package\Salryn-I1-PRE-BETA.zip` |
| Archive SHA-256 | `813BFBD78428CF89B9A48224F87FCBDF84D33BDEC0F6D91923632E6412D04B9D` |
| Archive size | 37,103,855 bytes |
| Package file count | 71 |
| Package folder bytes | 77,944,265 |
| Package tree SHA-256 | `93466EE541D872767CA51DD0DE2F7DCC857841A2CD175A717D9FBB7CC66BA883` |

Package launch command used for isolated proof:

```powershell
cd C:\Users\kirch\Salryn\artifacts\i1-package\Salryn
.\Salryn.Api.Local.exe --dev
```

Normal beta-package launch omits `--dev` and stores application data under the established production data roots. The `--dev` argument was used only to isolate this proof from production `ProgramData` state.

## Package repair made

- Changed the hosted browser title from the legacy `Salryn Ledger Console` to `Salryn POS`.
- Updated the existing I1 publish script to stamp the current lane, stage the package runbook and limitations, and create a SHA-256-addressed ZIP archive.
- No API business logic, license cryptography, checkout logic, LAN runtime behavior, printer behavior, terminal UI, installer, or WebView2 runtime code changed.

## Launch and hosted UI proof

The process actually executed from:

```text
C:\Users\kirch\Salryn\artifacts\i1-package\Salryn\Salryn.Api.Local.exe
```

| Check | Result |
| --- | --- |
| `GET /health` | PASS — HTTP 200, `status=ok`, `service=Salryn.Api.Local` |
| `GET /` | PASS — HTTP 200 from the packaged process |
| `/api/system/web-dist` | PASS — package `dist`, `indexHtmlExists=true` |
| Hashed JS asset | PASS — `/assets/index-CJrVIqEk.js`, HTTP 200, 616,848 bytes |
| Vite client marker | Absent |
| `localhost:5173` marker | Absent |
| Browser title after repair | `Salryn POS` |
| Visible browser sign-in screen | PASS |
| Visible authenticated Dashboard | PASS |
| npm/Vite dev server required | No |
| Visual Studio required | No |

## License proof

The existing valid signed Host license was first hashed and quarantined outside the package. The package then started with no active license file.

| Check | Result |
| --- | --- |
| Fresh status | Unlicensed; Machine Code returned |
| Machine Code | `SALRYN-MC-8939-3171-D262-7B9C` |
| Install existing valid signed Host license through packaged API | PASS — `Installed` |
| Status after install | Licensed Host, machine matched |
| License persistence after packaged-process restart | PASS — same license ID and machine match |
| Login/dashboard after restart | PASS |
| Tampered/wrong license behavior | Covered by the passing full CoreSmoke stack; crypto logic was not changed or reimplemented |

This was controlled reuse of an existing valid local beta license for package proof, not production activation or new license issuance. After proof, Kirch's original license file was restored with exact SHA-256 match `3044728E9D104378148ABB77471FCEBF52E6E494C392B56D78CCFA7C93C527CD`.

## Bootstrap, login, and session proof

| Check | Result |
| --- | --- |
| Fresh bootstrap status | `firstRunRequired=true` |
| First Admin creation | PASS in isolated proof DB |
| Login | PASS — authenticated Admin |
| Session | PASS — authenticated Admin session |
| Dashboard API | PASS |
| Browser login | PASS |
| Browser Dashboard visible | PASS |

No dev-only setup instruction was required in the hosted beta UI. The proof account and isolated DB were removed after evidence capture.

## POS, receipt, and reprint proof

| Check | Result |
| --- | --- |
| Product creation for isolated proof | PASS |
| Stock-in | PASS — `POS_M6_STOCK_IN_RECORDED` |
| Barcode/product lookup | PASS — one matching product |
| Shift open | PASS — `HostOperator.Shift.Opened` |
| Checkout | PASS — `Completed` |
| Receipt snapshot | PASS — saved |
| File receipt | PASS — written in isolated proof state |
| Reprint request | Safe disabled-printer result: `Receipt.PrintSale.DisabledUnavailable` |
| Sale count across reprint | 1 → 1 |
| Stock across reprint | 4 → 4 |
| Stock-movement count across reprint | 2 → 2 |

The mutation comparison proves the packaged reprint path did not create another sale or stock movement. Physical printer/paper output remains manual and was not claimed.

## Backup, restore, and audit proof

| Check | Result |
| --- | --- |
| Backup create | PASS |
| Backup list | PASS — created package present |
| Backup verify | PASS |
| Backup restore | PASS in the isolated package-proof DB only |
| Audit viewer/API | PASS — 7 rows visible to Admin |
| Audit CSV export | PASS — HTTP 200, 1,908 bytes, 7 exported rows |

No restore was attempted against Kirch's original development or production data.

## Original-state safety

Before package proof, the existing `.salryn-dev` tree and active local license were moved to a verified quarantine path outside the package. After proof:

- original development-state file count restored: 8;
- original development-state tree SHA-256 restored exactly: `C016B5ED8B00A56D00717AECF5636217AE6BB5E3D75D32C3C5449FACDFA69704`;
- original license SHA-256 restored exactly: `3044728E9D104378148ABB77471FCEBF52E6E494C392B56D78CCFA7C93C527CD`;
- temporary proof DB, proof license, logs, and harness were removed;
- port 5299 was left free.

## Package cleanliness

| Check | Result |
| --- | --- |
| `.git`, `.secrets`, `node_modules`, `.salryn-dev` paths | 0 |
| `license.json`, SQLite DB, proof log filenames | 0 |
| Private-key/secret text markers | 0 |
| Vite dev markers | 0 |
| Customer data or proof DB | None included |
| Local license or local app-data state | None included |
| Package runbook and known limitations | Included |

See `I1_PACKAGE_CONTENTS_MANIFEST.md` for the bounded contents manifest.

## Gates

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | PASS — 0 errors, 10 warnings |
| Full `dotnet run --project tools\Salryn.Tools.CoreSmoke` before package proof | PASS |
| Full CoreSmoke after package repair | PASS |
| `npm --prefix src\Salryn.App.Web run build` before package proof | PASS; non-failing chunk advisory |
| Web build after title repair | PASS; non-failing chunk advisory |
| `git diff --check` | PASS |

## Desktop shortcut, app shell, and icon decision

`DESKTOP_APP_SHELL_DEFERRED`

- The current `Salryn.Desktop.WebView` scaffold defaults to a Vite development URL and repo-based API startup. It is not package-safe without a separate bounded repair/proof lane.
- No desktop shortcut was created or claimed.
- No packaged product icon was created or claimed.
- The hosted UI now uses the required `Salryn POS` title and already carries `Salryn POS` / `Salryn by Kirjane Labs` product identity.
- Browser launch is the honest I1 pre-beta package model.

## Blockers and limitations

P0 blockers: none.

P1 beta blockers for the controlled browser package: none.

Accepted P2 limitations remain in `I1_PACKAGE_KNOWN_LIMITATIONS.md`, led by the lack of a host-local server-side checkout idempotency key. Installer, desktop shell/shortcut/icon, physical terminal, and physical printer proof remain outside this decision.

## Protected refs and promotion

- `KIRCH-SANDBOX` remained `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`.
- `KIRCH-INSTALLER-M1` remained `6b9c46676390b8a86a989eca600cb02e406ce865`.
- No branch promotion was performed.
- No production activation was performed.
- No BIR/eSales work or readiness claim was made.

## Recommended next

Controlled Kirch beta review of the browser package and archive. A future desktop-shell/shortcut lane may package the WebView2 host only after its dev-only startup assumptions are replaced and separately proven. The full MSI/installer lane remains distinct and unauthorized here.

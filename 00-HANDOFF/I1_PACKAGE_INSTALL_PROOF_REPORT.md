# I1 Package / Install Proof Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-I1-PACKAGE-INSTALL-PROOF` |
| Required base | `KIRCH-PRE-I1-LAN-TERMINAL-ACTIVATION-MANAGEMENT-BETA` |
| Starting SHA | `7ea411bed3925780f17cae19ccaa0c03bbcb3837` |
| Final decision | See section 21 |
| Protected `KIRCH-SANDBOX` | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (unchanged) |
| Protected `KIRCH-INSTALLER-M1` | `6b9c46676390b8a86a989eca600cb02e406ce865` (unchanged) |

This report cannot embed the SHA of the commit that contains itself. Use the branch tip after this report commit for the immutable final SHA.

LicensingOps used for test Host license only:

| Item | Value |
| --- | --- |
| Branch | `KIRCH-LICOPS-ISSUANCE-CONTRACT-HARDENING` |
| SHA | `10be2b3e03604ffb32f5e312f6569c38eacf486c` |
| POS repo changes | None |

## 2. SOL work studied

Studied before packaging:

- `00-HANDOFF/PRE_I1_LAN_TERMINAL_ACTIVATION_MANAGEMENT_BETA_REPORT.md`
- `00-HANDOFF/PRE_I1_CLIENT_SURFACE_LAN_LICENSE_CLEANUP_REPORT.md`
- `00-HANDOFF/PRE_I1_LICENSE_PAGE_PRODUCT_UX_POLISH_REPORT.md`
- `00-HANDOFF/PRE_I1_FRESH_LICENSE_FIRST_FLOW_REPORT.md`
- `00-HANDOFF/HARDEN_A9_INSTALLER_UPDATE_CONTRACT_STUDY_REPORT.md`
- `00-HANDOFF/CLEANUP_A1_OPERATIONAL_HARDENING.md`
- LicensingOps issuance contract hardening docs (read-only)
- Source: `LocalApiApplicationFactory` web dist resolution, `WebDistDiagnostics`, license/bootstrap endpoints, navigation model, package staging notes under `artifacts/installer` (reference only)

SOL LAN terminal beta status (user-attested):

- Code complete on `7ea411b`
- Physical second-device LAN proof **PASSED** by Kirch
- Working status entering I1: `LAN_TERMINAL_BETA_PROVEN_BY_CODE_SOL_AND_USER_DEVICE_PROOF`

I1 did **not** re-run the full physical second-device proof. Packaged Remote Terminal SPA routes and Host Terminal Management API were verified from package output.

## 3. Package / publish method used

**Chosen method:** web production build + `dotnet publish` of `Salryn.Api.Local` + stage `dist\` beside the published executable.

Script:

```text
tools/package/Publish-I1Package.ps1
```

Steps:

1. `npm --prefix src/Salryn.App.Web run build` (production Vite build + build stamp)
2. `dotnet publish src/Salryn.Api.Local -c Release -o artifacts/i1-package/Salryn`
3. Copy `src/Salryn.App.Web/dist` → `artifacts/i1-package/Salryn/dist`
4. Write `salryn-i1-package-stamp.txt`

**Not used for I1:** public MSI/WiX installer, auto-updater, Desktop WebView host as required entry, cloud activation.

**Why this method:** PRE-I1 API already serves React static assets; INSTALLER-M1 historically staged `dist\` beside `Salryn.Api.Local.exe`. Current PRE-I1 base only resolved repo-shaped `src/Salryn.App.Web/dist`, which is insufficient for package runtime. I1 repaired resolution and published a clean framework-dependent package folder.

## 4. Output path

```text
C:\Users\kirch\Salryn\artifacts\i1-package\Salryn
```

Contains:

- `Salryn.Api.Local.exe` (+ deps)
- `dist\index.html`, hashed JS/CSS, `salryn-build-stamp.txt`
- `salryn-i1-package-stamp.txt`
- No Vite `node_modules`, no source tree, no `.salryn-dev`, no LicensingOps secrets

## 5. Launch command from package

Dev-data isolation for proof (writes DB under repo `.salryn-dev`, not production ProgramData DB):

```powershell
cd C:\Users\kirch\Salryn\artifacts\i1-package\Salryn
.\Salryn.Api.Local.exe --dev
```

Then open:

```text
http://127.0.0.1:5299/
```

License file for this machine still uses the standard local license store:

```text
%LocalAppData%\Salryn\Licensing\license.json
```

## 6. API proof

From packaged process:

| Check | Result |
| --- | --- |
| Process path | `...\artifacts\i1-package\Salryn\Salryn.Api.Local.exe` |
| `GET /health` | `200` `{"status":"ok","service":"Salryn.Api.Local"}` |
| Listen URL | `http://127.0.0.1:5299` |
| Content root | package folder |

## 7. Web dist proof

Startup / `/api/system/web-dist`:

| Field | Evidence |
| --- | --- |
| `distPath` | `C:\Users\kirch\Salryn\artifacts\i1-package\Salryn\dist` |
| `indexHtmlExists` | true |
| script asset | `/assets/index-BhS-Y2ql.js` (200, ~548 KB) |
| CSS asset | `/assets/index-DFOcT0nS.css` |
| build stamp | present |
| Vite dev markers in asset | **false** (`localhost:5173` / `@vite/client` absent) |

## 8. Fresh Host activation proof

Fresh reset:

- Moved existing `.salryn-dev` to Kirch backup archive under Documents/Codex work folder
- Confirmed `.salryn-dev` absent before package launch

Fresh package flow:

1. `GET /api/license/status` → no license; machine code `SALRYN-MC-56DA-D28D-B387-9237`
2. SPA `/` served (Activate Salryn path available in client; “Activate Salryn” present in production JS)
3. LicensingOps CLI issued flat Host license (stdout only; no wrapper)
4. `POST /api/license/install` → `Installed`, validation Host/Licensed, machineMatched=true
5. Bootstrap status first-run required
6. `POST /api/bootstrap/first-admin` with `admin@kirch` / `StrongAdmin123!` → `201`
7. Login → authenticated Admin
8. Dashboard summary → `200`

## 9. License persistence proof

Stop packaged process → start again from same package folder with `--dev` (no re-import):

| Check | Result |
| --- | --- |
| License status after restart | Licensed Host, same `licenseId` `lic_host_20260716114120_ee9886ea` |
| `machineMatched` | true |
| Login after restart | `200` |
| Dashboard after restart | `200` |

## 10. Login / dashboard proof

| Check | Result |
| --- | --- |
| `POST /api/auth/login` | 200, role Admin |
| `GET /api/session` | authenticated |
| `GET /api/dashboard/summary` | 200 |

## 11. Backup proof

| Check | Result |
| --- | --- |
| Admin nav includes Backup & Restore | yes (production JS) |
| `GET /api/backups` after login | 200 `[]` (reachable, empty list on fresh DB) |

## 12. License page active-state proof

| Check | Result |
| --- | --- |
| `GET /api/license/status` after install | Licensed Host, features Pos/Reports/Users/Backup/Licensing |
| `GET /api/license/entitlements` | 200 with enabled features |
| Client label “Activate Salryn” | present in package JS for unlicensed UX path |

## 13. Terminal Management proof

| Check | Result |
| --- | --- |
| Host nav label Terminal Management | present in package JS |
| `GET /api/lan-control/status` (Admin session) | 200 |
| SPA route fallback for host shell | package serves index for client routes |

## 14. Remote terminal route proof

| Check | Result |
| --- | --- |
| `GET /remote-client` | 200 SPA index from package dist |
| `GET /terminal-activation` | 200 SPA index from package dist |
| Package JS contains Terminal client surface | true |
| Full physical second-device LAN retest | **Not re-run**; recorded as user-attested on PRE-I1 base |

## 15. No dev-server dependency proof

| Check | Result |
| --- | --- |
| Vite process required | no |
| Production asset contains `@vite/client` / `localhost:5173` | no |
| Package has `node_modules` | no |
| Static root logged | package `dist`, not Vite origin |

## 16. No source-working-tree dependency proof (runtime assets)

| Check | Result |
| --- | --- |
| Web dist path | package `...\i1-package\Salryn\dist` (not `src\Salryn.App.Web\dist`) |
| Package contains `src\` tree | no |
| Package binaries | self-contained under package folder (framework-dependent .NET runtime assumed installed) |
| Dev DB with `--dev` | may resolve repo root for `.salryn-dev` isolation only; not used for static assets |

## 17. Secret scan proof

Scanned package tree for private key PEM blocks, `.secrets`, `beta-signing-private`, LicensingOps ledger/secrets paths, generated LicensingOps licenses, secret-looking filenames.

| Check | Result |
| --- | --- |
| Private key content | none found |
| `.secrets` | absent |
| `*private*.pem` | 0 |
| LicensingOps material in package | absent |
| POS public verification key in binaries | expected / allowed |

**Decision on secrets:** not blocked.

## 18. Gates and results

### Build

| Command | Result |
| --- | --- |
| `dotnet build Salryn.sln` | Passed (after stopping stale Debug API lock) |

### Focused CoreSmoke

| Target | Result |
| --- | --- |
| `lic-closeout-hard-gates-and-terminal-proof` | Passed |
| `lic-beta-manual-host-terminal-activation` | Passed |
| `lic-lan-terminal-entitlement-enforcement` | Passed |
| `pre-i1-fresh-license-first-flow` | Passed |
| `pre-i1-lan-terminal-activation-management-beta` | Passed |
| `pos-b1-backup-restore-safety` | Passed |
| `web-a3-shell-design-system` | Passed |
| `web-a4-native-host-visual-fidelity` | Passed |
| `web-b0-read-only-operator-surfaces` | Passed |
| `lan-m16-remote-pos` | Passed |

Adjusted target name note: LAN/Remote POS focused target is `lan-m16-remote-pos` (not separate `remote-pos` / `terminal` aliases). PRE-I1 LAN beta aggregate target is `pre-i1-lan-terminal-activation-management-beta`.

### Full suite / frontend

| Command | Result |
| --- | --- |
| Full `dotnet run --project tools\Salryn.Tools.CoreSmoke` | Passed |
| `npm --prefix src\Salryn.App.Web run build` | Passed |
| `git diff --check` | Passed |

## 19. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.Api.Local/LocalApiApplicationFactory.cs` | Package-aware web dist candidates: `BaseDirectory/dist`, `wwwroot`, repo-shaped paths |
| `tests/Salryn.Tests/CleanupA1OperationalHardeningChecks.cs` | Source guards for package dist resolution |
| `tools/package/Publish-I1Package.ps1` | I1 publish script (not public installer) |
| `00-HANDOFF/I1_PACKAGE_INSTALL_PROOF_REPORT.md` | This report |

LicensingOps: **not modified**.

## 20. Remaining risks

1. **Not a public installer** — no MSI/shortcut/service lifecycle; operators launch published folder.
2. **Framework-dependent publish** — requires matching .NET runtime on the machine.
3. **License path vs DB path** — with `--dev`, DB is under `.salryn-dev`; license still uses LocalApplicationData licensing folder (by design). Production proof without `--dev` writes ProgramData DB and was intentionally avoided for safety.
4. **Physical second-device package retest not repeated** — PRE-I1 user attestation stands; package proved Host Terminal Management API + Remote Terminal SPA routes only.
5. **Report Artifacts** remains in client code behind `VITE_SALRYN_INTERNAL_SURFACES`; default production package build does not enable that flag, so it stays out of normal nav.
6. **No auto-update / cloud activation / BIR-RMO production claims**.

## 21. Final decision

**I1_PACKAGE_INSTALL_PROOF_REPAIRED_AND_PASSED**

Repair was required: PRE-I1 base could not resolve packaged `dist\` beside the published API. Resolution was ported to the INSTALLER-M1 package contract (package-first candidates) without promoting protected installer refs or shipping a public installer.

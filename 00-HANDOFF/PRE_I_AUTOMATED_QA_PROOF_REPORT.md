# PRE-I Automated QA Proof Report

## 1. Branch / base / final SHA

- Branch: `KIRCH-PRE-I-AUTOMATED-QA-PROOF`
- Base: `KIRCH-PRE-I-P1-COPY-GUARD-HONESTY`
- Base SHA: `173f187e1f8cf5078a1027ecf16227e017b68d3b`
- Tested implementation SHA: `bdea376` (`test: add PRE-I automated QA proof gate`)
- Final branch-tip SHA: supplied in the push handoff because this report is the final documentation-only commit.
- Protected refs were not checked out, reset, merged, or otherwise changed.

Two requested read-first files do not exist on the pinned base: `PRE_I_EXTREME_CODE_QUALITY_AUDIT_REPORT.md` and `PRE_I_EXTREME_CODE_QUALITY_FINDINGS.md`. The four available named reports were reviewed before implementation.

## 2. What was automated

Registered CoreSmoke gate `pre-i-automated-qa-proof`. It combines isolated fresh-state behavior, source/visibility contracts, safe reset-helper guards, terminal lifecycle service behavior, replacement-license gating, checkout completion hierarchy, and forbidden-claim guards. It runs both directly through `--only` and as part of full CoreSmoke.

## 3. What was source-reviewed only

React route ordering and the visibility/confirmation wiring for licensed or authenticated surfaces are source guards, not simulated clicks. This includes host License landing, Terminal Management button visibility and dialogs, Replace License re-ack UI, Backup page phrase/label wiring, and Checkout support-details placement. Existing focused gates add backend backup accept/reject behavior and related product-specific coverage.

## 4. What was browser-tested

Actual in-app browser inspection ran against an isolated temporary development database on `192.168.254.130:5399`. It verified the fresh LAN terminal landing, opened **Activate this Terminal**, observed the Terminal Machine Code and copy control, support email, no Host License ID/Store ID task, then opened **Import Signed Terminal License JSON** and observed that Host licenses cannot activate a terminal.

Host `/license` browser proof was not claimed: development mode supplies a development license baseline and routed the isolated loopback instance to Bootstrap. No fake license, production state, admin, or seeded sale was manufactured. Browser automation dependencies were not added.

## 5. Fresh Salryn state proof

The gate creates a unique temporary development root and verifies zero users, zero cashier sessions, one expected generated deployment-identity baseline, no `DEVKIRCH`/`Salryn Dev` store residue, an isolated unlicensed license store with no Host or Terminal license, no license file, and no authenticated API session. Source order asserts License before Bootstrap, Login, and Dashboard decisions. Temporary roots are cleaned best-effort; Kirch's real LocalAppData and repository `.salryn-dev` database are not reset or deleted.

The existing helper already meets the requested safety contract and was not churned: it supports `-WhatIf`, renames state to a timestamped backup, prints the backup location, warns about running processes, and contains no `Remove-Item`.

Manual safe use:

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-DevSalrynState.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-DevSalrynState.ps1
dotnet run --project src\Salryn.Api.Local -- --dev
```

Then open `http://127.0.0.1:5299/license`.

## 6. License landing proof

Source guards require `SALRYN`, `by Kirjane Labs`, `Activate Salryn`, `Import existing license`, `salryn.info@gmail.com`, and the Host Machine Code copy action. They reject `sr-only` and fresh-user Host License ID/Store ID tasks. Isolated backend behavior verifies the absence of an active Host or Terminal license. Browser proof is intentionally not claimed for this host surface.

## 7. Terminal activation proof

Source guards reject hidden `sr-only` content and normal-user `payload`, `schema`, `wrapper`, and `raw` jargon. They require visible Terminal Machine Code guidance/copy, terminal-only signed import, Host-license refusal, no Host/Store ID task, and support email. These assertions were also observed in the actual isolated browser flow described above.

## 8. Terminal Management proof

Source guards prove Active-only Disable visibility, Disabled-only Enable and Remove visibility, secondary Clear sessions styling, four confirmation states including **Clear operator sessions?**, user-facing Remove wording backed by internal revoke semantics, and absence of false Restore/primary Revoke labels. Isolated service behavior verifies Active to Disabled, Disabled to Active, Active cannot be enabled again, Disabled can be revoked/removed, and Revoked cannot be enabled. Authenticated browser clicks were not claimed.

## 9. Backup restore proof

The new gate checks the same `RESTORE SALRYN BACKUP` phrase across UI, web API, and backend, confirms the UI sends entered text unchanged, and requires the softer **Backup format version** label rather than schema/migration copy. The existing `pre-i-p1-copy-guard-honesty` behavioral fixture rejects short/wrong phrases and accepts the full phrase against a temporary backup/restore database.

## 10. Replace License proof

Source guards require first-install terms plus license text; replacement license text plus terms re-ack plus replacement consequence confirmation; direct import routing through terms; and disabled state tied to the complete replacement gate. Licensed browser state was not fabricated.

## 11. Checkout completion proof

Source order requires receipt success before Support details and Sale ID after/under Support details. The cashier-facing receipt-record instruction remains primary, and normal Checkout source contains no `debug` wording. No seeded checkout browser flow was fabricated.

## 12. Forbidden claim regression proof

The focused gate rejects visible client-source regressions for cloud activation, BIR-ready/accreditation, ESC/POS, cash drawer/drawer kick, cutter, hardware bundle, `Products of Kirjane Labs`, mock sample, large Build Type, and large Publisher wording. Historical reports were not treated as client UI.

## 13. Gates run

- `dotnet build Salryn.sln` — PASS, 0 warnings, 0 errors.
- All nine required focused CoreSmoke gates — PASS, including `pre-i-automated-qa-proof`.
- Full `dotnet run --project tools\Salryn.Tools.CoreSmoke` — PASS.
- `npm --prefix src\Salryn.App.Web run build` — PASS; Vite emitted its non-failing bundle-size advisory.
- `git diff --check` — PASS.
- Pre-report status contained only the intended CoreSmoke registration and new proof class.

## 14. Remaining manual screenshot requirements

- Fresh host `/license`: landing and no Dashboard/Login/Bootstrap bypass.
- Terminal Management: Active row, Disable confirm, Disabled Enable + Remove, Clear sessions confirm.
- Backup: full restore phrase.
- Replace License: re-ack requirements.
- Checkout: Sale ID only under Support/details.

Terminal activation landing, request dialog, and import dialog were browser-inspected, but screenshots were not committed.

## 15. Remaining risks

- Host License landing and licensed/authenticated application pages remain source/backend proof plus a small manual screenshot checklist because development mode supplies a license baseline.
- UI source guards detect contract drift but do not replace end-to-end interaction coverage for authenticated pages.
- The web production build remains a single large JavaScript chunk; this is an existing non-failing build advisory, not a PRE-I proof blocker.
- No package/I1, production activation, BIR/RMO/eSales, printer, or hardware claim was tested or authorized.

## 16. Final decision

`PRE_I_AUTOMATED_QA_PROOF_PASSED`

The requested focused automation is registered and green. Remaining manual work is limited to screenshots of host/licensed/authenticated surfaces that could not be reached honestly without manufacturing product state.

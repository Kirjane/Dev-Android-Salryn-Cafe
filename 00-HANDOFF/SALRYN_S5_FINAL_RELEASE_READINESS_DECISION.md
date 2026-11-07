# Salryn S5 Final Release Readiness Decision

Date: 2026-11-07 (Asia/Manila)  
Product: Base Salryn POS with U1 Accounts Receivable / Utang included  
Exact tested implementation SHA: `8310b16a90c49220f1953f35a9f1d8358eb13e08`  
Branch: `codex/issue-9-s0-s5-rc`  
PR: #10, draft; no merge or distribution authorized

## Candidate state

- Base source remains anchored to U1 with reviewed `main`/PR #8 reconciliation; no Café source, migrations 25/26, Café cash ledger, or Android client source was imported.
- U1 remains the signed paid Host feature `ReceivablesUtangLedger`, with Base and Terminal denial, Manila store-day accounting, durable request replay, collection reversal aging, and role boundaries.
- Publisher is Kirjane Information Technology Services. Personal credits and the superseded “Kirjane Labs” release-packaging identity are absent from the candidate packaging sources.
- LAN remains Future Feature. Normal release startup is actual-socket loopback-only even for a previously enabled database; saved LAN configuration and terminal records remain available only for explicit internal QA.
- No BIR accreditation, tax-compliance certification, production acceptance, or public-release claim is made.

## Gate decision

| Gate | Status | Evidence |
|---|---|---|
| Source reconciliation / PR conflict | PASS | PR #10 is conflict-free after conservative `main` merge; [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md). |
| U1 entitlement/checkout/collection/aging/dashboard | PASS | Current focused runtime gates at unchanged implementation path; [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md). |
| LAN release listener boundary | PASS | Real Windows socket proof with previously enabled disposable DB; [evidence reconciliation](SALRYN_FINAL_ACCEPTANCE_EVIDENCE_RECONCILIATION.md). |
| Schema/backup/restore | PASS | v24 clean/legacy upgrade, future-v25 rejection, safety backup/restore and non-mutation; [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md). |
| Build/web/dependency/diff | PASS | Release solution build, Vite build, npm audit, NuGet vulnerability scan, and diff check; [execution ledger](SALRYN_STABILIZATION_EXECUTION_LEDGER.md). |
| Canonical controlled activation proof | BLOCKED — controlled LicensingOps asset required | Repository/manifest/retired-key fixture unavailable to this environment; exact handoff in [evidence reconciliation](SALRYN_FINAL_ACCEPTANCE_EVIDENCE_RECONCILIATION.md). |
| Actual setup EXE compilation | PASS | Inno Setup 6.7.3; `C:\Salryn\artifacts\installer\setup\Salryn-POS-BetaRC1-Setup.exe`; SHA-256 `0D06FE18B6B371DD96AE8BF230534DE9A42D65F570DA8A74F8D51B6244A01034`. Artifact remains local and unpublished. |
| Installer identity/payload inspection | PASS | Product `Salryn POS`, version `0.1.0-rc1`, company `Kirjane Information Technology Services`, 24,110,051 bytes; WebView2 bootstrapper/loader and production dist present; forbidden database/license/private-key/test artifact count 0. |
| Clean install/upgrade/uninstall preservation | BLOCKED — physical Windows operator | Newly compiled exact candidate has not been installed. Minimal script is in the reconciliation report. |
| Real scanner and Windows printer | BLOCKED — physical hardware | Software behavior is reused; physical hardware was explicitly never proven. Minimal script is in the reconciliation report. |
| Installed offline restart/recovery and large U1 UI | BLOCKED — physical/operator observation | Headless persistence/recovery gates pass; current installed UX observation remains required. |
| Kirch exact-SHA approval | BLOCKED — authority | Required before merge, PR ready state, licensing, distribution, or pilot deployment. |

## Installer and recovery instructions

- Verify setup before use:

```powershell
Get-FileHash .\Salryn-POS-BetaRC1-Setup.exe -Algorithm SHA256
```

- Expected SHA-256: `0D06FE18B6B371DD96AE8BF230534DE9A42D65F570DA8A74F8D51B6244A01034`.
- Setup is framework-dependent and requires .NET 10; it includes the WebView2 Evergreen bootstrapper, which requires internet only if WebView2 Runtime is absent.
- Before upgrade, copy `%ProgramData%\Salryn` and `%LocalAppData%\Salryn\Licensing` to a dated operator-controlled backup. Do not move licenses between machines.
- Production database/backups remain under `%ProgramData%\Salryn`; the stable Inno AppId preserves upgrade identity and uninstall deliberately does not remove ProgramData.
- Restore only a verified `.salryn-backup.zip`, with Salryn stopped/exclusive database access available. The restore flow creates a pre-restore safety backup and rejects checksum mismatch or future schema.

## Known limits

- Setup EXE is unsigned and may trigger SmartScreen.
- .NET 10 runtime is not bundled.
- WebView2 Evergreen bootstrapper may need internet on a machine without WebView2 Runtime.
- LAN terminals remain Future Feature and are not marketed or accepted as part of the initial single-Host release.
- Printer support is Windows print queue only; scanner support is keyboard-wedge only.
- No BIR accreditation or accounting-system claim.

## Verdict

**HUMAN GATES BLOCKED**

All accessible code-side and packaging work is complete, including a real compiled installer. The candidate cannot honestly be labeled acceptable for pilot until the controlled canonical activation proof and the minimal clean-install/upgrade/offline/hardware/operator observations above are completed, followed by Kirch's written approval of the exact candidate SHA. No merge or distribution is authorized.

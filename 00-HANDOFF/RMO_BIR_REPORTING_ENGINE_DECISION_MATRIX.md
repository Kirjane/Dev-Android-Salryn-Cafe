# RMO / BIR / Reporting Engine Decision Matrix

Date: 2026-07-15

Branch: `KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

Base SHA: `b21fe40cc6437200a853cf5bcd44edbea209da6c`

Status: `READY_FOR_SOL_VERIFICATION`

Legend:

- YES = present in repo/source-reviewed.
- NO = not present or not appropriate to claim.
- PARTIAL = foundation exists but not enough for production/compliance claim.
- Manual-proven always means actual Kirch/operator manual acceptance, not source existence.

---

| Feature / Surface | Exists in repo? | UI visible? | Backend exists? | Mutates data? | Produces file/output? | Sends externally? | Automated-proven? | Manual-proven? | Client-safe now? | Beta-safe now? | Official/BIR claim allowed? | Recommended action |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Owner Reports | YES | YES | YES | NO | NO | NO | YES/PARTIAL from prior owner-report smoke | NO | YES internal | YES internal | NO | Keep; rename to Detailed Reports later |
| Sales & Receipts | YES | YES | YES | YES for void/reprint request paths; normal sales historical view read-only | Receipt snapshot preview/reprint | NO | YES from POS M/R lanes | PARTIAL manual | YES operational | YES after manual acceptance | NO | Keep operational only |
| Internal receipt snapshot | YES | YES via Sales & Receipts | YES | Created at sale completion; reprint does not mutate | Text snapshot | NO | YES from POS M1/M2/R3/PRINTER1 lanes | PARTIAL | YES operational | YES after manual receipt/printer acceptance | NO | Keep as internal/operational receipt snapshot |
| Windows printer internal receipt | YES | YES through print/reprint/settings | YES | NO print mutation | Windows queue submission | NO | YES per SOL-verified PRINTER1 truth supplied by Kirch; manual pending | NO unless Kirch manual printer test passes | YES internal after manual | YES after manual | NO | Run manual non-thermal printer test |
| Official Setup | YES | YES Advanced | YES | YES setup rows and dry-run package rows | Internal previews/output links | NO | PARTIAL from RMO setup/readiness gates | NO | NO for standard client | NO | NO | Hide/relabel as Internal Compliance Setup / lab |
| Traditional Reporting | YES | YES Advanced | YES | PARTIAL/YES: Z finalize endpoint mutates/finalizes | X/Z/EJ/eSales draft output | NO | PARTIAL from trad gates | NO | NO | NO | NO | Hide from standard client build; compliance lane only |
| Production Reports | YES | YES Advanced | YES | NO business mutation; export writes artifact | YES PDF/artifact/manifest | NO | PARTIAL from native-PDF/report gates | NO | NO if named Production Reports | NO until renamed/gated | NO | Rename to Report Artifacts or hide |
| X-reading | YES | YES via Traditional Reporting | YES | NO preview | YES preview/result | NO | PARTIAL | NO | NO standard client | NO | NO | Compliance draft only |
| Z-reading | YES | YES via Traditional Reporting | YES | YES finalize endpoint | YES view/result | NO | PARTIAL | NO | NO | NO | NO | Hide until compliance acceptance; rename internal test if retained |
| EJ / electronic journal | YES/PARTIAL | YES via Official/Traditional | YES/PARTIAL | NO for export; live append disabled in status paths | YES export/preview | NO | PARTIAL | NO | NO | NO | NO | Keep foundation only; no live claim |
| eSales export | PARTIAL | YES via Traditional Reporting | YES draft endpoint | NO draft export | YES draft | NO | PARTIAL | NO | NO | NO | NO | Hide/defer until authoritative schema and transmission design |
| BIR receipt output | PARTIAL | YES through Official Setup wording/previews | PARTIAL | NO live output | Internal preview only | NO | PARTIAL | NO | NO | NO | NO | Do not claim; keep internal/watermarked only |
| Official invoice output | PARTIAL | YES Official Setup | PARTIAL | NO live issuance | YES internal HTML/thermal/print-view projection | NO | PARTIAL | NO | NO | NO | NO | Hide/relabel; not legal issuance |
| Void/cancel official document | PARTIAL | Not primary operational UI | PARTIAL skeleton/status | NO live integration in inspected status | Preview/status only | NO | PARTIAL | NO | NO | NO | NO | Compliance future; operational void remains separate |
| Reprint official document | PARTIAL | Not primary operational UI | PARTIAL | NO live official reprint | Preview/status only | NO | PARTIAL | NO | NO | NO | NO | Compliance future |
| Report artifact export | YES | YES Production Reports | YES | Writes controlled artifact/manifest | YES PDF + manifest | NO | PARTIAL from native-PDF gates | NO | Admin/internal only | Not as production label | NO | Rename to Report Artifacts; keep controlled root |

---

## Surface classification table

| Surface | Classification | Why |
|---|---|---|
| Dashboard | KEEP_VISIBLE_ADMIN_ONLY / normal manager summary | Pulls owner summary safely and falls back to dashboard summary. |
| Owner Reports | RENAME_ONLY | Good internal detailed reports; label can become Detailed Reports. |
| Host LAN Control | KEEP_VISIBLE_BUT_COMING_SOON | Real host/admin page, but not client-ready or entitlement-ready. |
| Official Setup | COMPLIANCE_ONLY_FUTURE / HIDE_FROM_STANDARD_CLIENT_BUILD | Heavy official/BIR/invoice/setup wording and metadata. |
| Traditional Reporting | COMPLIANCE_ONLY_FUTURE / BLOCKER_BEFORE_BETA | X/Z/EJ/eSales/finalize wording and actions are high-risk. |
| Production Reports | RENAME_ONLY / REPORT_ONLY / HIDE_FROM_STANDARD_CLIENT_BUILD | Artifact/report pipeline exists, but label implies release readiness. |

---

## What to tag as coming soon

| Item | Hide? | Disabled card? | Admin-only? | Exact recommended copy |
|---|---:|---:|---:|---|
| LAN terminal control | No, keep internal admin visible | Optional | Yes | `Coming Soon / internal preview. LAN terminal control is not part of the current client-ready release. Licensing/entitlement and production client deployment remain deferred.` |
| Backup & Restore UI | Hide until built | Yes in Settings/Advanced only | Yes | `Backup & Restore UI planned. Backend foundation exists; create/restore/list controls are not exposed in this build.` |
| Official/BIR output | Hide standard client | Optional internal lab only | Yes/compliance role | `Internal compliance lab only. No official receipt/invoice is issued. No BIR/RMO compliance claim.` |
| eSales / Production Reports | Hide or rename | Optional | Yes/report admin | `Internal report artifacts only. No eSales production transmission or legal submission.` |
| ESC/POS / cash drawer / cutter / thermal profiles | Hide until implemented | Yes in printer settings | Yes | `Deferred hardware support. Windows printer queue is separate and does not prove ESC/POS, cutter, or cash drawer behavior.` |
| Licensing final UI | Hide until LIC-FINAL | Yes | Yes | `Licensing final UI deferred. Offline license and LAN entitlements are not final in this build.` |
| Cloud/license updater | Hide | Optional roadmap only | Admin only | `Cloud/license updater deferred. No cloud activation or updater is active in this build.` |

---

## Release claim boundary

Safe to say now:

- Owner Reports are available for internal operational reporting.
- Dashboard can show owner summary to users with report access.
- Windows printer queue wiring exists and needs/uses separate manual printer proof.
- LAN is internal preview / coming soon.
- Official/BIR/reporting foundations exist as guarded/internal surfaces.

Unsafe to say now:

- beta ready
- installer ready
- BIR ready
- official receipt ready
- official invoice ready
- eSales production ready
- licensing final
- LAN production ready
- compliance guaranteed

---

## Recommended next branches

1. `KIRCH-REPAIR-USER-M1-SMOKE-DATA-ISOLATION` push/verify
2. `KIRCH-POS-SURFACE4-ADVANCED-CLAIM-RISK-CLEANUP`
3. `KIRCH-POS-BACKUP-UI1-SAFE-BACKUP-RESTORE`
4. `KIRCH-LIC-FINAL-OFFLINE-LICENSE-LAN-ENTITLEMENTS`
5. `KIRCH-I1-PACKAGE-INSTALL-PROOF`

Status: `READY_FOR_SOL_VERIFICATION`

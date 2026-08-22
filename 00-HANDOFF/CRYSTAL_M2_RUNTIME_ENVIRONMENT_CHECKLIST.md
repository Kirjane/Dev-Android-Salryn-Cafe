# CRYSTAL-M2 Runtime Environment Checklist

**Branch:** `KIRCH-REPORTS-CRYSTAL-M2-RUNTIME-ENVIRONMENT-CHECKLIST`  
**Base:** `KIRCH-REPORTS-CRYSTAL-M1-RUNTIME-DECISION-AND-MINIMAL-PROOF` @ `783ad1f5dd40f258200403fa2fab16e8a88230ca`  
**Mode:** Documentation/checklist only. No runtime implementation.  
**Decision carried forward from M1:** Direct in-process Crystal integration remains rejected; isolated Windows worker remains the only approved path.

## 1. Source evidence checkpoint

Primary references used for this checklist:

- SAP Crystal Reports, developer version for Microsoft Visual Studio - Supported Platforms, updated May 29, 2026, applies up to Support Package 40: `https://www.sap.com/docs/download/2016/06/f871031e-757c-0010-82c7-eda71af511fa.pdf`
- SAP Crystal Reports for Visual Studio (.NET) community runtime/licensing page: `https://pages.community.sap.com/topics/crystal-reports/visual-studio`
- SAP Crystal Solutions FAQ: `https://pages.community.sap.com/topics/crystal-reports/faq`

Important source facts to preserve:

- Supported .NET Framework versions listed for CR for Visual Studio are .NET Framework 4.5.2, 4.6, 4.7 and 4.8.
- Modern .NET / `net10.0` is not listed as a supported CR for Visual Studio runtime target.
- Only 64-bit operating systems are supported after Dec. 31, 2025.
- 32-bit CR .NET runtime is no longer supported after Dec. 31, 2025.
- Windows Server must use Desktop Experience where applicable.
- CR4VS SP38+ removed the Microsoft Visual C++ 2015-2022 Redistributable from the CR package, so the worker machine must be checked for that prerequisite before install.
- Runtime redistribution/licensing must be confirmed before release; no assumption is allowed.

## 2. M2 non-goals

This branch must not introduce:

- SAP package references.
- Crystal runtime binaries.
- Runtime installer files.
- `.rpt` files.
- Template upload support.
- Caller-provided template paths.
- Caller-provided filesystem output paths.
- Arbitrary SQL.
- Printer logic.
- Installer packaging changes.
- PDF success claims.
- BIR/SAP/legal compliance claims.

## 3. Worker machine requirements

Before any Crystal-M3 runtime proof, assign one controlled Windows worker machine.

Required:

- 64-bit Windows only.
- Windows 10/11 for development/test, or supported Windows Server with Desktop Experience where server use is intended.
- CPU/RAM/disk capacity adequate for the selected SAP runtime package and proof workload.
- Local admin or equivalent install rights for Crystal runtime setup.
- Microsoft Visual C++ 2015-2022 Redistributable installed if required by the selected CR runtime package.
- Latest applicable Windows patches applied.
- Local filesystem location for worker config and controlled output folder.
- No direct production database credentials on the worker.

Record before proof:

| Item | Required evidence | Status |
|---|---|---|
| Machine name | Screenshot or command output | TODO |
| Windows edition/version/build | `winver` or PowerShell output | TODO |
| OS bitness | PowerShell output | TODO |
| Desktop Experience for Server | screenshot/role output if Server | TODO |
| Local admin/install rights | installer precheck or admin confirmation | TODO |
| VC++ 2015-2022 redistributable | installed apps / registry / installer check | TODO |
| Crystal runtime package name | exact installer filename/version | TODO |
| Crystal runtime bitness | 64-bit only | TODO |

Suggested PowerShell checks for Codex/Sol or Kirch manual proof:

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsHardwareAbstractionLayer, OsArchitecture
[Environment]::Is64BitOperatingSystem
Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* |
  Where-Object { $_.DisplayName -like '*Visual C++*2015*2022*' } |
  Select-Object DisplayName, DisplayVersion, Publisher
```

## 4. Runtime architecture checklist

Approved architecture:

```text
Salryn net10.0 host
  -> server-owned report dataset package
  -> isolated Windows worker protocol
  -> allow-listed template binding inside worker
  -> PDF artifact metadata
  -> host receives metadata/status only
```

Required properties:

- Salryn main app remains `net10.0` and Crystal-free.
- Worker is a separate Windows process, not in-process inside the Salryn host.
- Worker target is .NET Framework 4.8 if the selected Crystal SDK/runtime requires .NET Framework.
- Host-to-worker communication is protocol-only.
- Worker consumes only a server-built dataset package.
- Worker has no shared database access.
- Worker has no ability to run caller SQL.
- Worker has no ability to load caller-supplied `.rpt` paths.
- Worker writes only to a controlled output directory from worker config.

Required boundary questions before Crystal-M3:

| Question | Required answer before M3 |
|---|---|
| Is the selected CR runtime 64-bit? | Yes |
| Does the worker target .NET Framework 4.8? | Yes, unless newer official source says otherwise |
| Does Salryn host remain Crystal-free? | Yes |
| Does worker receive only dataset JSON/package? | Yes |
| Does worker have direct DB credentials? | No |
| Does worker accept template path from caller? | No |
| Does worker accept arbitrary SQL? | No |

## 5. Licensing and redistribution questions

No release or customer deployment may proceed until Kirch/project owner explicitly records licensing confirmation.

Questions to answer:

- Is this an internal business use deployment, external/customer deployment, or redistributed application?
- Will the customer only consume generated PDF files?
- Will any user directly view `.rpt` files?
- Will any user edit, refresh, or create `.rpt` files?
- Is SAP Crystal Reports Viewer needed for any scenario?
- Are named user Crystal Reports licenses needed for any scenario?
- Is the worker a client application or server application under SAP’s runtime licensing definitions?
- Who is responsible for acquiring/holding the Crystal license?
- Is redistribution of the selected runtime package permitted for the planned deployment model?

Release gate:

```text
No legal/licensing approval = no customer release with Crystal runtime.
```

## 6. Template policy checklist

Allowed:

- Allow-listed template IDs defined in source/config.
- Template version hash recorded before rendering.
- Template files held outside the repo unless Kirch explicitly approves committing a sanitized template.
- Template review for no embedded credentials.
- Template review for no direct database SQL/connection binding.
- Template uses provided dataset fields only.

Forbidden:

- Arbitrary `.rpt` upload.
- Caller-provided template path.
- Caller-provided SQL.
- Templates with embedded DB credentials.
- Templates with direct production database connection strings.
- Templates that recalculate official VAT/refund/void/tax totals.
- Template selection by raw filesystem path.

Template inventory table:

| Template ID | File name | Hash SHA-256 | Source approval | Data source mode | Status |
|---|---|---:|---|---|---|
| `TODO_FIRST_TEMPLATE_ID` | TODO | TODO | TODO | Dataset only | Pending |

## 7. Output policy checklist

First proof is PDF only.

Required:

- Output kind allow-list includes PDF only for first proof.
- Output directory is controlled by worker config.
- Caller cannot provide output directory or output filename path.
- Worker sanitizes generated filenames.
- Worker writes artifact metadata sidecar or returns metadata to host.
- Worker computes artifact SHA-256 after writing PDF.
- Worker records byte length.
- Worker records created-at UTC.
- Worker records renderer name/version.
- Worker records template ID/hash.
- Worker records source dataset content hash and dataset envelope hash.
- Worker retention/cleanup policy is defined before repeated proof runs.

Required artifact metadata fields:

| Field | Required |
|---|---|
| `fileName` | yes |
| `contentType` | `application/pdf` |
| `byteLength` | yes |
| `sha256` | yes |
| `createdAtUtc` | yes |
| `rendererName` | yes |
| `rendererVersion` | yes |
| `sourceDatasetContentSha256` | yes |
| `sourceDatasetEnvelopeSha256` | yes |
| `templateId` | yes |
| `templateSha256` | yes |
| `workerProtocolVersion` | yes |

## 8. Security and stability checklist

Runtime proof must preserve fail-closed behavior.

Required before M3 implementation:

- Timeout cap defined.
- Max concurrent jobs defined.
- First proof uses single-job lock or a one-item queue.
- Job cancellation behavior documented.
- Worker process crash handling documented.
- Worker unavailable response returns safe error.
- Render failure returns safe error and no partial artifact is advertised.
- No printer logic.
- No production official posting activation.
- No eSales transmission.
- No official/BIR production claim.
- No direct database query from template.
- No arbitrary SQL.
- No arbitrary template path.
- No arbitrary filesystem output path.

Suggested first-proof constraints:

```text
MaxTimeout: 120 seconds
MaxConcurrentJobs: 1
OutputKind: pdf only
DatasetSource: host-built JSON package only
TemplateSource: allow-listed local worker config only
```

## 9. Manual proof checklist

Before marking any real PDF proof as accepted, collect evidence for every item.

1. Install selected 64-bit Crystal runtime on the controlled worker machine.
2. Confirm runtime version and bitness.
3. Confirm VC++ Redistributable prerequisite if applicable.
4. Confirm worker target/framework selection.
5. Run worker self-check.
6. Generate one server-built dataset package from Salryn host.
7. Use one allow-listed, approved template.
8. Render one PDF artifact.
9. Verify artifact byte length > 0.
10. Verify PDF file SHA-256.
11. Verify dataset content hash and envelope hash in artifact metadata.
12. Verify template hash in artifact metadata.
13. Verify renderer name/version in artifact metadata.
14. Verify output folder is controlled by worker config.
15. Confirm no caller DB input, SQL input, template path input, or filesystem output path input.
16. Confirm failure when runtime is missing.
17. Confirm failure when template hash mismatches.
18. Confirm failure when output kind is not PDF.
19. Confirm failure on timeout.
20. Confirm no printer action occurred.

## 10. Gate before Crystal-M3

Crystal-M3 is blocked until all are true:

- Exact Crystal runtime version known.
- Runtime bitness confirmed 64-bit.
- Worker OS confirmed supported.
- VC++ prerequisite checked.
- Licensed usage confirmed by Kirch/project owner.
- Worker target chosen, likely .NET Framework 4.8 unless newer official SAP source proves otherwise.
- Template source approved.
- Template ID and template hash policy approved.
- Output folder policy approved.
- Cleanup/retention policy approved.
- One machine assigned for proof.
- Codex/Sol can run the worker proof locally on that machine.

## 11. Current M2 status

```text
Crystal runtime installed: no evidence
Worker implementation present: no
PDF proof present: no
Template present: no
Licensing approved: no evidence
Installer packaging changed: no
Salryn host Crystal-free: intended to remain yes
```

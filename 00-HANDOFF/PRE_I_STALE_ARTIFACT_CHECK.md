# PRE-I Stale Artifact Check

## Decision

`PRE_I_STALE_ARTIFACT_CHECK_P1_BLOCKED`

The blocker is current-state documentation staleness, not stale code execution.

## Checked artifacts

### Current-state docs

P1 blocker:

- `README.md` still points readers to `00-HANDOFF/SCOPE_SUMMARY.md` as the current state and identifies `KIRCH-SANDBOX` / `89e55b61748309d0d68d8d05627a01ef101649f2` as the current checkpoint.
- `00-HANDOFF/SCOPE_SUMMARY.md` still identifies the current repo branch as `KIRCH-SANDBOX`, current stable checkpoint as `89e55b61748309d0d68d8d05627a01ef101649f2`, and next lane as `SANDBOX-RMO-FOUNDATION-09D`.
- Those files are no longer safe as beta-facing current-state docs for the PRE-I stack unless updated or marked historical.

Recommended repair:

- Update root `README.md` to point to the PRE-I final review/seal path, or mark it as historical and direct readers to the latest PRE-I report.
- Update `00-HANDOFF/SCOPE_SUMMARY.md` to either reflect PRE-I current state or explicitly say it is historical to the KIRCH-SANDBOX/RMO lane.

### PRE-I accepted reports

Current proof/history:

- `00-HANDOFF/PRE_I_PRODUCT_SKU_BARCODE_CONTRACT_REPORT.md`
- `00-HANDOFF/PRE_I_SHELL_SIDEBAR_COLLAPSE_POS_USABILITY_REPORT.md`
- `00-HANDOFF/PRE_I_LAN_REACHABILITY_AND_TERMINAL_COPY_REPAIR_REPORT.md`

These are current/historical proof reports and should remain.

### Old public-key references

Current expected canonical fingerprint:

`97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59`

Retired old-key fingerprint from the assignment:

`10149b43f8dcb6c82e4c8fdd538572faaa78fd126af2caaa29cad997e67bc284`

Result from inspected source/proof docs:

- The canonical fingerprint is present in the Salryn canonical activation proof gate and LicensingOps M6 report.
- The Salryn proof gate requires old-key Host import rejection and historical old-key signature fail-closed behavior.
- No committed private-key path or private-key material was identified from inspected proof gates.
- Connector code search returned no hits for either full fingerprint string, so this result should be rechecked locally with `rg` before final seal.

### License proof/temp folders

- LicensingOps M6 creates canonical activation proof folders under `%TEMP%\salryn-canonical-license-activation-proof-*`.
- Those are intentionally temp proof artifacts, not durable client license storage.
- Cleanup should be local-only; do not commit generated proof folders.

Suggested local cleanup command after gates are captured:

```powershell
Get-ChildItem $env:TEMP -Directory -Filter 'salryn-canonical-license-activation-proof-*' |
  Remove-Item -Recurse -Force
```

### Stale package/build output

Not verified through local filesystem in this connector session.

Before seal, check locally:

```powershell
git status --ignored --short
rg -n "10149b43f8dcb6c82e4c8fdd538572faaa78fd126af2caaa29cad997e67bc284|97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59|Host License ID|terminal count|restart required|localhost|DB reset|new build|debug|LicensingOps|BIR certified|eSales|ESC/POS|cash drawer|cutter|unlimited terminals" .
```

### Outdated screenshots/docs

Not fully verifiable without local filesystem/media inspection. Treat this as manual/local follow-up before beta packaging:

- old localhost browser screenshots as final app,
- old License page screenshots,
- old LAN restart-required behavior screenshots,
- old terminal Host License ID instructions,
- old LicensingOps key mismatch notes.

## Classification summary

| Item | Classification | Action |
| --- | --- | --- |
| Root `README.md` current checkpoint | P1 stale doc blocker | Update or mark historical |
| `00-HANDOFF/SCOPE_SUMMARY.md` current checkpoint | P1 stale doc blocker | Update or mark historical |
| PRE-I accepted reports | Current proof/history | Keep |
| Old-key fixture references | Historical only if fail-closed | Recheck with local `rg` |
| `%TEMP%` proof folders | Local ignored/temp | Cleanup locally after proof capture |
| Screenshots/package output | Unknown in connector session | Local stale scan required |

## Seal readiness

Not seal-ready until stale current-state docs are repaired/marked and local stale scan is completed.

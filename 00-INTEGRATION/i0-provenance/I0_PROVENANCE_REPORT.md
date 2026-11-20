# Salryn Cafe — Issue #2 (I0) Provenance, Archival Import & Candidate Baseline Report

- **Date:** 2026-11-20
- **Destination Repository:** `https://github.com/Kirjane/Dev-Salryn-Cafe` (private)
- **Destination `main` (Untouched):** `a85fb2ec92651e3255f901acae6221928098b764`
- **Destination Brand Root:** `6b266249566c59cf0128c8f2ee8583010316ca1f`
- **Candidate Integration Branch:** `forge/candidate-salryn-cafe`
- **Base Unrelated-Histories Merge Commit:** `9882eef36b065af0971e8f883408e5ac04db62a9` (Parents: `607fcc4b17e2c069d0fd36d0646c554940977e2d` + `a85fb2ec92651e3255f901acae6221928098b764`)

---

## 1. Source Pins & Original Tree Hashes (`OBSERVED`)

| Source | Repository | Pinned Ref | Commit SHA | Original Tree SHA |
|---|---|---|---|---|
| **Kernel** | `Kirjane/Salryn` | `codex/salryn-r3-canonical-integration-r1` | `607fcc4b17e2c069d0fd36d0646c554940977e2d` | `2451fb435bff51e735f80ec3d8053ea56e123764` |
| **F&B (Yza)** | `Kirjane/Salryn-CafeYza` | `main` | `4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3` | `ba9d4ac6964ca8176f395206f0fcf101c8199bf1` |
| **Dest Brand Root** | `Kirjane/Dev-Salryn-Cafe` | `main~1` | `6b266249566c59cf0128c8f2ee8583010316ca1f` | `778c056b059e151177ec5305bd6e54b2df6c4ce6` |
| **Dest `main`** | `Kirjane/Dev-Salryn-Cafe` | `main` | `a85fb2ec92651e3255f901acae6221928098b764` | `3faa6b6f38483dd3f8d4e9f22823214b99de4ba2` |

---

## 2. Pre-Transfer Security & Workflow-Trigger Clearance (`PASS`)

Verified independently by `@argon` (#72) and `@aegis` (#75 — `PRE-TRANSFER CLEAR`):
- **Non-shallow source clones:** Both `Kirjane/Salryn` and `Kirjane/Salryn-CafeYza` verified `is-shallow=false`.
- **Sensitive files / secrets / LFS / submodules:** `0` sensitive blobs (`.env`, `.pfx`, `.p12`, `.pem`, `.key`, `.jks`, `.keystore`, `.kdbx`, `.sqlite`/`.db`, `.bak`, `.dump`, SSH keys, `appsettings.Production*`) in `git rev-list --all --objects`; `0` `.gitmodules`; `0` `.gitattributes` / LFS pointers; `0` real secrets on pinned trees.
- **Inherited CI workflow triggers:** All historical `.github/workflows/*.yml` use explicit branch allowlists (`0` wildcard `*`, `0` unfiltered `on: push`, `0` `schedule:`). Archival namespaces `refs/heads/archive/kernel/*`, `refs/tags/archive/kernel/*`, `refs/heads/archive/yza/*`, `refs/tags/archive/yza/*` do not match any inherited trigger.

---

## 3. Archival Ref & Reachable Commit Parity (`OBSERVED — PASS`)

Full manifests of all advertised remote heads and tags (including peeled annotated tag SHAs) are recorded in:
- `00-INTEGRATION/i0-provenance/kernel-refs-manifest.txt`
- `00-INTEGRATION/i0-provenance/yza-refs-manifest.txt`

| Verification Gate | Command / Method | Observed Result | Status |
|---|---|---|---|
| **Kernel Archival Refs** | `git fetch --no-tags https://github.com/Kirjane/Salryn.git "refs/heads/*:refs/heads/archive/kernel/*" "refs/tags/*:refs/tags/archive/kernel/*"` | `180` heads, `2` tags imported; `0` tip mismatches vs `ls-remote` | `PASS` |
| **Yza Archival Refs** | `git fetch --no-tags https://github.com/Kirjane/Salryn-CafeYza.git "refs/heads/*:refs/heads/archive/yza/*" "refs/tags/*:refs/tags/archive/yza/*"` | `41` heads, `0` tags imported; `0` tip mismatches vs `ls-remote` | `PASS` |
| **Kernel Reachable SHA Parity** | `Compare-Object` (`rev-list --remotes=origin --tags` vs `archive/kernel/*`) | `src=1373`, `dst=1373`, `set_diff=0` | `PASS` |
| **Yza Reachable SHA Parity** | `Compare-Object` (`rev-list --remotes=origin --tags` vs `archive/yza/*`) | `src=2091`, `dst=2091`, `set_diff=0` | `PASS` |
| **Candidate Ancestry (Kernel)** | `git merge-base --is-ancestor 607fcc4b17e2c069d0fd36d0646c554940977e2d HEAD` | `exit=0` | `PASS` |
| **Candidate Ancestry (Brand Root)** | `git merge-base --is-ancestor 6b266249566c59cf0128c8f2ee8583010316ca1f HEAD` | `exit=0` | `PASS` |
| **Candidate Ancestry (Dest `main`)** | `git merge-base --is-ancestor a85fb2ec92651e3255f901acae6221928098b764 HEAD` | `exit=0` | `PASS` |
| **Brand Assets Blob Parity** | `git ls-tree -r 6b266249 assets/brand` vs `HEAD` | `0` diffs across all 7 files (hashes below) | `PASS` |
| **`FORGE-DISPATCH-V2.md` Parity** | `git rev-parse a85fb2ec:00-INTEGRATION/FORGE-DISPATCH-V2.md` vs `HEAD` | `5e8d8c13a24b58c3f5a12d18bf6d870a2d9b3e9b` (identical) | `PASS` |
| **Historical `AGENTS.md` Preserved** | `git rev-parse 607fcc4b:AGENTS.md` vs `HEAD` | `4d1d241924dabf49d7a8040b86915e1dc574cbb5` (unmodified) | `PASS` |
| **Submodules / LFS** | `git ls-files "*gitmodules*"` / `"*gitattributes*"` | `0` / `0` | `PASS` |
| **Object Integrity** | `git fsck --full` | `exit=0` (0 errors, 0 corrupt objects) | `PASS` |

*Note on Scope:* Unpushed/unreachable local objects in external workstations are outside verified scope; all advertised remote heads, tags, and reachable commit graphs from both upstream repositories are 100% preserved with original SHAs, parent graphs, and authorship. Neither upstream repository was modified.

### Preserved Brand Asset Blob Hashes (`assets/brand/*`)
- `assets/brand/PALETTE.md`: `81840dec1947bcf42f76a5d5d44d1e83ade83dcf`
- `assets/brand/colors.css`: `b21a3b20b967c25870f1f84466cd0deda2dba274`
- `assets/brand/colors.json`: `4b655cf3d57903957694d2c7aa3b60b8ca350d4e`
- `assets/brand/prompts.txt`: `eacddd226c901ca1f79c0636fb36affe40bffd4f`
- `assets/brand/salryn-app-icon.png`: `ce83298762cb5dabca05d6b5b6e2ea4813101159`
- `assets/brand/salryn-banner.png`: `02d3ba94d3af180d64fbf7e42b0a169ebc305e48`
- `assets/brand/salryn-transparent-logo.png`: `2897b64bbe64f9a9c09adff1a291e8b1fb23cddb`

---

## 4. Merge Resolution & Unmerged Yza Active Branches Summary

- **Deliberate Merge Conflict Resolution (`9882eef3`):** Only `README.md` conflicted during `git merge --allow-unrelated-histories --no-ff a85fb2ec`. Resolved by preserving Salryn Cafe's brand kit README and adding explicit I0 authority/provenance pointers. Yza's tree was **not** merged into `forge/candidate-salryn-cafe`; all Yza history remains immutable under `refs/heads/archive/yza/*`.
- **Unmerged Yza Remote Branches Inspected (3 of 41 ahead of Yza `main` `4241ea1e`):**
  1. `archive/yza/KIRCH-C3-DAT20-OPERATOR-CLOSEOUT` (`f3768bc0`, `+1/-0`): docs/QA only (C3-DAT-20 isolated restore acceptance + flags `NU1902`/`NU1903` on `System.Security.Cryptography.Pkcs 8.0.1`).
  2. `archive/yza/KIRCH-CAFEYZA-DOC-AUTHORITY-RECONCILIATION` (`af3ba595`, `+7/-4`): docs only (authority cleanup branched from `15f001eb`).
  3. `archive/yza/KURT-CAFEYZA-OPERATOR-BOOTSTRAP` (`ec8a0f3a`, `+1/-929`): 1 bootstrap doc file.
  *(All 38 remaining Yza branches are ancestor-merged into `4241ea1e`; 0 unmerged Yza code changes exist outside `4241ea1e`.)*

---

## 5. Baseline Build & Executable Test Verification (`OBSERVED`)

- **Environment:** Windows, `.NET SDK 10.0.401`, `Git 2.55.0.windows.3`, worktree `Dev-Salryn-Cafe-wt/i0`
- **Build (`Salryn.sln`):**
  - Command: `dotnet build Salryn.sln -c Release --nologo`
  - Result: **`PASS`** (`exit=0`, `0 Error(s)`, `20 Warning(s)`)
- **Executable Baseline Checks (`Salryn.Tools.CoreSmoke`):**
  - Command: `dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build` (run from worktree root)
  - Result: **`PASS`** (`exit=0`, `415` check invocations in `CoreSmoke/Program.cs`, `SALRYN Core smoke checks passed.`)
- **Note on `dotnet test tests/Salryn.Tests`:** Intentionally **`NOT RUN`** as a pass gate because `tests/Salryn.Tests` lacks `Microsoft.NET.Test.Sdk`/xUnit (`0` discovered tests; zero-test `PASS` is prohibited under V2 Dispatch and tracked for replacement in Issue `#9` I7).

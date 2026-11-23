# 00 — Source Custody, Worktree Isolation & Bundle Architecture

**Mission:** `FORGE-SALRYN-CAFE-CANONICAL-RECOVERY-R0-C1-PROOF`  
**Issue:** [Kirjane/Dev-Salryn-Cafe#44](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/44)  
**GitHub Actor:** `Kirch-Nairu`  
**Date:** 2026-11-23 (Asia/Manila)

## 1. Verified Source Authorities

| Authority | Repository / Ref | Verified Full SHA | Verification Result |
|---|---|---|---|
| Canonical Cafe Yza | `Kirjane/Salryn-CafeYza` (`main` / `archive/yza/main`) | `4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3` | `SOURCE_INSPECTED` — verified via `git rev-parse` in local `Salryn-CafeYza` checkout and `Dev-Salryn-Cafe` archival ref |
| Canonical C3 Operator Closeout | `Kirjane/Salryn-CafeYza` (`KIRCH-C3-DAT20-OPERATOR-CLOSEOUT` / `archive/yza/KIRCH-C3-DAT20-OPERATOR-CLOSEOUT`) | `f3768bc0c85028e7d67c19a2f1053fda7f89b209` | `SOURCE_INSPECTED` — verified zero diff vs `4241ea1` on `CafeMenuManager.tsx`, `menuCatalog.ts`, and `LocalMenuCatalogEndpoints.cs` |
| Current Salryn Cafe Candidate | `Kirjane/Dev-Salryn-Cafe` (`origin/forge/salryn-operator-recovery-r1`, Draft PR #43) | `9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5` | `SOURCE_INSPECTED` — verified via `git ls-remote origin` and `gh pr view 43`; PR #43 remains open draft and untouched |
| Historical Retail/Kernel Reference | `Kirjane/Salryn` (`archive/kernel/codex/salryn-r3-canonical-integration-r1`) | `607fcc4b17e2c069d0fd36d0646c554940977e2d` | `SOURCE_INSPECTED` — verified in local repo |
| Operation Forge Source | `Kirjane/Operation-FORGE.kirion` (`.forge/source-manifest.json`) | `0e8765de6e0e87ea9b4503714f08fa650e1a79c6` | `SOURCE_INSPECTED` — verified in `Operation-FORGE.kirion` checkout and `.forge/source-manifest.json` |

## 2. Isolated Recovery Branch & Worktree Custody

- **Recovery Branch:** `workstreams/salryn-canonical-c1-recovery-proof`
- **Base Commit:** `9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5` (`origin/forge/salryn-operator-recovery-r1`)
- **Worktree Path:** `C:\Users\arane\.gemini\antigravity\scratch\Dev-Salryn-Cafe-wt\c1-recovery-proof`
- **Pre-existing Branch State:** Confirmed `workstreams/salryn-canonical-c1-recovery-proof` did not exist on `origin` before creation.
- **Preserved Environment Boundaries:**
  - All 27 pre-existing Git worktrees under `Dev-Salryn-Cafe-wt` and `Dev-Salryn-Cafe` remain untouched.
  - Protected Cafe install/data/backup paths (`C:\Program Files\Kirjane Information Technology Services - Cafe`, `C:\ProgramData\SalrynCafe`, `%LOCALAPPDATA%\SalrynCafe`, `C:\Users\arane\Salryn-Cafe-InPlace-QA\run-20261123-222727-d6520945\backups`) remain untouched.
  - Protected Retail paths (`C:\ProgramData\Salryn`, `%LOCALAPPDATA%\Salryn`) remain untouched; `Reset-SalrynLocalState.ps1` is never executed.

## 3. Forge Runtime Qualification & Ponytail Activation

- **Execution Mode:** `MANUAL_AGENT_EXECUTION_WITH_SKILLS` (not `HARNESS_ORCHESTRATED`).
- **Skills Loaded:** `.agents/skills/kirion-core/SKILL.md`, `.agents/skills/kirion-dispatch/SKILL.md`, `.agents/skills/kirion-frontend/SKILL.md`, and `C:\Users\arane\.gemini\config\skills\ponytail\SKILL.md` (Ponytail `full`, aligned with `DietrichGebert/ponytail`).
- **Runtime Capability Handshake:**
  - `ACTIVE_VERIFIED`: Kirion skill loading, native `invoke_subagent` (bounded to max 2 temporary read-only reviewers; no persistent Swarm), manual frozen-SHA non-author reviewer dispatch.
  - `AVAILABLE_NOT_ACTIVATED`: `.forge/vendor/kirion/integrations/antigravity/factory/{scheduler,dispatcher,ownership,evidence,recovery}.py` and `forge_guard.py` / `scripts/forge/codex_guard.py` (present on disk, not hooked as active Antigravity host enforcement).

## 4. Installed `/cafe/` Bundle vs Legacy Root SPA & Release Isolation

1. **Legacy Root SPA (`src/Salryn.App.Web`):**
   - Served by `LocalApiApplicationFactory.cs:229-252` (`ResolveWebDistPath()` checks `<base>/dist`, `<base>/wwwroot`, `<repo>/src/Salryn.App.Web/dist`) at root `/`.
   - In `installer/Build-SalrynCafeInstaller.ps1:54-59,109-116`, `src/Salryn.App.Web` is **not built or packaged** into the installer unless `-IncludeLegacyRootShell` is explicitly passed.
2. **Installed Cafe Bundle (`apps/cafe-web` mounted under `/cafe/`):**
   - Built by `Build-SalrynCafeInstaller.ps1:54` (`npm run build` in `apps/cafe-web`, with `base: "/cafe/"` in `apps/cafe-web/vite.config.ts:6`).
   - Staged into `cafe-dist/` (`Build-SalrynCafeInstaller.ps1:105-107`).
   - Mounted by `LocalApiApplicationFactory.cs:254-297` (`app.Map("/cafe", ...)`) and opened by `Salryn.Desktop.WebView/DevHostConfig.cs:47` (`http://127.0.0.1:5399/cafe/`).
3. **Why the C1 Proof Route Cannot Ship Accidentally in Production Installers:**
   - **Build-time Gate (`apps/cafe-web/src/cashier/main.tsx`):** `/cafe/recovery/c1` is gated behind `import.meta.env.DEV || import.meta.env.VITE_ENABLE_C1_RECOVERY === "1"` using dynamic `lazy(() => import(...))`. During `Build-SalrynCafeInstaller.ps1` (`npm run build` with `VITE_ENABLE_C1_RECOVERY` unset), the condition is statically `false`, so Rollup/Vite excludes the recovery module from the emitted production bundle (`cafe-dist`).
   - **Runtime Fail-Closed Guard (`CafeMenuManagerRecovery.tsx`):** Even in a recovery-enabled build, the page checks `GET /api/test/host-identity` (which `LocalApiApplicationFactory.cs:355-374` maps only when `LocalApiApplicationOptions.TestHostNonce` is non-empty, i.e., disposable test hosts) and refuses to operate against `ProgramData\SalrynCafe` or `ProgramData\Salryn`.

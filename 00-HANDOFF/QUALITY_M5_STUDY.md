# QUALITY-M5 Study — Stable Cleanup and Installer-Readiness

**Date:** 2026-07-09  
**Branch:** `KIRCH-SANDBOX`  
**Starting HEAD:** `42af23ea` — Document post-promotion stable roadmap  
**Mode:** Study first; implement only safe cleanup that cannot change LAN/Remote POS/session behavior.

Roadmap order confirmed from `POST_PROMOTION_M4A_STABLE_ROADMAP.md`:

1. Quality cleanup ← **this lane**  
2. Installer prep  
3. RMO/BIR matrix  
4. Reports  
5. UI polish  
6. Licensing  

---

## Phase 0 stamp

| Item | Value |
|------|--------|
| Branch | `KIRCH-SANDBOX` |
| HEAD | `42af23ea` |
| `origin/KIRCH-SANDBOX` | `42af23ea` |
| `origin/KIRCH-V2-REACT-SHELL` | `d529a103` |
| React…Sandbox left-right | `0 1` (React 0 ahead, Sandbox 1 ahead) |
| Worktree | Clean |

**Branch note:** React is behind Sandbox by the post-promotion roadmap commit. Future React work must fast-forward from Sandbox first.

---

## A. Repo cleanliness

| Finding | Classification | Action |
|---------|----------------|--------|
| `bin/`, `obj/`, `dist/`, `node_modules/`, `*.tsbuildinfo` | Expected ignored build outputs | None |
| `.salryn-*.log`, `.salryn-dev/`, `.salryn-smoke/` | Expected ignored local dev | None |
| `coreSmoke.out.log` / `coreSmoke.err.log` | Ignored via `*.log` | None |
| Root `POS_STAB_BATCH_*_SEAL.md` (tracked) | Historical seal docs | Keep |
| `00-HANDOFF/**` studies | Intentional maintainer memory | Keep; do not delete |
| `*.bak` ignored; Host.Local broken bak exists locally | Local only | Do not commit; leave alone |
| Line-ending CRLF warnings on Windows | Cosmetic | No bulk reformat (risk noise) |
| Untracked source files | None at study start | OK |

**Installer readiness implication:** Data/log dirs are already gitignored; installer M1 can map production data path separately without fighting these ignore rules.

---

## B. Test / gate structure

### Targeted V2 runners (`--only`)

| Runner | Present |
|--------|---------|
| `v2-lan-bind-truth` | yes |
| `v2-lan-client-pairing` | yes |
| `v2-terminal-lifecycle` | yes |
| `v2-host-lan-control-ux` | yes |
| `v2-host-shift` | yes |
| `v2-host-checkout` | yes |
| `v2-react-checkout` | yes |

All seven are also in full CoreSmoke main path.

### Observations

| Finding | Classification | Action |
|---------|----------------|--------|
| Stale exact-copy guards largely fixed in HARDEN/M4 | Expected residual | No further brittle rewrites unless full suite fails |
| Source checks read files from disk | Fast, non-flaky | Prefer for quality work |
| Runtime shift/checkout gates spin ephemeral hosts | Slower; can lock DLLs if API running | Document: stop `Salryn.Api.Local` or use `/p:BuildProjectReferences=false` |
| MSBuild child-node flakes occasionally | Environment | Retry with `-m:1` |
| No dedicated `v2-remote-pos-confirm-actions` runner | Confirmations folded into `v2-lan-client-pairing` | Acceptable; do not over-add runners |
| Full CoreSmoke still runs large historical suite | Slow but valuable regression net | Keep; do not thin for “speed” without maintainer OK |

---

## C. React code risk

| File | Size | Risk | Refactor now? |
|------|------|------|----------------|
| `useRemotePosClient.ts` | ~890 lines | **High maintainability risk** — state machine, restore, pairing, cart/checkout in one hook | **Defer** structural split (behavior risk). Safe: dedupe host URL normalize only |
| `HostLanControlPage.tsx` | ~688 lines | Medium — wizard + pairing + terminals in one page | **Defer** split until UIUX-M1 |
| `lanClientStorage.ts` | focused | Low | Good boundary already |
| `lanBindNormalize.ts` | focused | Low | Mirrors backend; intentional client-side form validation |
| Confirmation strings | in page | Low | Already guarded by source checks |

### Duplication

- `normalizeHostBaseUrl` in `useRemotePosClient.ts` **duplicates** `normalizeLanHostBaseUrl` in `lanClientStorage.ts` (identical trim/trailing-slash logic).
- Bind normalizers exist in C# and TS by design (server authoritative, client UX). **Do not unify across languages.**

### TypeScript

- `tsc -b` clean (exit 0) at study time.

---

## D. Backend code risk

| File | Size | Risk | Notes |
|------|------|------|-------|
| `LocalLanControlService.cs` | ~365 lines | Low–medium | Save path normalizes bind; repair + clear-sessions present; host-local only |
| `LocalApiLanRuntimeStatusService.cs` | ~221 lines | Low | Small dead-code smells: redundant ternary; empty `if (listensOnConfiguredLan)` block |
| `LocalApiLanBindNormalizer.cs` | focused | Low | Good single purpose |
| `RemotePosOperatorSessionService.cs` | known solid | Low | Do not touch session crypto/roles |

**Do not change:** trust probe semantics, session token hashing, Admin/Owner allow-list, lan-control authorization.

---

## E. Installer-readiness notes (study only — no installer build)

| Topic | Finding |
|-------|---------|
| Publish surface | React `dist/` + `Salryn.Api.Local` static host path already known |
| Data dirs | `.salryn-dev` is dev-only; production path still open for INSTALLER-M1 |
| Secrets | No client password storage; operator tokens browser-local only |
| LAN | Explicit bind still requires host IP knowledge — installer may document, not auto-discover (no mDNS) |
| Service model | Currently `dotnet run` / manual process; installer must define host process lifecycle |
| Gates | Keep V2 CoreSmoke green as installer changes land |

---

## Decision

### GO IMPLEMENT — narrow safe cleanup only

Allowed in this task:

1. Remove trivial dead code in `LocalApiLanRuntimeStatusService` (no behavior change).
2. Dedupe host URL normalizer in Remote POS hook → use `normalizeLanHostBaseUrl`.
3. Document study + optional branch-align note for React FF.

### STOP / DEFER

| Item | Why deferred |
|------|----------------|
| Split `useRemotePosClient` into modules | High regression risk to restore/session state machine |
| Split `HostLanControlPage` | UX/regression risk; belongs to UIUX-M1 if needed |
| Delete historical handoff/POS_STAB seals | Memory, not clutter |
| Thin full CoreSmoke suite | Needs maintainer policy |
| RMO / installer / licensing / reports | Wrong roadmap lane |

---

## Post-cleanup verification plan

- `git diff --check`
- `dotnet build` tests + CoreSmoke (`BuildProjectReferences=false`)
- Targeted V2 gates + full CoreSmoke
- `npm run build`
- Commit to `KIRCH-SANDBOX` only

---

## Implementation result

| Change | Safe because |
|--------|----------------|
| `LocalApiLanRuntimeStatusService` — remove redundant ternary + empty `if` | Same readiness/problem/clientUrlReady outcomes |
| `useRemotePosClient` — use `normalizeLanHostBaseUrl` | Identical trim/trailing-slash semantics |

**Deferred:** hook/page splits, CoreSmoke thinning, historical doc deletion.

**Verification:** all targeted V2 gates, full CoreSmoke, `npm run build` — pass.

**Next lane:** INSTALLER-M1 (after quality commit lands).
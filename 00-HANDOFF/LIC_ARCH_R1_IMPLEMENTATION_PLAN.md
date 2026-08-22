# LIC-ARCH-R1 — Implementation Plan (Branch Sequence)

**Status:** Plan only. No implementation on this study branch.  
**Study base:** `KIRCH-LIC-FINAL-OFFLINE-LICENSE-LAN-ENTITLEMENTS` @ `4979b525d5a721568ddf4785f27f774e4c9cacaa`  
**Study branch:** `KIRCH-LIC-ARCH-R1-BETA-MANUAL-HOST-TERMINAL-STUDY`  
**Companion:** `00-HANDOFF/LIC_ARCH_R1_BETA_MANUAL_HOST_TERMINAL_STUDY.md`

**Product constraints:**
- DELAY INSTALLER — do not continue I1
- First five beta clients: manual offline licensing
- Private key never in Salryn POS repo
- Kirch remains project lead for branch promotion

**Protected refs (do not move):**
- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 0. Recommended stack order (before implementation)

Licensing implementation should stack on an accepted base chosen by Kirch. Suggested preference:

1. Keep architecture study parked.
2. Prefer base = latest **accepted** POS stack (at least CoreSmoke isolation + Surface4 as decided by Kirch).
3. Rebase or restack LIC-FINAL / Backup UI only under Kirch promotion policy — this plan does **not** promote parked branches.

**Do not** implement licensing on installer branch.

---

## 1. Branch: `KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION`

### Purpose

Make beta manual activation real for a **single host** (and prepare terminal document type), offline, no cloud.

### In scope

| Item | Detail |
|------|--------|
| LicenseType | Add `Host` / `Terminal` (schema v2 preferred) |
| Machine code UX | Show machine code prominently on License page when unlicensed/invalid/mismatch |
| Contact copy | Instruct customer to email **salryn.info@gmail.com** with store info + machine code |
| Fingerprint improve | Strengthen `LocalMachineFingerprint` material (document stability vs uniqueness tradeoffs); reject empty MachineBinding for production licenses |
| Host vs Terminal validation | Install/status reject wrong type for role; terminal cannot grant host-only feature set |
| ApplicationId | Require known application id constant (e.g. `Salryn.POS`) |
| StoreId | Require non-empty for beta production licenses |
| Signing payload | Include LicenseType, HostLicenseId, TerminalId when present |
| Features | Keep existing names or add aliases; map host unlock set (Pos, Reports, Backup, Licensing, Users, LanTerminals as configured) |
| Permanent license | Allow `ExpiresAt` null; Support/Updates display retained |
| No cloud | No HttpClient/Vercel/payment/subscription |
| Tests | Extend M1/M2/M3 or add `licensing-beta-manual-host-terminal` CoreSmoke checks + source guards |
| Docs | Handoff report with SHA after verification |

### Out of scope

- LAN pairing count enforcement (branch 2)
- Private key / generator code in POS repo
- Installer
- Cloud activation
- Mass feature hard-lock of every endpoint (optional soft gates only if Kirch requests)

### Acceptance criteria

- [ ] Unlicensed UI shows machine code + salryn.info@gmail.com steps
- [ ] Valid Host license installs only when MachineBinding matches and LicenseType=Host
- [ ] Terminal license rejected as host operational license (and vice versa per policy)
- [ ] Empty MachineBinding fails closed
- [ ] No private key in tree
- [ ] Focused smoke green; full CoreSmoke per Kirch policy
- [ ] Protected refs unchanged

### Suggested file touch list (when implementing)

- `LicenseContracts.cs`, validator, service, signature payload
- `LocalMachineFingerprint.cs`
- `LicensePage.tsx`, maybe `license.ts` types
- Tests + CoreSmoke hook
- Handoff report

---

## 2. Branch: `KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT`

### Purpose

Host commercially controls LAN terminal count and terminal license identity. Still no cloud.

### Depends on

Branch 1 (Host/Terminal types + machine binding quality).

### In scope

| Item | Detail |
|------|--------|
| Terminal count | Effective max terminals = min(hard capacity policy, host `AllowedLanTerminals`) when Lan entitled |
| Zero terminals | Host with AllowedLanTerminals=0 or missing Lan feature: reject new pairings for production |
| Terminal license proof | Pairing / session establishment requires terminal-presented signed Terminal license |
| Reject wrong StoreId | Terminal.StoreId must equal host StoreId |
| Reject wrong HostLicenseId | Terminal.HostLicenseId must equal host LicenseId |
| Reject machine mismatch | Terminal MachineBinding must match terminal device |
| Reject host-as-terminal | LicenseType Host cannot pair as operational terminal license |
| Reject copy | Same Terminal license on second machine fails binding |
| API / host service | Wire into `RemotePosPairingGateService` / LAN control path |
| UI honesty | License page + Host LAN page: production LAN only when entitled and enforced |
| Tests | Negative path smoke: over-count, wrong store, wrong host, host license as terminal, wrong machine |

### Out of scope

- Cloud
- Installer
- Changing LAN product architecture beyond entitlement gates
- Declaring LAN “production ready” without separate LAN acceptance by Kirch

### Acceptance criteria

- [ ] Over-count pairing denied with deterministic error
- [ ] Wrong store/host/machine denied
- [ ] Host license cannot satisfy terminal proof
- [ ] Capacity policy and license entitlement do not diverge silently
- [ ] License UI does not claim LAN production unless enforcement present
- [ ] Smoke green; protected refs unchanged

### Note on LAN production block

Even after this branch: **commercial production LAN** should remain gated until Kirch accepts LAN quality + this enforcement. Code may implement enforcement while product copy still says internal preview if LAN stack is unaccepted.

---

## 3. Branch: `KIRCH-LIC-OPS-PRIVATE-GENERATOR-SPEC`

### Purpose

Define (and preferably implement outside POS) the private issuance tool. **No private key in Salryn POS repo.**

### In scope (POS repo)

- Spec document only (or thin public payload fixture format for tests using **test** keys never used in production)
- Exact signing payload canonicalization matching POS verifier
- Host and Terminal issuance field checklist
- Customer email SOP for salryn.info@gmail.com
- Reissue / machine replacement SOP
- Record format for offline registry (CSV/JSON for first five clients)

### In scope (private `Salryn.LicensingOps` — outside POS)

- Private key storage
- Sign CLI or small internal UI
- Issue Host / Terminal licenses
- Never commit production private key to public or POS repos

### Out of scope

- Vercel
- Customer self-serve portal
- Payment processing

### Acceptance criteria

- [ ] Spec matches POS `BuildSigningPayload` field set and algorithms
- [ ] Documented workflow for beta issuance for five clients
- [ ] Explicit “no private key in Salryn” checklist
- [ ] Test vectors: public-key-only verification samples (prod public key + sample signed by offline ops) without embedding private key

---

## 4. Future only: `KIRCH-CLOUD-LICENSING-VERCEL-ACTIVATION`

### Purpose

Online activation and lifecycle after beta manual path is proven.

### In scope (future)

- Activation code generation (not the license itself)
- License record database
- Activation limits
- Machine reset / reissue portal
- Upgrade terminal counts
- Private signing key custody in backend
- Audit trail
- Customer/admin portal

### Explicit non-goals for beta

- Not required for first five clients
- Must not make daily POS internet-dependent

### Suggested sequencing relative to product

Only after:

1. Branch 1 shipped and used for real beta hosts  
2. Branch 2 if any multi-terminal beta  
3. LicensingOps operational for reissue  

---

## 5. Explicitly NOT next

| Item | Why not |
|------|---------|
| Installer I1 / packaging | DELAY INSTALLER until licensing architecture locked and Kirch reopens |
| Payment / subscription sync | Commercial model is permanent one-time license |
| 16-digit key as sole license | Weak; reserve for future activation codes |
| Private key commit to Salryn | Security boundary |
| One combined license for host+all terminals | Undermines machine binding and resale control |
| RMO/BIR licensing claims | Separate compliance lanes |
| Hardware / Surface / printer licensing scope expansion | Out of licensing architecture study |
| Broad silent feature lockout without beta SOP | Risk of bricking beta sites mid-support |
| Cloud revocation as beta blocker | Offline permanent license model |

---

## 6. Cross-cutting engineering rules (all implementation branches)

1. **Evidence-first:** each branch ships report with gates actually run or honest NOT RUN.
2. **Source guards:** no PRIVATE KEY, no Vercel/payment/subscription in licensing slices unless cloud branch.
3. **Admin-only import:** keep Settings.Manage; cashiers/managers excluded.
4. **Loopback license APIs:** preserve local operator guard.
5. **Backup boundary:** DB backup package does not include license unless a later dedicated design overrides this plan with Kirch approval.
6. **Protected refs:** never move SANDBOX or INSTALLER-M1.
7. **No branch promotion** without Kirch.
8. **Stack honesty:** ungated parked code is not production proven.

---

## 7. Suggested verification matrix (per branch)

| Check | Branch 1 | Branch 2 | Branch 3 (spec) | Cloud future |
|-------|----------|----------|-----------------|--------------|
| `dotnet build` | Yes | Yes | N/A if docs-only | Yes |
| licensing-m1/m2/m3 | Yes | Yes | N/A | Yes |
| New beta/LAN checks | Yes | Yes | N/A | Yes |
| Web typecheck/build if UI | Yes | If UI | No | Yes |
| `git diff --check` | Yes | Yes | Yes | Yes |
| Private key scan | Yes | Yes | Yes | Yes |
| Full CoreSmoke | Per Kirch | Per Kirch | No | Per Kirch |

---

## 8. Decision gate before coding Branch 1

Kirch should confirm (expected answers from study):

1. Cloud deferred for five clients — **YES**  
2. Generator outside POS — **YES**  
3. No private key in POS — **YES**  
4. Separate host/terminal licenses — **YES**  
5. No 16-digit key as license — **YES**  
6. Show email + machine code — **YES**  
7. License outside DB backup — **YES**  
8. LAN production blocked until enforcement + acceptance — **YES**  
9. Installer delayed — **YES**  

When confirmed, implement **only** `KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION` next.

---

## 9. Plan status

`PLAN_READY_AWAITING_KIRCH_AUTHORIZATION`

No code was implemented under this plan document on the study branch.

# POST-PROMOTION-M4A — Stable Repo Handoff and Next-Work Roadmap

**Date:** 2026-07-09  
**Stable branch:** `KIRCH-SANDBOX`  
**Stable HEAD:** `d529a103` — Confirm Remote POS sign out and clear pairing actions  
**Promotion:** PROMOTE-REACT-LAN-POS-M4 (fast-forward only from `KIRCH-V2-REACT-SHELL`)

---

## 1. Stable milestone summary

`KIRCH-SANDBOX` is **stable** at **`d529a103`**.

This promotion includes:

- **LAN bind truth** — explicit IPv4 bind, no wildcard/`0.0.0.0`, runtime readiness from actual listen URLs, bind normalization/repair
- **Host LAN Control UX** — human-friendly LAN connection wizard, repair/activate/disable, grouped terminal rows, client troubleshooting guide
- **Remote POS pairing persistence** — guided state machine, localStorage pairing, trust probe, revoke/stale clear
- **Remote POS operator session persistence** — token-only storage, host validation before S6, remember-session preference
- **Remote terminal lifecycle UX** — Active / Disabled / Revoked honesty; re-enable not supported; revoked requires re-pair
- **React checkout workstation** — host-local POS checkout UI with shift/stock honesty
- **Host shift/checkout backend gates** — V2 CoreSmoke runners for host shift and host checkout
- **Dev DB reset/seed helpers** — flow-reset tooling for development only
- **Full CoreSmoke green** by the promotion report (pre- and post-merge)

Related handoff notes:

- `00-HANDOFF/V2_CANDIDATE_COMPLETION_MAP_M3.md`
- `00-HANDOFF/V2_CANDIDATE_FINAL_ACCEPTANCE_CHECKLIST_M3.md`
- `00-HANDOFF/V2_REMOTE_POS_HARDEN_M3_STUDY.md`

---

## 2. Still not included

Explicitly **not** part of this stable milestone:

- **RMO/BIR accreditation** — not complete; no compliance claim
- **Official receipt / official output** — not implemented
- **Printer / PDF / Crystal** — not implemented
- **Product mutation UI** — not implemented
- **Licensing** — not implemented
- **Production installer** — not implemented
- **V2 complete:** **no**

Remote Admin/Owner login remains **forbidden**.  
Host Checkout on LAN client remains **forbidden**.  
`/api/lan-control` remains **host-local only**.

---

## 3. Next roadmap order

Recorded order for post-stable work:

1. **Quality cleanup**
2. **Installer prep**
3. **RMO/BIR matrix**
4. **Reports**
5. **UI polish**
6. **Licensing**

Do not reorder casually. Later lanes depend on earlier foundations (especially installer paths before licensing; RMO matrix before official report engines).

---

## 4. Meaning of each next lane

### QUALITY-M5

- Remove stale guards and dead code
- Reduce huge-hook risk (e.g. large Remote POS client hook complexity)
- Inspect duplication
- Improve naming where it aids clarity
- Keep all gates green
- **No new product features**

### INSTALLER-M1

- Produce an **MSI**
- Publish MSI as a **GitHub Release** asset
- Install local app/runtime cleanly
- Configure data directory
- Start menu shortcut
- Uninstall behavior
- **No licensing yet** except an explicit placeholder if needed for layout only

### RMO-BIR-M1

- **Requirements matrix only**
- **No claims of compliance**
- Map current Salryn support vs gaps
- Determine official output/report requirements **before** any Crystal or official-print work

### REPORTS-M1

- Decide report engine **after** RMO matrix
- Crystal Reports only if requirements justify it
- Otherwise consider HTML/PDF/text export architecture
- **No official claims** until the RMO matrix is complete

### UIUX-M1

- Polish **stable** screens only
- **No new backend behavior**
- Improve host dashboard, checkout, LAN control, and Remote POS mobile layout

### LICENSE-M1

- **Study-first** licensing
- Activation **may** require internet
- Daily POS must work **offline after activation**
- Possible Vercel license API (or equivalent)
- Local **signed license cache**
- **No cloud dependency for checkout/LAN**
- **No implementation** until installer and data paths are stable

---

## 5. Licensing policy note

Licensing must not make LAN checkout or local sales dependent on internet. Internet may be required for installation-time or first-run activation. After activation, Salryn should use a signed local license file and allow offline operation with a clear grace policy.

---

## 6. Installer policy note

Installer target is a GitHub Release MSI artifact first. Do not build auto-update, licensing, or cloud sync into installer M1 unless separately approved.

---

## 7. Promotion discipline

- **`KIRCH-SANDBOX` is protected stable**
- Future work should **branch from Sandbox**
- **Verify before merge** (targeted gates, full CoreSmoke where practical, `npm run build` for React changes)
- **Promote only by explicit maintainer instruction**
- Prefer fast-forward promotion; no force push; no casual merge commits to Sandbox

---

## Honest residual

This roadmap does **not** make Salryn “V2 complete.” It freezes a verified React/LAN/Remote POS/Checkout candidate and sequences the next maintainer lanes without inventing features or compliance.

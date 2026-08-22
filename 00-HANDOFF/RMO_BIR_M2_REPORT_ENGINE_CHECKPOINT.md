# RMO-BIR-M2 Report Engine Checkpoint

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Disclaimer:** Engine choice is deferred. No Crystal/PDF implementation.

---

## 1. Is Crystal Reports justified now?

**No.**

Reasons:

1. Official **data contracts** are not frozen in product code yet (this M2 defines them as design only).  
2. Live **official post path** does not exist — nothing authoritative to bind templates to.  
3. Crystal adds Windows runtime, licensing cost, deployment complexity, and version coupling.  
4. Many required outputs (X/Z, EJ export, audit) fit **structured data → text/HTML/PDF** generators.  
5. M1 matrix explicitly routes REPORTS-M1 **after** contracts; CRYSTAL-M1 only if justified later.

---

## 2. What must be true before choosing Crystal?

| Prerequisite | Why |
|--------------|-----|
| Stable OfficialSaleDocument contract v1 | Template field binding |
| At least one real posted official sample dataset | Demo/evidence |
| Confirmed need for **pixel-perfect paginated** layouts that HTML/PDF cannot meet | Avoid unnecessary dependency |
| Installer/runtime path to ship Crystal runtime or viewer | Deployment |
| Cost/license approval | Business |
| Maintainer explicit GO | Process |

---

## 3. Contracts that must be stable first

1. **Official sale document** (header, lines, tax, payments, markers)  
2. **X-reading** summary  
3. **Z-reading** summary + number range  
4. **Electronic journal export** shape (even if eSales format still open)  
5. **Void/refund** document links  

Until these are versioned, any Crystal RPT is throwaway work.

---

## 4. Risks of Crystal deployment/runtime

| Risk | Severity |
|------|----------|
| Extra redistributable / license on POS PCs | High |
| Harder CI smoke without Crystal installed | High |
| Template drift vs contract version | High |
| Offline POS must not depend on network for Crystal server | Medium (local RPT OK) |
| Skill/tooling lock-in | Medium |

---

## 5. Is HTML/PDF/text export still viable?

**Yes — preferred default for REPORTS-M1.**

| Output | Viable engines |
|--------|----------------|
| Internal X/Z preview | HTML + print CSS; plain text (existing text layout builders) |
| Examiner samples | PDF via library or print-to-PDF; text files |
| EJ export | JSON/CSV first |
| Backend sales books | HTML/PDF tables |

Repo already has **text layout builders** for official audit evidence — extend that pattern before Crystal.

---

## 6. Which outputs may need exact paginated layout?

| Output | Pagination pressure | Engine guess |
|--------|---------------------|--------------|
| Principal SI/Invoice tape/A4 | Medium–High | HTML/PDF first; Crystal only if rejected by TWG samples |
| X/Z reading | Medium | Text/HTML often enough |
| Backend multi-page books | Medium | PDF tables |
| EJ dump | Low | Text/JSON |

**Until a failed TWG demo or explicit legal template requires Crystal, do not start CRYSTAL-M1.**

---

## 7. Checkpoint decision

| Question | Answer |
|----------|--------|
| Crystal now? | **No** |
| REPORTS-M1 can start templates? | **After** M3 contracts land in code **and** at least a non-production official sample path exists |
| Preferred engine today | **Text / HTML / PDF library** |
| Crystal decision ready? | **No** |

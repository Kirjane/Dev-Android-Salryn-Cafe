# RMO-BIR TRAD-M1 â€” Taxpayer Mode and Invoice Policy Spine Study

**Date:** 2026-07-10
**Branch:** `KIRCH-RMO-BIR-TRAD-M1-TAXPAYER-MODE-AND-INVOICE-POLICY-SPINE`
**Base:** QA1 `ef36a3f0a3816b44c4b2b13bbfa34460eab29daf`
**Decision:** **GO WITH MIGRATION v15**

---

## 1. Problem statement

Salryn must support **VAT-registered** and **non-VAT** MSME installations without allowing the operator to casually choose contradictory tax behavior at checkout. Official sample invoice formats distinguish VAT Invoice, Non-VAT Invoice, exempt, zero-rated, and non-VAT mixed sales.

Repo already has:

- setup contract field `VatRegistrationMode` (always read back as `null`)
- product tax classifications (VATable / exempt / zero-rated / NonVat)
- inclusive 12% VAT bucket engine
- tax buckets in snapshot/EJ evidence contracts

Gaps:

- mode not persisted on `bir_machine_profiles`
- non-VAT sales collapsed to one generic bucket (no SSPT vs exempt)
- non-VAT engine accepts lines too loosely
- no invoice-first policy object / versioned effective dates
- product classification not constrained by taxpayer mode

---

## 2. Decisions

### 2.1 Business registration modes (persisted string codes)

| Code | Meaning | Engine taxpayer mode |
|------|---------|----------------------|
| `VAT_REGISTERED` | VAT-registered taxpayer | `VatRegistered` |
| `NON_VAT_PERCENTAGE_TAX` | Non-VAT, percentage tax (SSPT path) | `NonVatPercentageTax` |
| `NON_VAT_EXEMPT` | Non-VAT, exempt-only path | `NonVatExempt` |

Unset (`NULL` / empty) is allowed for incomplete setup but **fails closed** for any official tax computation path that requires a mode.

### 2.2 Allowed line classifications

| Business mode | Allowed product/line classifications |
|---------------|--------------------------------------|
| `VAT_REGISTERED` | `Vatable`, `VatExempt`, `ZeroRated` |
| `NON_VAT_PERCENTAGE_TAX` | `NonVat` (SSPT), `NonVatExempt` |
| `NON_VAT_EXEMPT` | `NonVatExempt` only |

Invalid combinations fail validation (fail closed).

### 2.3 Product enum evolution (backward-safe)

| Value | Name | Notes |
|------:|------|-------|
| 1 | `Vatable` | unchanged |
| 2 | `VatExempt` | unchanged |
| 3 | `ZeroRated` | unchanged |
| 4 | `NonVat` | **SSPT / sales subject to percentage tax** (display text updated) |
| 5 | `NonVatExempt` | **new** â€” non-VAT exempt sales |

Existing products with classification `1` (default from v8) remain VATable. Operators of non-VAT shops must reclassify products under Official Setup discipline before official issuance (later readiness gate).

### 2.4 Bucket kinds

| Kind | Use |
|------|-----|
| `VatableSales` / `VatAmount` | VAT path |
| `VatExemptSales` / `ZeroRatedSales` | VAT path exempt / zero-rated |
| `SsptSales` | Non-VAT percentage-tax sales |
| `NonVatExemptSales` | Non-VAT exempt sales |
| `NonVatSales` | **legacy shape only** â€” engine no longer prefers it for new non-VAT drafts |

### 2.5 Invoice-first document policy

- Primary sales document: **Invoice** (`OFFICIAL_INVOICE`)
- OR remains supported as document kind string for supplementary/legacy profile text only
- VAT sellers: operational policy = issue invoice for every completed POS sale
- Non-VAT statutory threshold: stored as **versioned policy constant with explicit â€œCPI-adjustable / not hard-coded in checkoutâ€ note**; Salryn MSME operational default for this spine = **issue invoice for every completed POS sale** (stricter than minimum statute) â€” documented, not a legal claim of sufficiency

### 2.6 Inclusive VAT calculation / rounding

- Keep existing engine: inclusive 12%, half-away-from-zero to integer cents
- Rate constant remains engineering policy (`SalrynOfficialTaxBucketPolicy.VatInclusiveRatePercent = 12`)

### 2.7 Mixed-sale discount allocation

- **VAT path, sale-level discount:** continue existing vatable-gross-only adjustment when discount applied to VATable portion; if future mixed discount spanning exempt/zero-rated is needed, allocate **proportionally by line gross** and recompute inclusive VAT only on remaining VATable gross
- **Non-VAT path:** proportional allocation of sale-level discount across SSPT and exempt grosses by pre-discount line totals

### 2.8 Immutable tax snapshot

- Official sale evidence already embeds tax bucket draft JSON
- TRAD-M1 freezes **setup mode code + policy version + engine mode** into a small immutable `OfficialTaxPolicySnapshot` used by later posting/output milestones
- Changing setup mode after issuance must not rewrite historical snapshots (append-only evidence rule preserved)

### 2.9 Schema / migration

**v15 required:**

```sql
ALTER TABLE bir_machine_profiles
  ADD COLUMN vat_registration_mode TEXT NULL;
```

- No default mode inventing VAT for existing installs
- CHECK optional in SQLite via app validation (strict enum parse on save/read)
- `SupportedMigrationVersion` â†’ **15**
- DB-A2 future probe â†’ **16**
- QA1 AllMigrationsOrdered includes v15
- Critical tables catalog: no new critical table (column on existing `bir_machine_profiles`)

### 2.10 UI safeguards

- Official Setup business form: required select for VAT registration mode (empty = not set)
- Save rejects unknown codes
- Product tax classification display options remain full list in catalog UI; **mode-constrained validation** runs when computing tax buckets / readiness (not silent coercion)

---

## 3. Scope of implementation (this branch)

In:

- migration v15 + catalog + factory + QA1 migration list
- policy types + combination matrix
- invoice document policy
- setup save/read persistence of mode
- tax bucket engine + validator SSPT/exempt
- product classification enum + mapper
- Official Setup UI mode field
- `rmo-bir-trad-tax-invoice-policy` CoreSmoke gate
- study + decision + report handoffs

Out:

- live atomic posting (TRAD-M2)
- real invoice print/PDF (TRAD-M3)
- void/refund docs (TRAD-M4)
- X/Z/eSales (TRAD-M5)
- installer (TRAD-M6)
- Annex F demo (TRAD-M7)
- eAccReg package (TRAD-M8)

---

## 4. GO / STOP

**GO WITH MIGRATION v15** â€” narrow, reversible, necessary for MSME VAT/non-VAT spine; no live posting; no compliance claim.

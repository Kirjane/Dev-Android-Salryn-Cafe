# RMO-BIR-M2 Business / Taxpayer / Machine Config Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Existing schema anchor:** `bir_machine_profiles` (J21-2)  
**Disclaimer:** Design only. Not accreditation.

---

## A. Taxpayer / business profile

| Field | Source | Current support | Missing | Risk |
|-------|--------|-----------------|---------|------|
| Registered taxpayer name | S2 header | **Yes** — `registered_taxpayer_name` | Operator UI validation | high |
| Business name / style | S2, RMC 55-2019 | **Yes** — `business_name` nullable | UI | high |
| Registered address | S2 | **Yes** — `registered_address` | UI | high |
| Branch address | S2 | **Yes** — `branch_address` | UI | medium |
| TIN (9-digit) | S2 | **Yes** — `taxpayer_identification_number` | Format validation | high |
| Branch code (5-digit) | S2 | **Yes** — `branch_code` | Format validation | high |
| VAT vs NON-VAT mode | S2 header phrase | **Partial** — store tax mode / draft tax mode; not fully on profile | Explicit profile tax registration mode | high |
| Contact phone/email | Weak | **No** dedicated | Optional later | low |
| Store / branch identifier | Inventory | **Partial** — `store_settings_id`, scope code | Multi-branch policy | medium |
| Supplier accreditation (footer party) | S2 footer | **Missing** as separate supplier entity | Supplier config for footer | high |

**Note:** External **Certificate of Accreditation / PTU issuance** remains a BIR process. Salryn only **stores** taxpayer-entered permit metadata for output and gating.

---

## B. Machine / POS profile

| Field | Source | Current support | Missing | Risk |
|-------|--------|-----------------|---------|------|
| Machine Identification Number (MIN) | S2, S5 | **Yes** — `machine_identification_number` | UI + required when official | high |
| Machine serial number | S2, S5 | **Yes** — `machine_serial_number` NOT NULL | UI | high |
| POS terminal number | Inventory | **Yes** — `pos_terminal_number` | Map vs LAN terminal registry | medium |
| Brand/model | S5 PTU content | **Missing** columns | Optional fields | medium |
| Software name/version/build | S2 evidence | **Yes** — defaults `Salryn` / `UNSET` | Stamp from real build | high |
| Accreditation number | S2 footer | **Yes** nullable | UI + date pair | high |
| PTU number | S2, S5 | **Yes** — `permit_to_use_number` | UI | high |
| PTU issued at | S5 | **Yes** — `permit_to_use_issued_at` | UI | medium |
| Accreditation issued / valid until | S2, S8 | **Yes** | UI + expiry warning | medium |
| `compliance_mode` | Engineering gate | **Yes** — default `NOT_ACCREDITED` | Operator cannot casually set ACCREDITED without audit | **blocker** |
| `official_output_enabled` | Engineering gate | **Yes** — CHECK with ACCREDITED only | Service enforce | **blocker** |
| `is_active` | Engineering | **Yes** | Single-active profile rule | high |

---

## C. Invoice / receipt profile (configuration)

| Field | Source | Current support | Missing | Risk |
|-------|--------|-----------------|---------|------|
| Document title / kind | S6/S7 | Sequence `document_kind` OR/Invoice | Product default policy | high |
| Series prefix | F-h | Sequence `prefix` | Setup UI | high |
| Start / current / end number | F-h | Sequence columns | Setup UI + exhaustion | high |
| Padding digits (≥6) | S2 | Default 6 | UI | high |
| Footer legal text | S2 | Not first-class config | Template store | high |
| Supplementary disclaimer text | S2 | Missing | Template | high |
| SC/PWD capture enabled flag | S2 | Missing | Config flag later | medium |
| Training mode global flag | Integrity | Missing | Config + watermark | high |
| Reprint label text | F-i | Missing | Template | medium |
| Supplier footer block | S2 | Missing entity | Config table or profile extension | high |

---

## D. Security / config controls

| Control | Proposal | Current | Risk |
|---------|----------|---------|------|
| Who can edit profile | Host Admin/Owner only; never remote | Partial permissions | high |
| Audit on edit | Before/after snapshot of profile fields | Partial audit services | high |
| Effective dating | Optional; MVP: edits only if no posted official for profile or append-only history | Missing history table | medium |
| Immutable past transaction metadata | Snapshot denormalizes header at post | Draft builder exists | high |
| Cannot retro-edit posted sale header | Integrity spine | Not enforced on live path | **blocker** |
| ACCREDITED mode change | Dual control / reason code + audit | CHECK constraint only | high |

---

## E. Active profile selection rule (proposal)

1. At most **one** `is_active = 1` machine profile per local store scope.  
2. Official allocation requires: `is_active` + `compliance_mode = ACCREDITED` + `official_output_enabled = 1` + valid sequence.  
3. Host checkout **default remains non-official** until product feature “Official mode” is explicitly turned on **and** readiness service passes.  
4. LAN Remote POS **never** reads PTU as authority to print official SI/OR remotely.

---

## F. Schema change guidance for M3+

| Change | Needed now? |
|--------|-------------|
| New core profile columns for brand/model/supplier footer | Optional later |
| Profile edit history table | Recommended before production official mode |
| **No schema in M2** | Confirmed — study only |

---

## G. Implementation lane

**RMO-BIR-M3** should prioritize:

1. Host-only readiness API: “may issue official documents?”  
2. Read models for active profile + active sequences  
3. Config edit audit contracts  
4. UI only after contracts (can be M3b)

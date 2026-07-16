# PRE-BETA-H — Capability Matrix Seal Report

## 1. Branch / base

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-H-CAPABILITY_MATRIX_SEAL` |
| Base | `KIRCH-PRE-BETA-G-PRINTER_HARDENING` @ `b1956500e230b64bbd31bb85127f92901d07d50a` |
| Final SHA | `4f709a5305cf3632044395e5855d00a58be15a8c` |

## 2. Decision

**`BETA_CAPABILITY_MATRIX_SEALED`**

## 3. Delivered

- Created `00-HANDOFF/BETA_CAPABILITY_MATRIX.md` with SUPPORTED / CODE-PROVEN / HARDWARE-PROOF-PENDING / NOT SUPPORTED / FORBIDDEN CLAIMS
- Clarified `09-DEPLOYMENT/DEPLOYMENT_LOCK.md` for single-host DB + licensed LAN terminals
- Marked `00-HANDOFF/V1_BUILD_ORDER.md` historical; ESC/POS step obsolete/forbidden
- Cross-linked `06-HARDWARE/HARDWARE_LOCK.md` and updated `01-FEATURE-LOCK/FEATURE_LOCK.md` for logo + portable backup
- Guard: `PreBetaHCapabilityMatrixSealChecks`

## 4. Gates

| Gate | Result (on H tip `4f709a5…`) |
| --- | --- |
| `dotnet build Salryn.sln` | PASS |
| full CoreSmoke | PASS |
| web build | PASS |
| `git diff --check` | PASS |

## 5. Non-claims

- Scope not weakened; only clarified
- No installer/I1 packaging
- No hardware proof invented

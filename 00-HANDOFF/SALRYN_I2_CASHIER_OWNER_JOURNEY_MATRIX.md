# Salryn I2 Cashier / Owner Journey Matrix

Tested implementation SHA: `73deac6d16e59b58eec79e87e0dfa62c89fbe99e`

| Journey | Evidence | Result |
|---|---|---|
| Barcode scan, cart, F9 review | Playwright interaction + baselines 10/12/13 | PASS MOCKED |
| Cash, over-tender/change, full Utang and split presentation | checkout/U1 CoreSmoke + baselines 10/11 | PASS |
| Linked cash collection versus noncash | W4/U1 source-event CoreSmoke | PASS |
| Blind physical count and explicit confirmation | integrated W4 spec | PASS MOCKED |
| Physical zero versus zero variance | `w4-physical-zero-shortage-1366x768.png` | PASS |
| Ambiguous committed close retry | same request ID twice; no duplicate close | PASS |
| 400/401/403/409 deterministic responses | four executable cases | PASS |
| Missing source coverage / no force-close | 1024 screenshot and assertion | PASS |
| Long and stacked financial toasts | 390, 1024, 125%, 150%; actual bounding boxes | PASS |
| Receipt detail and void controls | baseline 15 + W4 sale/void CoreSmoke | PASS; UI mutation remains synthetic |
| Utang collection/reversal controls | baseline 16 + U1 CoreSmoke | PASS; UI mutation remains synthetic |
| Owner totals and AR aging | analytics/report CoreSmoke + baselines 17/18 | PASS |
| Reload/back report drilldown | two executable navigation tests | PASS |
| Users, audit, settings, backup, license | page render/action inventory + backup executable assertion | PASS for named assertions; see machine-readable map |
| Terminal/internal/future-only screens | baselines 27 and 38-44 | MOCKED_RENDERED / NOT_APPLICABLE_INTERNAL |
| Licensed disposable Host | live no-mock harness | BLOCKED_NO_HOST |
| Physical operator/drawer | supervised store exercise | NOT_RUN |

Mocked UI evidence is not relabeled as live financial or physical acceptance.

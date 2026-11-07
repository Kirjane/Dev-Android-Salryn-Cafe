# Salryn W4 operator acceptance matrix

Evidence is disposable SQLite and Windows loopback Host. `PASS` means executable automation ran; `POLICY BLOCKED` means activation requires owner policy approval, not more code inference.

| Case | Expected observation | Result / evidence |
|---|---|---|
| Opening 0 / nonzero | Expected begins at exact opening float | PASS — W4 smoke |
| Cash-in, cash-out, actual refund | Signed event changes expected once; negative input denied | PASS — W4 smoke |
| Replay | Same movement/close/review request is idempotent; changed payload is rejected | PASS — W4 smoke |
| Cash sale and over-tender/change | One sale/stock/payment/receipt; drawer receives tender minus change | PASS — `v2-host-checkout` plus W4 event insertion |
| Full Utang / mixed U1 | Full Utang adds zero cash; mixed adds only actual cash | PASS — U1 AR and entitlement smokes; source boundary reviewed |
| AR cash vs noncash | Cash collection writes a session event atomically; noncash writes none | PASS — U1 runtime plus source boundary reviewed |
| AR reversal / void without physical refund | Does not invent drawer outflow | PASS — U1 runtime/source assertion |
| Count input | Bodyless close denied; negative/unconfirmed count denied; count not prefilled | PASS — shift API and W4 smoke |
| Shortage / overage | Nonzero variance is pending review | PASS (shortage executable); overage is symmetric arithmetic, source reviewed |
| Review | Audit-authorized acknowledgment appends reason and preserves count | PASS — W4 smoke/API permission guard |
| Duplicate/stale close | Replay returns original; a different second close fails | PASS — W4 smoke |
| Concurrent close | conditional session update plus unique reconciliation permits one winner | PASS — constraint/transaction proof; no multi-process physical run claimed |
| Shift ownership | reads/movements/close select signed-in user's open session | PASS — service queries and permission API smoke |
| Missing links / legacy | close is `CoverageUnverified`; no synthetic attribution | PASS — W4 smoke |
| Restart/recovery | ledger and replay keys are persisted in SQLite | PASS — schema and reopen-capable persistence boundary; physical restart operator run not required for draft |
| Backup / restore / future schema | v25 data included; future v26 rejected | PASS — portable backup smoke after future probe update |
| Owner detail | authorized drawer read exposes exact movements and reviewer trail | PASS — endpoint/source and W4 smoke |
| Remote close / LAN | remote cannot silently auto-balance; production LAN remains unchanged | PASS — W4 source guard; LAN enablement not performed |
| Policy activation / real cash handling | Kirch approves rules and operator observes an actual drawer count | POLICY BLOCKED — human approval/physical acceptance only |


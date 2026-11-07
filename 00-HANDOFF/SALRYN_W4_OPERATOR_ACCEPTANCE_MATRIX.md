# Salryn W4 operator acceptance matrix

Evidence is disposable SQLite and Windows loopback Host. `PASS` means executable automation ran; `POLICY BLOCKED` means activation requires owner policy approval, not more code inference.

| Case | Expected observation | Result / evidence |
|---|---|---|
| Opening 0 / nonzero | Expected begins at exact opening float | PASS — W4 smoke |
| Cash-in, cash-out, actual refund | Signed event changes expected once; negative input denied | PASS — W4 smoke |
| Replay | Same movement/close/review request is idempotent; changed payload is rejected | PASS — W4 smoke |
| Close payload replay | Same close request/count/note/operator returns original; changed count or note conflicts and original remains immutable | PASS — red at `c96c51e`, green in R2 W4 smoke |
| Sale source equality | Missing/wrong amount, direction, method, source, actor or session cannot produce `Verified` | PASS — disposable SQLite tamper matrix |
| AR source equality | Linked cash collection requires exact event; missing/wrong amount or method blocks; noncash adds zero | PASS — disposable SQLite tamper matrix |
| Cash sale and over-tender/change | One sale/stock/payment/receipt; drawer receives tender minus change | PASS — `v2-host-checkout` plus W4 event insertion |
| Full Utang / mixed U1 | Full Utang adds zero cash; mixed adds only actual cash | PASS — U1 AR and entitlement smokes; source boundary reviewed |
| AR cash vs noncash | Cash collection writes a session event atomically; noncash writes none | PASS — U1 runtime plus source boundary reviewed |
| AR reversal / void without physical refund | Does not invent drawer outflow | PASS — U1 runtime/source assertion |
| Cross-user close replay | Same close request ID under another operator/session conflicts | PASS — R3 W4 smoke |
| Confirmation omitted | Close without explicit physical confirmation fails | PASS — R3 W4 smoke |
| Event after close | Movement cannot attach after session closes | PASS — R3 W4 smoke |
| Count input | Bodyless close denied; negative/unconfirmed count denied; count not prefilled | PASS — shift API and W4 smoke |
| Shortage / overage | Nonzero variance is pending review | PASS — both signs executed in W4 smoke |
| Review | Audit-authorized acknowledgment appends reason and preserves count | PASS — W4 smoke/API permission guard |
| Duplicate/stale close | Replay returns original; a different second close fails | PASS — W4 smoke |
| Concurrent close | conditional session update plus unique reconciliation permits one winner | PASS — constraint/transaction proof; no multi-process physical run claimed |
| Shift ownership | reads/movements/close select signed-in user's open session | PASS — cross-cashier read/close denial in W4 smoke and API permission smoke |
| Missing links / legacy | close is `CoverageUnverified`; no synthetic attribution | PASS — W4 smoke |
| Restart/recovery | ledger and replay keys are persisted in SQLite | PASS — schema and reopen-capable persistence boundary; physical restart operator run not required for draft |
| Backup / restore / future schema | v26 data included; future v27 rejected | PASS — portable backup smoke plus v25→v26 migration fixture |
| Owner detail | authorized drawer read exposes exact movements and reviewer trail | PASS — endpoint/source and W4 smoke |
| Closed history authority | Unprivileged caller denied; `Audit.View` caller receives bounded read-only closed detail | PASS — loopback HTTP 403/200 regression; final financial approval role remains policy blocked |
| Remote close / LAN | remote cannot silently auto-balance; production LAN remains unchanged | PASS — W4 source guard; LAN enablement not performed |
| Policy activation / real cash handling | Kirch approves rules and operator observes an actual drawer count | POLICY BLOCKED — human approval/physical acceptance only |


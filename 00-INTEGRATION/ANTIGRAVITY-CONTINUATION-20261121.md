# Antigravity continuation — Salryn Cafe software completion

Stop requested by Engineer to conserve Codex tokens. All active engineering workers have stopped and preserved source. This is a WIP custody handoff, NOT software completion, review acceptance, candidate integration or production authorization. Antigravity already has its skills/plugins: use them; do not install/recreate the harness.

## Start here

Use primary worktree `C:/Users/arane/.gemini/antigravity/scratch/Dev-Salryn-Cafe-wt/codex-wave-b`, branch `codex/wave-b-integrator`. The original chat cwd `.../i0` is the preserved candidate worktree; DO NOT edit there as if it were the new implementation branch.

Read AGENTS.md, existing kirion-core then kirion-dispatch and only needed specialists; FORGE-DISPATCH-V2.md, CODEX-TAKEOVER-STATE.md, CODEX-FORGE-ADAPTER.md. Current authority is Issue29 comment7056186721:
https://github.com/Kirjane/Dev-Salryn-Cafe/issues/29#issuecomment-7056186721
Full goal file: C:/Users/arane/.codex/attachments/fbae92bc-c164-483b-9451-7040f62652a7/goal-objective.md. Read Issues1–15 and current29 once as required by the mandate. Older takeover-state prose is historical when contradicted by exact source/evidence below.

Mission: FORGE-SALRYN-CAFE-CODEX-SOFTWARE-COMPLETE. Continue the entire feasible engineering queue: atomic/recoverable money+HTTP outcomes+outbox; secure canonical Central domain application/bootstrap/lossless restore; operational native Android; full Windows/F&B C1–C11; real multi-client offline/reconnect/crash proofs; packaging/emulator/reproducibility; independent current-SHA review. Checkpoints are not endpoints. Missing license/hardware blocks its own gate, not independent software work.

Default one primary; at most2 temporary independent agents, disjoint worktrees/paths; no nested/perpetual fleet. Do not reactivate the superseded persistent Swarm. User authorized handoff to Antigravity, not destruction of preserved work or blanket self-approval. Existing native Antigravity tooling must be used according to actual host qualification; copied Codex hooks are not proof of enforcement.

## Exact source custody at stop

Repository Kirjane/Dev-Salryn-Cafe ID1465577766; last authenticated publisher Kirch-Nairu ID291564617. Reauthenticate before any publication; do not copy credentials.

| Lane | Worktree suffix | Branch | Frozen source |
|---|---|---|---|
| Primary financial WIP | codex-wave-b | codex/wave-b-integrator | cb03f7e12b28eb06881cab33b3aaf7f34d57d7cb |
| Shared explicit-file factory | same, ancestor | same | cc5333e6944cefc8f9d1abda0179a75f7d6d5d4f |
| Central WIP | codex-central-complete | codex/central-semantic-completion | 67ce68c928f86b30350544adef1b583d1e14b1f5 |
| Android WIP | codex-android-cashier | codex/android-operational-cashier | 00cbede13efc1a694bd4a080fe29e4fc509e91d8 |
| Last published combined source | codex-wave-b remote | origin/codex/wave-b-integrator / draft PR34 | 947aedf29a0ec564ec3ccf69583bfa659b4f58ab |
| Remote integration candidate | preserved | forge/candidate-salryn-cafe | 974de30042964cb84a9ad6075e8baa90d6f99946 |
| Preserved local candidate | i0 | forge/candidate-salryn-cafe | 6f1407e6 (unpublished heading fix) |

All new WIP is LOCAL ONLY, deliberately not pushed during safe stop to avoid starting new CI. Draft34 remains947aedf. Central contains shared cc5333; Android starts947aedf. No WIP lane has been merged into primary, candidate or main. A documentation-only handoff commit may follow cb03f7e1; verify git log/status. Preserve every existing worktree/ref/bundle. Never reset-hard, clean destructively, rebase/squash imported history, force-push, delete preserved refs, write upstream, promote main, deploy production or touch customer/retail data.

No worker-owned live process handles remain. Root known test sessions3181/69298/59824/89285 and workers60443/35894 are terminal. Compiler-server processes must not be mistaken for live engineering jobs or killed indiscriminately. Central's only untracked directory is TestResults; evidence already copied outside Git. Android and primary source were clean after source checkpoint.

## Primary financial WIP — inspect before integration

cb03f7e1 changes6 files: LocalCafeOrderEndpoints.cs; CafeMutationIdempotencyGuard.cs; SqliteSaleWriteRepository.cs; SqliteCafeOrderCompletionCoordinator.cs; new I3 CanonicalCompletionRecoveryTests.cs and CanonicalOrderCompletionFixture.cs. Fixture adapted from archived Yza C5OrderFinancialV2Proof; entirely synthetic.

Implemented, NOT independently reviewed:
- Existing C5 bug fixed: `grossSubtotalBeforeAllocatedDiscounts` was accepted but unused. C5 gross header1000, allocated discounts200, fee50, total850, Cash350+AR500 previously failed subtotal validation. Gross mode now sums item net+discount and checks allocated discount sum against header. Legacy net-subtotal mode unchanged. Canonical partial Cash+AR/discount/fee persisted-total regression now passes.
- Coordinator Complete gains optional explicit Action<SqliteConnection,SqliteTransaction,CafeOrderCompletionResult> outcome participant. Invoked before same domain/lifecycle/outbox commit, including canonical replay. Response-persistence fault rolls back transaction.
- Guard PersistAtomicOutcome binds request scope, operation, actual same database/transaction and immutable response. Completion endpoint persists normalized Idempotent=false HTTP outcome in that transaction.
- Only tagged exact canonical completion endpoint may recover a matching v43 UNKNOWN fence using proven domain dedupe. Other callbacks remain fail-closed. Legacy canonical commit without cache can reconstruct result through coordinator.
- Completion replay now explicitly binds original actor/device/store/order/expected version from immutable completion outbox. Discriminating V1 foreign-actor/order replay tests failed before fix.

Exact last targeted run: canonical-atomic-after.trx,8 cases,6 PASS/2 FAIL, exit1. Both failures are the test's expected outbox delta before+3 versus actual before+2. Inspect source conditional: ingredient outbox emitted only when consumption.Lines.Count>0; this recovered C5 fixture has no recipe lines. Do NOT remove the outbox assertion or silently accept a product bug. Verify the intended two exact Sale/Order envelope identities, absence of ingredient lines in this fixture, then correct the oracle or add a recipe-bearing fixture that legitimately expects3. Financial rollback/HTTP-loss paths passed prior assertions; full I3 NOT rerun after this latest WIP. This source is NOT a green/integration-approved checkpoint.

Prior focused evidence in recovery root: explicit-central-path.trx1/1PASS at cc5333 (fresh canonical explicit path; unknown transport DB rejected without mutation); c5-subtotal-before1FAIL; canonical-recovery-c5fixed3PASS/1FAIL expected409 vs200; canonical-recovery-after4/4PASS; canonical-v1-binding-before2FAIL. Last broader baseline at947aedf: I221,I334,I6A12,I6B25 PASS; not current WIP acceptance.

Immediate primary follow-ups: finish targeted atomic tests; full I3/I2; real multi-process/HTTP fault proof; independent frozen-SHA review. Audit recovery metadata contract carefully before expanding beyond order completion. Other cash/refund/AR endpoints still need atomic outcomes or exact-event recovery. Same-actor changed-payment completed RemotePos replay remains open. Endpoint completion DTO still lacks full Customer/AR/C5 options; fee IDs still use Guid.NewGuid fallback when missing and require stable deterministic intent-derived treatment. Legacy gaps without adequate canonical evidence must stay explicit UNKNOWN until reviewed reconciliation; never infer original actor or fabricate success.

## Central WIP — independent lane

67ce68c9 commits13 owned Server/I6A files; details in git show --stat. Opt-in `EnableCanonicalDomainProcessing`, `CanonicalTenantId`, `CanonicalStoreId`; default transport-only containment retained. Fresh canonical Cafe schema initialized at SAME physical Central file before transport tables through explicit factory; existing unclassified transport-only DB rejected, no attached second financial ledger.

Canonical order processor calls shared ApplyCanonical; SaleCommittedV1 uses existing SharedSqliteCompletedSaleWriteParticipant and archived prerequisite checks. Domain posting, inbox resolution and applied feed share one managed SQLite transaction. Feed insertion fault test proves sale/stock/inbox rollback then exactly-once retry.

Separate operator authentication implemented: POST /api/sync/v1/operators/login uses existing authenticated DEVICE bearer + product/tenant/store/device headers, JSON{username,password}; canonical credential verification; response{operatorToken,userId,expiresAtUtc,serverEpoch}. Token high-entropy opaque, only hash durable, bound tenant/store/device/user/epoch,30min expiry; five-attempt lockout; current role/permission fingerprint checked on each push. /push canonical mode also requires `X-Salryn-Operator-Token`; every envelope actor must match. POST /operators/logout revokes. Device identity alone never proves actor authority. Queue from an old actor requires that actor's reauthentication; never substitute current actor.

Worker's last exact-code I6A22/22PASS exit0 (12old +order1 +sale/fault2 +operatornegative6 +realHTTP1), source unchanged when committed. Logs copied to C:/Users/arane/SalrynForgeRecovery/20261121-takeover/central-semantic-stop/TestResults. Formal independent security/migration review NOT RUN.

Remaining: V2/V3/other families still NeedsReview; bootstrap/restore/epoch high-watermark not finished; fresh initialization interruption between migration and authority binding is an availability issue to repair. Reconcile parent C5 fix then use existing ExecuteC5Async for V2, not legacy ExecuteAsync. Do not publish fake Applied for unsupported processors or bypass operator proof. Existing source/contract helpers are canonical; no second ledger.

## Android WIP — independent lane

00cbede1 commits7 owned Android/Foundation/I6B files. New Foundation/Cashier portable controller uses canonical MobileProductionComposition (Foundation->Runtime approved, acyclic), actual operator authentication/current-permission revalidation, signed protected enrollment guard, menu/order/preparation, Cash/AR/split, shifts. MainActivity replaces setup preview with native screens. Protected pending intents retain original actor and operation ID.

Contract: IMobileEnrollmentReader.ReadAsync -> MobileEnrollmentBinding(DeviceId,TenantId,StoreId,ProductId,ServerEpoch,TrustedServerPublicKeySpkiBase64,MobileSignedOfflineLease). Protected key `salryn.cafe.approved_enrollment.v1`. Only actual approved enrollment/bootstrap writer may persist this; never pin a server key merely because an unauthenticated lease response contains it. Missing approved binding fails closed. UTC+ElapsedRealtime+BootCount lease guard remains mandatory.

Worker reports portable NativeCanonicalCashierTests10/10PASS exit0 on same code before checkpoint; Foundation Release build0errors (2inherited warnings). Earlier 5-case run3PASS/2FAIL was an erroneous AR oracle, then5/5PASS, expanded10/10PASS; all logs retained at C:/Users/arane/SalrynForgeRecovery/20261121-takeover/native-cashier-stop-20261121/TestResults. MainActivity native compilation/APK/emulator NOT RUN for00cbede1. The prior947 APK does NOT prove this new native code.

Remaining: real approved enrollment/pairing/bootstrap writer absent; root-reserved HttpSyncOutboxTransport operator-token provider not wired; protected pending rejected intent currently blocks further dispatch until reconciliation and needs reviewed recovery UX; shift idempotency participation not added; refund/receipt/kitchen/printing/full delivery UI incomplete; README still preview and needs update only after true verification. No prototype financial engine promoted. Canonical fixture available in new I6B CanonicalCashierFixture.cs; use actual services for tests.

## Path ownership / integration sequence

Primary owns Sync.Contracts, Persistence (including linked Infrastructure/Data), Mobile.Runtime, API.Local, migrations/catalog, global solution/build/CI and transport HttpSyncOutboxTransport. Central lane owns Server/I6A/deploy; Android owns Android/Foundation except reserved transport and only its new I6B tests. Maintain disjoint ownership if using agents. No new agents are running.

1. Verify status/refs; repair current primary proof oracle from actual contract, finish atomic/legacy recovery tests and independent review.
2. Independently review Central WIP security/transaction changes; history-preserving merge into isolated primary only after ownership/contract inspection, reconcile C5 helper before V2/V3. Continue other domain families/bootstrap/lossless restore.
3. Review Android WIP; integrate into isolated composition; wire approved enrollment/bootstrap and separate Central operator token. Batch operations under original authenticated actor; retain receipts until true business Applied.
4. Execute real combined host/canonical Android/Central offline, reconnect, duplicate, crash/restart, AR/refund/blind-close/printing semantics with synthetic DBs. Complete all feasible software queue, not just this slice. Run exact-source native compilation/APK/emulator. Obtain fresh independent Nox/Aegis reviews; do not self-approve or reuse old review for new SHA.
5. Finish C1–C11/frontend/Windows install/backup/restore/packaging and acceptance matrix; only serialize reviewed integration into candidate when policy permits. Never main/production without Technical Authority.

## License and last published CI evidence

Full Windows Release CoreSmoke at947 passes the explicit retail boundary, then fails licensed /api/checkout: expected409, actual403 License.FeatureDenied, no local license file. Never bypass Release licensing or change product identity on signed fixtures. Checked repo Actions secret NAMES only: `gh secret list --repo Kirjane/Dev-Salryn-Cafe` returned none, exit0. Existing tests/Fixtures/CanonicalBeta host/terminal license JSON metadata is ApplicationId Salryn.POS, NOT Salryn.Cafe. No secret values/private keys read or invented. Engineer previously asked for approved synthetic Cafe Release test-license fixture path/CI secret name; no reply in this chat yet. Continue independent software while that gate remains blocked.

Published947 evidence: Android44849554236 PASS25tests+native Release arm64 APK. PR generated merge74b3880b817b597a50a9557a713e2987a6354ae5 has identical tree9466219b6afab478f6d18bafe4347c4ca9c391d5. Signed APK17,161,930bytes SHA256 BD2A8113DA8B81E764251DB47E38CA71B435C9CC229C815A5C6DE812D4C6118E. Downloaded local artifact and checksum manifest under C:/Users/arane/SalrynForgeRecovery/20261121-wave-b/android-947aedf. Verification44849554261: PortablePASS; WindowsFAIL core-smoke; Windows builds and nonzero suites passed. Artifacts14684852011(Android),14684083757(Windows),14684189171(Portable) downloaded locally.

947 scoped review evidence exists; separate reviewer independently4/4 fence/denial tests at runtime-identical e898349. Formal Kirion-Nox requested on PR34, no submitted formal review when last checked. None of the new cb03/67ce/00cb WIP has independent acceptance. Raw audit/board/evidence stays outside Git; concise sanitized receipts only.

Evidence root C:/Users/arane/SalrynForgeRecovery/20261121-wave-b. Prior final receipt: https://github.com/Kirjane/Dev-Salryn-Cafe/issues/29#issuecomment-7055115692 . Latest new WIP supersedes its source-completion claims only within explicitly described scope; mission remains unfinished.

## Suggested Antigravity entry prompt

Continue FORGE-SALRYN-CAFE-CODEX-SOFTWARE-COMPLETE from this exact handoff. Use your existing skills/plugins. First read this file and current Technical Authority comment7056186721, verify all three WIP refs/dirty states, preserve history, then resume primary canonical financial outcome recovery and current failing proofs. Keep Central/Android work isolated until inspected/reviewed; do not recreate either implementation. Advance the full dependency graph continuously, respecting synthetic-only/no-main/no-production/no-self-approval boundaries. Missing human fixtures do not abandon independent software. Report honest exact-SHA evidence and checkpoint materially, without routine permission pauses or treating a draft PR as completion.

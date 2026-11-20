# S4 Central synchronization — Wave A source reconciliation (NOT ACCEPTED)

Source: `Kirjane/Dev-Salryn-Cafe@974de30042964cb84a9ad6075e8baa90d6f99946`; read-only historical reference `Kirjane/Salryn-CafeYza@4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3`.

Status: source inspected, bounded HTTPS proxy correction proposed; **not deployment-ready**. S4 cannot autonomously change shared contracts, canonical migration/financial stores, Android, or Windows host transport.

## Confirmed mismatch — central ingestion is not business application

Current `CentralSyncService.PushOperations` accepts any envelope passing `SyncProtocolV1.Validate`, stores a result `status=Applied`, `result_code=APPLIED`, and appends the original payload to `sync_change_feed` without dispatching a business-semantic processor. Existing I6A tests submit arbitrary JSON for `cafe.sale.committed.v3`, `cafe.order.created.v1`, and `cafe.cash.expense.v1`, showing transport/identity behavior only. **These tests do not establish canonical financial posting, role authorization, ledger reconciliation, or domain validation.**

Historical `Salryn.Sync.Central.Sqlite` has `CentralSaleCommittedV1/V2/V3Processor`, `CentralCafeOrderV1Processor`, `CentralCafeInventoryV1Processor`, `CentralCafeCashV1Processor`, `CentralCafeRefundV1Processor`, `CentralCafeDeliveryV1Processor`, `CentralCafeBookingV1Processor`, `CentralCafeAttendanceIncidentV1Processor`, `CentralIngredientConsumptionV1Processor`, `CentralSyncInbox`, `CentralChangeFeedStore`, and multiple bootstrap readers. The V3 sale processor validates canonical payload, checks deployment prerequisites, uses a SQL savepoint, posts domain effects, and distinguishes `Applied` / `Rejected` / `NeedsReview`. Its storage/schema assumptions differ from the currently isolated central server database.

**Integrator contract decision required:** designate one canonical central ingestion and domain projection authority. Define the explicit operation-type/schema-version dispatch map; reject unknown types or resolve to `NeedsReview` without asserting financial application; preserve final-resolution formats and stable operation identity. Do not retarget archived processors merely to compile, import a second parallel inbox, or write Cafe financial tables without reconciled canonical schema lineage.

## Bootstrap and isolation

Current `GetBootstrap` uses `SyncBootstrapSnapshotProtocolV1.Create` with empty roles, users, units, products, sessions, customers, and a sequence baseline. Historical `CentralBootstrapSnapshotReader` reads actual roles, users, units, products, open sessions, and customers from an applied domain database. A sequence-only bootstrap must not be advertised as recovery of business projections.

Proposed shared-boundary decision: tenant/product/store-qualified snapshot schema and baseline semantics, operator access to bootstrap projections, stage-and-verify before cursor advancement, and explicit non-empty fixture assertions. Any `src/Salryn.Sync.Contracts/**` changes belong to the Integrator.

## Restore, sequence and durable replay safety

`CentralSyncDatabase.RestoreAndBumpEpoch` currently overwrites the database from backup and increments the epoch **in the restored snapshot**, without checking the pre-restore live epoch, head sequence, or whether the backup predates acknowledged operations. Restoring older backups can decrease `head_sequence` and reuse a previous epoch. Also, copying a backup before validating it risks destructive replacement. This helper must not be used as an unattended deployment/recovery procedure.

Required design decision: quiesce writes; take a verified durable online backup; record pre-restore instance/epoch/high-watermark evidence outside the replaced database; stage and validate backup; choose a new non-reusable instance/epoch fence and monotonic replay policy; reconcile acknowledged operations from independent durable evidence or stop with manual financial recovery; atomically switch only after integrity checks. **Epoch rotation alone cannot recover lost acknowledged financial facts.**

## Security/proxy scope

The HTTPS gate previously accepted `X-Forwarded-Proto: https` when `RemoteIpAddress` was null. S4 Wave A now requires a known loopback or explicitly allowlisted peer IP before trusting this header. Existing direct HTTPS continues to be accepted; the explicit proxy allowlist is unchanged. A negative null-peer test is provided.

Still required: production-level reverse-proxy boundary test and configuration review, operator-level authorization for each allowed operation, offline lease/device fencing with reconnect handling, revoked quarantine replay/collision semantics, abuse controls and multi-store integration.

## Verification / delivery classification

- GitHub authenticated source and original Yza reference: inspected.
- Scoped proxy code change and negative test: source candidate only.
- `dotnet build src/Salryn.Sync.Central.Server/Salryn.Sync.Central.Server.csproj -c Release`: **NOT RUN**, no `dotnet` in session VM.
- `dotnet test tests/Salryn.Cafe.I6ATests/Salryn.Cafe.I6ATests.csproj -c Release --logger trx`: **NOT RUN**, same reason. Executed test count **0**. No PASS claim.
- Live loopback Kestrel, synthetic two-client service, restore/epoch, CI/TRX, independent Nox/Aegis review: **NOT RUN / PENDING**.
- No real customer data, credentials, production deployment, shared contract edits, Android sync activation or integration merge.

This is a reviewable S4 partial candidate and integration blocker record, **not a completed central synchronization service**.

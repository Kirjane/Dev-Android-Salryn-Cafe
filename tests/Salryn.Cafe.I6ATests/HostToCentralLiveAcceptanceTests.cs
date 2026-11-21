using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class HostToCentralLiveAcceptanceTests
{
    [Fact]
    public async Task LiveHttpsHostToCentral_SaleDuplicateUnsupportedBootstrapLogoutAndRestartPreserveExactOnceAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "live-host-central-" + Guid.NewGuid().ToString("N"));
        C4ProofDatabase target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));

        Guid deviceId;
        Guid tenantId;
        Guid storeId;
        string dbPath = target.Paths.DatabasePath;

        using (var host = new CentralSyncTestHost(dbPath, canonical: true, canonicalStore: C4ProofIds.StoreId))
        {
            tenantId = host.TenantId;
            storeId = host.StoreId;
            using HttpClient admin = host.CreateHttpsClient();
            string adminToken = await host.LoginAsAdminAsync(admin);
            using EnrolledDeviceClient device = await host.EnrollApproveAndAuthenticateDeviceAsync(
                admin, adminToken, "Live Host-to-Central Acceptance Terminal");
            deviceId = device.DeviceId;

            var context = new AuthenticatedDeviceContext(
                device.DeviceId, host.TenantId, host.StoreId, "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
            SyncOperationEnvelopeV1 saleEnvelope = CanonicalDomainTransactionTests.SaleEnvelope(context);

            // 1. Operator authentication over real HTTPS
            HttpResponseMessage loginResp = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/operators/login",
                new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            OperatorLoginResponse login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
            device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

            // 2. Push canonical sale -> Applied only after canonical commit
            HttpResponseMessage pushResp = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, [saleEnvelope]));
            Assert.Equal(HttpStatusCode.OK, pushResp.StatusCode);
            PushOperationsResponse pushBody = (await pushResp.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            PushOperationResultDto saleResult = Assert.Single(pushBody.Results);
            Assert.True(saleResult.Resolution.IsApplied, saleResult.Resolution.ResultCode);
            Assert.Equal(1L, saleResult.ChangeFeedSequence);
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));

            // 3. Duplicate submission -> idempotent replay, zero duplicate financial rows
            HttpResponseMessage dupResp = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, [saleEnvelope]));
            Assert.Equal(HttpStatusCode.OK, dupResp.StatusCode);
            PushOperationsResponse dupBody = (await dupResp.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.True(Assert.Single(dupBody.Results).IsDuplicateReplay);
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

            // 4. Unsupported operation family -> explicit NeedsReview, never Applied
            SyncOperationEnvelopeV1 unsupported = SyncProtocolV1.CreateEnvelope(
                deviceId: device.DeviceId,
                storeId: host.StoreId,
                actorUserId: C4ProofIds.ActorUserId,
                localSequence: 2,
                operationType: CafeDeliverySyncContract.DeliveryConfiguredV1,
                schemaVersion: 1,
                aggregateId: Guid.NewGuid(),
                baseVersion: null,
                occurredAtLocal: DateTimeOffset.UtcNow,
                payloadJson: "{\"delivery\":\"synthetic-live\"}");
            HttpResponseMessage unsupResp = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, [unsupported]));
            Assert.Equal(HttpStatusCode.OK, unsupResp.StatusCode);
            PushOperationsResponse unsupBody = (await unsupResp.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            PushOperationResultDto unsupResult = Assert.Single(unsupBody.Results);
            Assert.False(unsupResult.Resolution.IsApplied);
            Assert.Equal(SaleCommittedV1Contract.NeedsReview, unsupResult.Resolution.Status);
            Assert.Equal("DOMAIN_PROCESSOR_UNAVAILABLE", unsupResult.Resolution.ResultCode);
            Assert.Null(unsupResult.ChangeFeedSequence);
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

            // 5. Canonical bootstrap + change-feed pull over real HTTPS
            HttpResponseMessage bootResp = await device.Http.GetAsync($"/api/sync/v1/bootstrap?storeId={host.StoreId:D}");
            Assert.Equal(HttpStatusCode.OK, bootResp.StatusCode);
            using var bootDoc = System.Text.Json.JsonDocument.Parse(await bootResp.Content.ReadAsStringAsync());
            Assert.Equal(1L, bootDoc.RootElement.GetProperty("headSequence").GetInt64());
            Assert.True(bootDoc.RootElement.TryGetProperty("snapshot", out var snapshotEl) && snapshotEl.ValueKind == System.Text.Json.JsonValueKind.Object);

            HttpResponseMessage pullResp = await device.Http.GetAsync($"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&knownEpoch=1&limit=50");
            Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);

            // 6. Operator logout revokes token immediately -> subsequent push fails closed
            HttpResponseMessage logoutResp = await device.Http.PostAsync("/api/sync/v1/operators/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResp.StatusCode);
            HttpResponseMessage revokedResp = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, [saleEnvelope]));
            Assert.Equal(HttpStatusCode.Forbidden, revokedResp.StatusCode);
        }

        // 7. Full Central restart against persisted SQLite DB preserves exact-once financial state
        using (var restarted = new CentralSyncTestHost(dbPath, canonical: true, canonicalStore: storeId))
        {
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
            Assert.NotEqual(Guid.Empty, deviceId);
            Assert.NotEqual(Guid.Empty, tenantId);
        }
    }

    [Fact]
    public async Task LiveHttpsHostToCentral_ConflictingPayloadReuseIsRejectedAndStaleEpochIsFenced()
    {
        string root = Path.Combine(Path.GetTempPath(), "live-host-central-conflict-" + Guid.NewGuid().ToString("N"));
        C4ProofDatabase target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));

        using var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient device = await host.EnrollApproveAndAuthenticateDeviceAsync(
            admin, adminToken, "Live Conflict & Epoch Terminal");

        var context = new AuthenticatedDeviceContext(
            device.DeviceId, host.TenantId, host.StoreId, "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
        SyncOperationEnvelopeV1 original = CanonicalDomainTransactionTests.SaleEnvelope(context);

        HttpResponseMessage loginResp = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        OperatorLoginResponse login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

        // 1. Original canonical sale succeeds
        HttpResponseMessage first = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [original]));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));

        // 2. Reusing the exact same OperationId with an altered payload MUST be rejected
        SyncOperationEnvelopeV1 tampered = original with { PayloadJson = original.PayloadJson.Replace("500", "999", StringComparison.Ordinal) };
        HttpResponseMessage conflict = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [tampered]));
        if (conflict.StatusCode == HttpStatusCode.OK)
        {
            PushOperationsResponse conflictBody = (await conflict.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.False(Assert.Single(conflictBody.Results).Resolution.IsApplied);
        }
        else
        {
            Assert.True(
                conflict.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest,
                $"Expected 409/400 tampered payload rejection, got {(int)conflict.StatusCode}");
        }
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(500L, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));

        // 3. Stale epoch push is fenced after server epoch advances
        target.Execute("UPDATE sync_server_state SET server_epoch = server_epoch + 1;");
        SyncOperationEnvelopeV1 nextOp = SyncProtocolV1.CreateEnvelope(
            deviceId: device.DeviceId,
            storeId: host.StoreId,
            actorUserId: C4ProofIds.ActorUserId,
            localSequence: 2,
            operationType: CafeDeliverySyncContract.DeliveryConfiguredV1,
            schemaVersion: 1,
            aggregateId: Guid.NewGuid(),
            baseVersion: null,
            occurredAtLocal: DateTimeOffset.UtcNow,
            payloadJson: "{\"delivery\":\"stale-epoch\"}");
        HttpResponseMessage fenced = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [nextOp]));
        Assert.True(
            fenced.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden,
            $"Expected 409/403 epoch fence, got {(int)fenced.StatusCode}");
    }

    [Fact]
    public async Task NoxP1_NeedsReviewMissingPrerequisite_ReprocessesAndAppliesWhenPrerequisiteArrives()
    {
        string root = Path.Combine(Path.GetTempPath(), "nox-p1-needsreview-" + Guid.NewGuid().ToString("N"));
        C4ProofDatabase target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));

        // Remove the prerequisite cashier session so the first push MUST return NeedsReview
        target.Execute("DELETE FROM cashier_sessions WHERE id=$id;", ("$id", C4ProofIds.CashierSessionId.ToString("D")));

        using var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient device = await host.EnrollApproveAndAuthenticateDeviceAsync(
            admin, adminToken, "Nox P1 NeedsReview Retry Terminal");

        var context = new AuthenticatedDeviceContext(
            device.DeviceId, host.TenantId, host.StoreId, "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
        SyncOperationEnvelopeV1 saleEnvelope = CanonicalDomainTransactionTests.SaleEnvelope(context);

        HttpResponseMessage loginResp = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        OperatorLoginResponse login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

        // 1. First push -> NeedsReview (missing cashier session prerequisite)
        HttpResponseMessage first = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        PushOperationsResponse firstBody = (await first.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        PushOperationResultDto firstResult = Assert.Single(firstBody.Results);
        Assert.False(firstResult.Resolution.IsApplied);
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, firstResult.Resolution.Status);
        Assert.Equal(0L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(0L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

        // 2. Satisfy the missing prerequisite
        target.Execute(
            """
            INSERT INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, status)
            VALUES ($id, $userId, '2026-09-22T03:00:00Z', 10000, 1);
            """,
            ("$id", C4ProofIds.CashierSessionId.ToString("D")),
            ("$userId", C4ProofIds.ActorUserId.ToString("D")));

        // 3. Retry the exact same immutable operation -> MUST reprocess and transition to Applied!
        HttpResponseMessage retry = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        PushOperationsResponse retryBody = (await retry.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        PushOperationResultDto retryResult = Assert.Single(retryBody.Results);
        Assert.True(retryResult.Resolution.IsApplied, retryResult.Resolution.ResultCode);
        Assert.Equal(1L, retryResult.ChangeFeedSequence);
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

        // 4. Subsequent replay returns cached Applied without duplicating
        HttpResponseMessage third = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        PushOperationsResponse thirdBody = (await third.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.True(Assert.Single(thirdBody.Results).IsDuplicateReplay);
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

        // 5. Nox 6003491961 Finding A: immutable transition audit retained the prior failure.
        AssertSingleNeedsReviewTransition(target, saleEnvelope, firstResult.Resolution, device.DeviceId, expectedFeedSeq: 1L);
        host.Dispose();
        AssertTransitionAuditImmutableAfterDisposal(target, saleEnvelope);
    }

    [Fact]
    public async Task NoxFindingB_ConcurrentReplayAfterPrerequisiteRestored_AppliesExactlyOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "nox-b-concurrent-" + Guid.NewGuid().ToString("N"));
        C4ProofDatabase target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));
        target.Execute("DELETE FROM cashier_sessions WHERE id=$id;", ("$id", C4ProofIds.CashierSessionId.ToString("D")));

        var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient device = await host.EnrollApproveAndAuthenticateDeviceAsync(
            admin, adminToken, "Nox B Concurrent Replay Terminal");
        var context = new AuthenticatedDeviceContext(
            device.DeviceId, host.TenantId, host.StoreId, "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
        SyncOperationEnvelopeV1 saleEnvelope = CanonicalDomainTransactionTests.SaleEnvelope(context);

        HttpResponseMessage loginResp = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        OperatorLoginResponse login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

        HttpResponseMessage first = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        PushOperationResultDto firstResult = Assert.Single((await first.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, firstResult.Resolution.Status);

        target.Execute(
            """
            INSERT INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, status)
            VALUES ($id, $userId, '2026-09-22T03:00:00Z', 10000, 1);
            """,
            ("$id", C4ProofIds.CashierSessionId.ToString("D")),
            ("$userId", C4ProofIds.ActorUserId.ToString("D")));

        // Overlapping authenticated retries of the same immutable operation, released together.
        const int Parallel = 8;
        using var gate = new SemaphoreSlim(0, Parallel);
        Task<HttpResponseMessage>[] inflight = Enumerable.Range(0, Parallel).Select(async _ =>
        {
            await gate.WaitAsync();
            return await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [saleEnvelope]));
        }).ToArray();
        gate.Release(Parallel);
        HttpResponseMessage[] responses = await Task.WhenAll(inflight);

        List<PushOperationResultDto> results = [];
        foreach (HttpResponseMessage r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            results.Add(Assert.Single((await r.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results));
        }

        // Exactly one request performed the transition; all others observe the cached Applied result.
        Assert.Single(results, r => !r.IsDuplicateReplay);
        Assert.All(results, r =>
        {
            Assert.True(r.Resolution.IsApplied, r.Resolution.ResultCode);
            Assert.Equal(firstResult.Resolution.ReceivedSequence, r.Resolution.ReceivedSequence);
            Assert.Equal(1L, r.ChangeFeedSequence);
        });
        Assert.Single(results.Select(r => r.Resolution.ResultCode).Distinct());

        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_operations;"));
        AssertSingleNeedsReviewTransition(target, saleEnvelope, firstResult.Resolution, device.DeviceId, expectedFeedSeq: 1L);
        long stockAfterBurst = target.ScalarLong("SELECT COUNT(*) FROM stock_movements;");

        // Subsequent sequential replay: consistent cached response, no further financial/feed/audit effect.
        HttpResponseMessage after = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [saleEnvelope]));
        PushOperationResultDto afterResult = Assert.Single((await after.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);
        Assert.True(afterResult.IsDuplicateReplay);
        Assert.True(afterResult.Resolution.IsApplied);
        Assert.Equal(firstResult.Resolution.ReceivedSequence, afterResult.Resolution.ReceivedSequence);
        Assert.Equal(1L, afterResult.ChangeFeedSequence);
        Assert.Equal(stockAfterBurst, target.ScalarLong("SELECT COUNT(*) FROM stock_movements;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));

        // Host disposal (process-level restart of the Central app): durable state retained.
        host.Dispose();
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        AssertTransitionAuditImmutableAfterDisposal(target, saleEnvelope);
    }

    private static void AssertSingleNeedsReviewTransition(
        C4ProofDatabase target, SyncOperationEnvelopeV1 env, SyncOperationFinalResolutionV1 original, Guid deviceId, long expectedFeedSeq)
    {
        string op = env.OperationId.ToString("D");
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_status_transitions;"));
        Assert.Equal(1L, target.ScalarLong(
            """
            SELECT COUNT(*) FROM sync_inbox_status_transitions
            WHERE operation_id=$op AND operation_hash=$hash AND received_sequence=$seq
              AND previous_status='NeedsReview' AND previous_result_code=$prevCode
              AND new_status='Applied' AND resolving_device_id=$dev AND resolving_actor_user_id=$actor
              AND resolution_source='AuthenticatedDeviceReplay' AND change_feed_sequence=$feed
              AND length(transition_id)=36 AND length(transitioned_at_utc) > 0;
            """,
            ("$op", op), ("$hash", env.OperationHash), ("$seq", original.ReceivedSequence),
            ("$prevCode", original.ResultCode), ("$dev", deviceId.ToString("D")),
            ("$actor", env.ActorUserId.ToString("D")), ("$feed", expectedFeedSeq)));
        // Inbox receipt retains its original received sequence.
        Assert.Equal(original.ReceivedSequence, target.ScalarLong(
            "SELECT received_sequence FROM sync_inbox_operations WHERE operation_id=$op;", ("$op", op)));
    }

    private static void AssertTransitionAuditImmutableAfterDisposal(C4ProofDatabase target, SyncOperationEnvelopeV1 env)
    {
        Assert.Equal(1L, target.ScalarLong(
            "SELECT COUNT(*) FROM sync_inbox_status_transitions WHERE operation_id=$op AND previous_status='NeedsReview';",
            ("$op", env.OperationId.ToString("D"))));
        Assert.ThrowsAny<Exception>(() => target.Execute("UPDATE sync_inbox_status_transitions SET new_status='Rejected';"));
        Assert.ThrowsAny<Exception>(() => target.Execute("DELETE FROM sync_inbox_status_transitions;"));
        Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_status_transitions WHERE new_status='Applied';"));
    }

    [Fact]
    public Task NoxP1_CreditSaleV2_RejectedWhenCreditSalePermissionRevokedEvenWithSalesCreate()
        => VerifyCreditSalePermissionAsync(version: 2, authorized: false);

    [Fact]
    public Task NoxGate3_CreditSaleV1_RejectedWhenCreditSalePermissionRevokedEvenWithSalesCreate()
        => VerifyCreditSalePermissionAsync(version: 1, authorized: false);

    [Fact]
    public Task NoxGate3_CreditSaleV1_AuthorizedPositive_AppliesWithCreditSalePermission()
        => VerifyCreditSalePermissionAsync(version: 1, authorized: true);

    [Fact]
    public Task NoxGate3_CreditSaleV2_AuthorizedPositive_AppliesWithCreditSalePermission()
        => VerifyCreditSalePermissionAsync(version: 2, authorized: true);

    private static async Task VerifyCreditSalePermissionAsync(int version, bool authorized)
    {
        string root = Path.Combine(Path.GetTempPath(), "nox-p1-creditsale-" + Guid.NewGuid().ToString("N"));
        C4ProofDatabase target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));
        using var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient device = await host.EnrollApproveAndAuthenticateDeviceAsync(
            admin, adminToken, "Nox P1 Credit Sale Guard Terminal");

        // Revoke credit authority after initialization while retaining Sales.Create.
        if (!authorized)
            target.Execute("DELETE FROM role_permissions WHERE permission='Receivables.CreateCreditSale';");

        HttpResponseMessage loginResp = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        OperatorLoginResponse login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

        var context = new AuthenticatedDeviceContext(
            device.DeviceId, host.TenantId, host.StoreId, "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
        SyncOperationEnvelopeV1 baseSale = CanonicalDomainTransactionTests.SaleEnvelope(context);
        SaleCommittedV1 parsedBase = SaleCommittedV1CanonicalJson.Parse(baseSale.PayloadJson).Payload!;
        Guid customerId = Guid.NewGuid();
        target.Execute(
            """
            INSERT INTO customers(id, full_name, is_active, created_by_user_id, created_at, updated_at)
            VALUES($id, 'Synthetic Gate 3 Customer', 1, $actor, '2026-11-21T00:00:00Z', '2026-11-21T00:00:00Z');
            """,
            ("$id", customerId.ToString("D")), ("$actor", C4ProofIds.ActorUserId.ToString("D")));
        SaleCommittedV1 arSaleEvidence = parsedBase with
        {
            Sale = parsedBase.Sale with { CustomerId = customerId, PaymentStatus = SaleCommittedV1Contract.PaymentStatusCredit, AmountPaidCents = parsedBase.Sale.TotalCents, BalanceDueCents = parsedBase.Sale.TotalCents },
            Payments = parsedBase.Payments
                .Select(p => p with { Method = nameof(Salryn.Core.Models.PaymentMethod.AccountsReceivable) })
                .ToArray()
        };
        Guid financialOpId = Guid.NewGuid();
        Guid financialDecisionId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lineDiscounts = arSaleEvidence.Items
            .Select(i => new SaleCommittedV2LineDiscount(i.Id, 0, 0, 0))
            .ToArray();
        var decision = new Salryn.Core.Models.CafeSaleFinancialDecision(
            financialDecisionId,
            financialOpId,
            host.StoreId,
            null,
            null,
            null,
            arSaleEvidence.Sale.Id,
            C4ProofIds.CashierSessionId,
            C4ProofIds.ActorUserId,
            arSaleEvidence.Sale.SubtotalCents,
            0,
            0,
            0,
            0,
            arSaleEvidence.Sale.TotalCents,
            0,
            arSaleEvidence.Sale.TotalCents,
            arSaleEvidence.Sale.TotalCents,
            0,
            0,
            string.Empty,
            string.Empty,
            now,
            arSaleEvidence.Items.Select(i => new Salryn.Core.Models.CafeSaleFinancialLineDecision(i.Id, 0, 0)).ToArray(),
            []);
        string decisionHash = Salryn.Core.Services.CafeSaleFinancialCalculator.ComputeContentHash(decision);
        SaleCommittedV2 v2 = new(
            SaleEvidence: arSaleEvidence,
            FinancialOperationId: financialOpId,
            FinancialDecisionId: financialDecisionId,
            OrderId: null,
            OrderRevisionId: null,
            ExpectedOrderVersion: null,
            FinancialDecisionCreatedAtUtc: now,
            FinancialDecisionHash: decisionHash,
            DiscountReason: string.Empty,
            FeeTotalCents: 0,
            LineDiscounts: lineDiscounts,
            Fees: []);
        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            deviceId: device.DeviceId,
            storeId: host.StoreId,
            actorUserId: C4ProofIds.ActorUserId,
            localSequence: 1,
            operationType: version == 1 ? SaleCommittedV1Contract.OperationType : SaleCommittedV2Contract.OperationType,
            schemaVersion: version == 1 ? SaleCommittedV1Contract.SchemaVersion : SaleCommittedV2Contract.SchemaVersion,
            aggregateId: arSaleEvidence.Sale.Id,
            baseVersion: null,
            occurredAtLocal: now,
            payloadJson: version == 1 ? SaleCommittedV1CanonicalJson.Serialize(arSaleEvidence) : SaleCommittedV2CanonicalJson.Serialize(v2));

        HttpResponseMessage push = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [envelope]));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        PushOperationsResponse body = (await push.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        PushOperationResultDto result = Assert.Single(body.Results);
        Assert.Equal(authorized, result.Resolution.IsApplied);
        Assert.Equal(authorized && version == 2 ? 1L : 0L,
            target.ScalarLong("SELECT COUNT(*) FROM cafe_sale_financial_decisions;"));
        if (!authorized)
        {
            Assert.Equal(SaleCommittedV1Contract.Rejected, result.Resolution.Status);
            Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY", result.Resolution.ResultCode);
            Assert.Null(result.ChangeFeedSequence);
        }
        else
        {
            Assert.Equal(1L, result.ChangeFeedSequence);
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(debit_cents) FROM accounts_receivable_entries;"));
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(balance_after_cents) FROM accounts_receivable_entries;"));
            Assert.Equal(9000L, target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
        }
        foreach (string table in new[] { "sales", "payments", "accounts_receivable_entries", "stock_movements", "sync_change_feed" })
            Assert.Equal(authorized ? 1L : 0L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));

        // Replaying an accepted credit operation must preserve the ledger and stock exactly once.
        if (authorized)
        {
            using HttpResponseMessage replay = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push", new PushOperationsRequest(1, [envelope]));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True(Assert.Single((await replay.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results).IsDuplicateReplay);
            foreach (string table in new[] { "sales", "payments", "accounts_receivable_entries", "stock_movements", "sync_change_feed" })
                Assert.Equal(1L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
        }
    }
}

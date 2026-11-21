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
}

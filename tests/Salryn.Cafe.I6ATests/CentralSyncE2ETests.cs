using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CentralSyncE2ETests
{
    private static SyncOperationEnvelopeV1 MakeEnvelope(
        Guid deviceId,
        Guid storeId,
        long localSeq,
        string opType,
        string payloadJson,
        Guid? operationId = null)
    {
        return SyncProtocolV1.CreateEnvelope(
            deviceId: deviceId,
            storeId: storeId,
            actorUserId: Guid.CreateVersion7(),
            localSequence: localSeq,
            operationType: opType,
            schemaVersion: opType == SaleCommittedV3Contract.OperationType ? SaleCommittedV3Contract.SchemaVersion : 1,
            aggregateId: Guid.CreateVersion7(),
            baseVersion: null,
            occurredAtLocal: DateTimeOffset.UtcNow,
            payloadJson: payloadJson,
            operationId: operationId);
    }

    [Fact]
    public async Task HttpsEnforcement_RejectsUnverifiedForwarder_AllowsDirectHttps()
    {
        using CentralSyncTestHost host = new();
        using HttpClient plain = host.CreatePlainHttpClient();

        HttpResponseMessage plainResp = await plain.GetAsync("/api/sync/v1/health");
        Assert.Equal(HttpStatusCode.UpgradeRequired, plainResp.StatusCode);
        ErrorResponseDto? err = await plainResp.Content.ReadFromJsonAsync<ErrorResponseDto>();
        Assert.Equal("ERR_HTTPS_REQUIRED", err?.ErrorCode);

        // TestServer has no verified proxy peer; the forged header must not bypass TLS.
        using HttpRequestMessage proxiedReq = new(HttpMethod.Get, "/api/sync/v1/health");
        proxiedReq.Headers.Add("X-Forwarded-Proto", "https");
        HttpResponseMessage proxiedResp = await plain.SendAsync(proxiedReq);
        Assert.Equal(HttpStatusCode.UpgradeRequired, proxiedResp.StatusCode);
        Assert.Equal("ERR_HTTPS_REQUIRED",
            (await proxiedResp.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // The direct HTTPS branch remains functional.
        using HttpClient direct = host.CreateHttpsClient();
        HttpResponseMessage directResp = await direct.GetAsync("/api/sync/v1/health");
        Assert.Equal(HttpStatusCode.OK, directResp.StatusCode);
        HealthResponseDto? health = await directResp.Content.ReadFromJsonAsync<HealthResponseDto>();
        Assert.Equal(CentralServerOptions.CanonicalProductId, health?.ProductId);
        Assert.Equal(1, health?.ServerEpoch);
    }

    [Fact]
    public async Task TwoClientSync_PreservesFinancialEvidenceWithoutFalseApplication()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);

        using EnrolledDeviceClient tabletA = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Tablet-A-Cashier");
        using EnrolledDeviceClient tabletB = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Tablet-B-Kitchen");

        // Verify signed offline lease signature using server public key
        SignedOfflineLeaseV1 lease = tabletA.TokenResponse.OfflineLease;
        string leasePayload = CentralCrypto.BuildLeaseSigningPayload(
            lease.DeviceId, lease.TenantId, lease.StoreId, lease.ProductId,
            lease.ServerEpoch, lease.IssuedAtUtc, lease.ExpiresAtUtc);
        Assert.True(CentralCrypto.VerifyEcdsaP256Signature(lease.ServerPublicKeySpkiBase64, leasePayload, lease.SignatureBase64));

        // Tablet A pushes a cash sale envelope
        SyncOperationEnvelopeV1 saleOp = MakeEnvelope(
            tabletA.DeviceId,
            host.StoreId,
            localSeq: 1,
            opType: SaleCommittedV3Contract.OperationType,
            payloadJson: "{\"receipt\":\"CY-0001\",\"tender\":\"Cash\",\"totalCents\":24500}");

        HttpResponseMessage pushResp = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(KnownEpoch: 1, Operations: [saleOp]));
        Assert.Equal(HttpStatusCode.OK, pushResp.StatusCode);
        PushOperationsResponse? pushBody = await pushResp.Content.ReadFromJsonAsync<PushOperationsResponse>();
        Assert.NotNull(pushBody);
        Assert.Single(pushBody!.Results);
        Assert.False(pushBody.Results[0].IsDuplicateReplay);
        Assert.False(pushBody.Results[0].Resolution.IsApplied);
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, pushBody.Results[0].Resolution.Status);
        Assert.Equal("DOMAIN_PROCESSOR_UNAVAILABLE", pushBody.Results[0].Resolution.ResultCode);
        Assert.Null(pushBody.Results[0].ChangeFeedSequence);
        Assert.Equal(0, pushBody.HeadSequence);

        // Tablet B cannot observe unposted money as an applied change.
        HttpResponseMessage pullResp = await tabletB.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
        Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
        PullChangesResponseDto? pullBody = await pullResp.Content.ReadFromJsonAsync<PullChangesResponseDto>();
        Assert.NotNull(pullBody);
        Assert.Empty(pullBody!.Page.Changes);
        Assert.Equal(0, pullBody.Page.HighWatermarkServerSequence);
        Assert.False(pullBody.Page.HasMore);

        // Empty domain projections cannot be mistaken for a recoverable snapshot.
        HttpResponseMessage bootResp = await tabletB.Http.GetAsync($"/api/sync/v1/bootstrap?storeId={host.StoreId:D}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, bootResp.StatusCode);
        Assert.Equal("ERR_BOOTSTRAP_DOMAIN_SNAPSHOT_UNAVAILABLE",
            (await bootResp.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);
    }

    [Fact]
    public async Task PushIdempotency_DuplicateLostResponseReplaysCleanly_HashDivergenceReturns409()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tabletA = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Tablet-Idempotent");

        Guid stableOpId = Guid.CreateVersion7();
        SyncOperationEnvelopeV1 original = MakeEnvelope(
            tabletA.DeviceId, host.StoreId, 1, CafeOrderOperationContract.OrderRevisionCommittedV1,
            "{\"orderNo\":\"ORD-101\",\"items\":2}", stableOpId);

        // First push
        HttpResponseMessage r1 = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [original]));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        PushOperationsResponse b1 = (await r1.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.False(b1.Results[0].IsDuplicateReplay);
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, b1.Results[0].Resolution.Status);
        Assert.Null(b1.Results[0].ChangeFeedSequence);

        // Duplicate retry (simulating lost HTTP response)
        HttpResponseMessage r2 = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [original]));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        PushOperationsResponse b2 = (await r2.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.True(b2.Results[0].IsDuplicateReplay);
        Assert.Equal(b1.Results[0].Resolution.ReceivedSequence, b2.Results[0].Resolution.ReceivedSequence);
        Assert.Equal(0, b2.HeadSequence); // No domain processor; never publish a change.

        // Divergent payload with same OperationId -> 409 OPERATION_HASH_CONFLICT
        SyncOperationEnvelopeV1 tampered = MakeEnvelope(
            tabletA.DeviceId, host.StoreId, 1, CafeOrderOperationContract.OrderRevisionCommittedV1,
            "{\"orderNo\":\"ORD-101\",\"items\":99}", stableOpId);
        HttpResponseMessage r3 = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [tampered]));
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        ErrorResponseDto? err = await r3.Content.ReadFromJsonAsync<ErrorResponseDto>();
        Assert.Equal("OPERATION_HASH_CONFLICT", err?.ErrorCode);
    }

    [Fact]
    public async Task PartitionAndReconnect_RetainsBothBatchesWithoutPublishingFinancialFacts()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tabletA = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Partition-A");
        using EnrolledDeviceClient tabletB = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Partition-B");

        // Both devices accumulate offline outbox batches while partitioned
        SyncOperationEnvelopeV1[] batchA =
        [
            MakeEnvelope(tabletA.DeviceId, host.StoreId, 1, SaleCommittedV3Contract.OperationType, "{\"n\":\"A1\"}"),
            MakeEnvelope(tabletA.DeviceId, host.StoreId, 2, SaleCommittedV3Contract.OperationType, "{\"n\":\"A2\"}")
        ];
        SyncOperationEnvelopeV1[] batchB =
        [
            MakeEnvelope(tabletB.DeviceId, host.StoreId, 1, CafeCashMovementSyncContract.OperationType, "{\"n\":\"B1\"}"),
            MakeEnvelope(tabletB.DeviceId, host.StoreId, 2, CafeCashMovementSyncContract.OperationType, "{\"n\":\"B2\"}")
        ];

        // Reconnect: both independent batches are durably received but NOT applied.
        HttpResponseMessage aResp = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, batchA));
        HttpResponseMessage bResp = await tabletB.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, batchB));
        Assert.Equal(HttpStatusCode.OK, aResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bResp.StatusCode);
        PushOperationsResponse acceptedA = (await aResp.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        PushOperationsResponse acceptedB = (await bResp.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.Equal(2, acceptedA.Results.Count);
        Assert.Equal(2, acceptedB.Results.Count);
        Assert.All(acceptedA.Results.Concat(acceptedB.Results), r =>
        {
            Assert.Equal(SaleCommittedV1Contract.NeedsReview, r.Resolution.Status);
            Assert.Null(r.ChangeFeedSequence);
        });

        // Durable inbox holds all four identities, while the applied feed remains empty.
        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        using (Microsoft.Data.Sqlite.SqliteConnection conn = db.OpenConnection())
        using (Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox_operations;";
            Assert.Equal(4L, Convert.ToInt64(cmd.ExecuteScalar()));
            cmd.CommandText = "SELECT COUNT(*) FROM sync_change_feed;";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        HttpResponseMessage pullResp = await tabletA.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=2&knownEpoch=1");
        Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
        PullChangesResponseDto pull = (await pullResp.Content.ReadFromJsonAsync<PullChangesResponseDto>())!;
        Assert.Empty(pull.Page.Changes);
        Assert.Equal(0, pull.Page.HighWatermarkServerSequence);
        Assert.False(pull.Page.HasMore);
    }

    [Fact]
    public async Task RevokedDevice_PullAndTokenFail403_PushQuarantinedAsDeviceRevokedFenced_LiveFeedUntouched()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tabletA = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Stolen-Tablet");
        using EnrolledDeviceClient tabletB = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Healthy-Tablet");

        // Admin revokes Tablet A
        using HttpRequestMessage revokeReq = new(HttpMethod.Post, $"/api/sync/v1/admin/devices/{tabletA.DeviceId:D}/revoke");
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        revokeReq.Content = JsonContent.Create(new RevokeDeviceRequest("Device lost on floor"));
        HttpResponseMessage revokeResp = await adminClient.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // 1. Revoked device cannot pull (403 DEVICE_REVOKED)
        HttpResponseMessage pullResp = await tabletA.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
        Assert.Equal(HttpStatusCode.Forbidden, pullResp.StatusCode);
        ErrorResponseDto? pullErr = await pullResp.Content.ReadFromJsonAsync<ErrorResponseDto>();
        Assert.Equal("DEVICE_REVOKED", pullErr?.ErrorCode);

        // 2. Revoked device cannot get new challenge/token (403 DEVICE_REVOKED)
        HttpResponseMessage chResp = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/challenge",
            new DeviceChallengeRequest(tabletA.DeviceId, host.TenantId, host.StoreId, CentralServerOptions.CanonicalProductId));
        Assert.Equal(HttpStatusCode.Forbidden, chResp.StatusCode);
        Assert.Equal("DEVICE_REVOKED", (await chResp.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // 3. Revoked device pushes offline sales -> 403 DEVICE_REVOKED_FENCED + quarantined!
        SyncOperationEnvelopeV1 offlineSale = MakeEnvelope(
            tabletA.DeviceId, host.StoreId, 1, SaleCommittedV3Contract.OperationType, "{\"receipt\":\"OFF-99\",\"totalCents\":15000}");
        HttpResponseMessage pushResp = await tabletA.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [offlineSale]));
        Assert.Equal(HttpStatusCode.Forbidden, pushResp.StatusCode);
        RevokedFencedResponseDto? fenced = await pushResp.Content.ReadFromJsonAsync<RevokedFencedResponseDto>();
        Assert.Equal("DEVICE_REVOKED_FENCED", fenced?.ErrorCode);
        Assert.Contains(offlineSale.OperationId, fenced!.QuarantinedOperationIds);

        // 4. Verify live change feed for Tablet B is 100% UNTOUCHED (0 changes)
        HttpResponseMessage healthyPull = await tabletB.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
        PullChangesResponseDto? healthyFeed = await healthyPull.Content.ReadFromJsonAsync<PullChangesResponseDto>();
        Assert.Empty(healthyFeed!.Page.Changes);

        // 5. Admin inspects quarantine and marks ApprovedForManualAudit
        using HttpRequestMessage listQReq = new(HttpMethod.Get, "/api/sync/v1/admin/quarantine");
        listQReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        HttpResponseMessage listQResp = await adminClient.SendAsync(listQReq);
        Assert.Equal(HttpStatusCode.OK, listQResp.StatusCode);
        List<QuarantineItemDto>? qItems = await listQResp.Content.ReadFromJsonAsync<List<QuarantineItemDto>>();
        Assert.Single(qItems!);
        Assert.Equal("AdminReviewRequired", qItems![0].ReviewStatus);
        Assert.Equal(offlineSale.OperationId, qItems[0].OperationId);

        using HttpRequestMessage reviewReq = new(HttpMethod.Post, $"/api/sync/v1/admin/quarantine/{qItems[0].QuarantineId:D}/review");
        reviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        reviewReq.Content = JsonContent.Create(new ReviewQuarantineRequest("ApprovedForManualAudit", "Verified cash drawer slip"));
        HttpResponseMessage reviewResp = await adminClient.SendAsync(reviewReq);
        Assert.Equal(HttpStatusCode.OK, reviewResp.StatusCode);
        QuarantineItemDto? reviewed = await reviewResp.Content.ReadFromJsonAsync<QuarantineItemDto>();
        Assert.Equal("ApprovedForManualAudit", reviewed?.ReviewStatus);
    }

    [Fact]
    public async Task BackupRestoreAndEpochBump_StaleEpochReturns409_RebootstrapResumesSync()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Epoch-Tablet");

        // Take a real SQLite online backup, then push an op
        string backupPath = Path.Combine(host.TempDirectory, "central-backup.db");
        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        db.BackupTo(backupPath);
        Assert.True(File.Exists(backupPath));

        // Admin bumps epoch (or RestoreAndBumpEpoch after restore)
        using HttpRequestMessage bumpReq = new(HttpMethod.Post, "/api/sync/v1/admin/epoch/bump");
        bumpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        bumpReq.Content = JsonContent.Create(new EpochBumpRequest("Post-restore epoch rotation"));
        HttpResponseMessage bumpResp = await adminClient.SendAsync(bumpReq);
        Assert.Equal(HttpStatusCode.OK, bumpResp.StatusCode);
        EpochBumpResponse? bumped = await bumpResp.Content.ReadFromJsonAsync<EpochBumpResponse>();
        Assert.Equal(1, bumped?.PreviousEpoch);
        Assert.Equal(2, bumped?.NewEpoch);

        // Stale epoch on push -> 409 EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED
        SyncOperationEnvelopeV1 op = MakeEnvelope(tablet.DeviceId, host.StoreId, 1, SaleCommittedV3Contract.OperationType, "{\"x\":1}");
        HttpResponseMessage stalePush = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(KnownEpoch: 1, [op]));
        Assert.Equal(HttpStatusCode.Conflict, stalePush.StatusCode);
        ErrorResponseDto? pushErr = await stalePush.Content.ReadFromJsonAsync<ErrorResponseDto>();
        Assert.Equal("EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED", pushErr?.ErrorCode);
        Assert.Equal(2, pushErr?.ServerEpoch);

        // Stale epoch on pull -> 409 EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED
        HttpResponseMessage stalePull = await tablet.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
        Assert.Equal(HttpStatusCode.Conflict, stalePull.StatusCode);

        // The failed bootstrap does not advance any cursor or invent domain state.
        HttpResponseMessage bootResp = await tablet.Http.GetAsync($"/api/sync/v1/bootstrap?storeId={host.StoreId:D}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, bootResp.StatusCode);
        Assert.Equal("ERR_BOOTSTRAP_DOMAIN_SNAPSHOT_UNAVAILABLE",
            (await bootResp.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // Epoch=2 can be used by a currently trusted device to retain unapplied evidence.
        HttpResponseMessage okPush = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(KnownEpoch: bumped!.NewEpoch, [op]));
        Assert.Equal(HttpStatusCode.OK, okPush.StatusCode);
        PushOperationsResponse retained = (await okPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, retained.Results[0].Resolution.Status);
    }

    [Fact]
    public async Task CrossProductCrossTenantReplayAndAuthBoundaries_AllFailClosed()
    {
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Boundary-Tablet");

        // 1. Cross-product header rejected (Salryn.POS != Salryn.Cafe)
        using HttpClient retailClient = host.CreateHttpsClient(productId: "Salryn.POS");
        HttpResponseMessage wrongProd = await retailClient.PostAsJsonAsync(
            "/api/sync/v1/admin/auth/login",
            new AdminLoginRequest(host.AdminUsername, host.AdminPassword));
        Assert.Equal(HttpStatusCode.Forbidden, wrongProd.StatusCode);
        Assert.Equal("ERR_WRONG_PRODUCT", (await wrongProd.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // 2. Device token CANNOT call admin endpoints (strict Device vs Operator RBAC separation)
        HttpResponseMessage deviceOnAdmin = await tablet.Http.GetAsync("/api/sync/v1/admin/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, deviceOnAdmin.StatusCode);

        // 3. Cross-store / cross-tenant pull rejected (403 ERR_TENANT_SCOPE_VIOLATION)
        Guid foreignStoreId = Guid.CreateVersion7();
        HttpResponseMessage crossStorePull = await tablet.Http.GetAsync(
            $"/api/sync/v1/pull?storeId={foreignStoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
        Assert.Equal(HttpStatusCode.Forbidden, crossStorePull.StatusCode);
        Assert.Equal("ERR_TENANT_SCOPE_VIOLATION", (await crossStorePull.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // 4. Cross-store envelope push rejected
        SyncOperationEnvelopeV1 foreignOp = MakeEnvelope(tablet.DeviceId, foreignStoreId, 1, SaleCommittedV3Contract.OperationType, "{}");
        HttpResponseMessage crossStorePush = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [foreignOp]));
        Assert.Equal(HttpStatusCode.Forbidden, crossStorePush.StatusCode);

        // 5. Replayed join code rejected (409 ERR_JOIN_CODE_REPLAYED)
        (_, string pub2) = CentralCrypto.CreateEcdsaP256KeyPair();
        HttpResponseMessage replayJoin = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/enroll",
            new EnrollDeviceRequest(tablet.UsedJoinCode, host.TenantId, host.StoreId, CentralServerOptions.CanonicalProductId, "Clone", pub2));
        Assert.Equal(HttpStatusCode.Conflict, replayJoin.StatusCode);
        Assert.Equal("ERR_JOIN_CODE_REPLAYED", (await replayJoin.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // 6. Replayed challenge nonce rejected (401 ERR_CHALLENGE_REPLAYED)
        string signingPayload = CentralCrypto.BuildChallengeSigningPayload(
            tablet.UsedChallenge.ChallengeId, tablet.UsedChallenge.NonceBase64,
            tablet.DeviceId, CentralServerOptions.CanonicalProductId, host.TenantId, host.StoreId);
        string sig = Convert.ToBase64String(
            tablet.PrivateKey.SignData(Encoding.UTF8.GetBytes(signingPayload), System.Security.Cryptography.HashAlgorithmName.SHA256));
        HttpResponseMessage replayChallenge = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/token",
            new DeviceTokenRequest(tablet.UsedChallenge.ChallengeId, tablet.DeviceId, host.TenantId, host.StoreId, CentralServerOptions.CanonicalProductId, sig));
        Assert.Equal(HttpStatusCode.Unauthorized, replayChallenge.StatusCode);
        Assert.Equal("ERR_CHALLENGE_REPLAYED", (await replayChallenge.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        // 7. Admin brute-force lockout (5 bad passwords -> 6th returns 429 ERR_ADMIN_LOCKED_OUT)
        for (int i = 0; i < 5; i++)
        {
            HttpResponseMessage bad = await adminClient.PostAsJsonAsync(
                "/api/sync/v1/admin/auth/login",
                new AdminLoginRequest(host.AdminUsername, "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }
        HttpResponseMessage locked = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/admin/auth/login",
            new AdminLoginRequest(host.AdminUsername, host.AdminPassword));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("ERR_ADMIN_LOCKED_OUT", (await locked.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);
    }

    [Fact]
    public async Task SecurityHardening_UntrustedForwardedProtoRejected_AndExpiredRevokedTokenRejected()
    {
        // P1 #2: Untrusted non-loopback RemoteIpAddress cannot spoof X-Forwarded-Proto: https
        Microsoft.AspNetCore.Http.DefaultHttpContext spoofedCtx = new();
        spoofedCtx.Request.Scheme = "http";
        spoofedCtx.Request.Headers["X-Forwarded-Proto"] = "https";
        spoofedCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        CentralServerOptions opts = new() { RequireHttps = true, TrustForwardedProto = true };
        Assert.False(CentralSyncEndpoints.IsHttpsRequest(spoofedCtx, opts));

        // Missing peer address is not evidence of a trusted reverse proxy.
        // A caller-controlled forwarded header must fail closed in this case.
        spoofedCtx.Connection.RemoteIpAddress = null;
        Assert.False(CentralSyncEndpoints.IsHttpsRequest(spoofedCtx, opts));

        // Loopback proxy IS trusted
        spoofedCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        Assert.True(CentralSyncEndpoints.IsHttpsRequest(spoofedCtx, opts));

        // P1 #1: Revoked device with EXPIRED token cannot flood quarantine (401 ERR_DEVICE_TOKEN_EXPIRED)
        using CentralSyncTestHost host = new();
        using HttpClient adminClient = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(adminClient);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(adminClient, adminToken, "Expired-Revoked");

        using (HttpRequestMessage revokeReq = new(HttpMethod.Post, $"/api/sync/v1/admin/devices/{tablet.DeviceId:D}/revoke"))
        {
            revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            revokeReq.Content = JsonContent.Create(new RevokeDeviceRequest("Revoked"));
            Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(revokeReq)).StatusCode);
        }

        // Backdate token expiry in SQLite to simulate an expired stolen token
        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        using (Microsoft.Data.Sqlite.SqliteConnection conn = db.OpenConnection())
        using (Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE sync_device_tokens SET expires_at_utc = $past WHERE device_id = $did;";
            cmd.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
            cmd.Parameters.AddWithValue("$did", tablet.DeviceId.ToString("D"));
            cmd.ExecuteNonQuery();
        }

        SyncOperationEnvelopeV1 op = MakeEnvelope(tablet.DeviceId, host.StoreId, 1, SaleCommittedV3Contract.OperationType, "{\"x\":1}");
        HttpResponseMessage expiredPush = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [op]));
        Assert.Equal(HttpStatusCode.Unauthorized, expiredPush.StatusCode);
        Assert.Equal("ERR_DEVICE_TOKEN_EXPIRED", (await expiredPush.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);
    }
    [Fact]
    public async Task UnsupportedOperation_FailsWholeBatchWithoutInboxOrAppliedFeed()
    {
        using CentralSyncTestHost host = new();
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "Unsupported-Tablet");

        SyncOperationEnvelopeV1 known = MakeEnvelope(
            tablet.DeviceId, host.StoreId, 1, SaleCommittedV3Contract.OperationType, "{\"invoice\":1}");
        SyncOperationEnvelopeV1 unknown = MakeEnvelope(
            tablet.DeviceId, host.StoreId, 2, "UnknownLedgerOperationV9", "{\"invoice\":2}");
        HttpResponseMessage response = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [known, unknown]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("ERR_UNSUPPORTED_OPERATION_TYPE",
            (await response.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);

        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        using Microsoft.Data.Sqlite.SqliteConnection conn = db.OpenConnection();
        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox_operations;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        cmd.CommandText = "SELECT COUNT(*) FROM sync_change_feed;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public async Task ConcurrentSameIdDivergence_AtMostOneDurableUnappliedReceipt()
    {
        using CentralSyncTestHost host = new();
        using HttpClient admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "Racing-Tablet");

        Guid sharedId = Guid.CreateVersion7();
        SyncOperationEnvelopeV1 a = MakeEnvelope(tablet.DeviceId, host.StoreId, 1,
            SaleCommittedV3Contract.OperationType, "{\"total\":100}", sharedId);
        SyncOperationEnvelopeV1 b = MakeEnvelope(tablet.DeviceId, host.StoreId, 1,
            SaleCommittedV3Contract.OperationType, "{\"total\":200}", sharedId);
        HttpResponseMessage[] responses = await Task.WhenAll(
            tablet.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [a])),
            tablet.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [b])));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        using Microsoft.Data.Sqlite.SqliteConnection conn = db.OpenConnection();
        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox_operations WHERE operation_id = $id;";
        cmd.Parameters.AddWithValue("$id", sharedId.ToString("D"));
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        cmd.CommandText = "SELECT COUNT(*) FROM sync_change_feed;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public async Task DeviceSequenceCollision_DoesNotAdmitSecondFinancialFact()
    {
        using CentralSyncTestHost host = new();
        using HttpClient admin = host.CreateHttpsClient();
        string token = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, token, "Sequence-Tablet");
        SyncOperationEnvelopeV1 a = MakeEnvelope(tablet.DeviceId, host.StoreId, 1,
            SaleCommittedV3Contract.OperationType, "{\"amount\":1}");
        SyncOperationEnvelopeV1 b = MakeEnvelope(tablet.DeviceId, host.StoreId, 1,
            SaleCommittedV3Contract.OperationType, "{\"amount\":2}");
        Assert.Equal(HttpStatusCode.OK,
            (await tablet.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [a]))).StatusCode);
        HttpResponseMessage second = await tablet.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [b]));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ERR_DEVICE_LOCAL_SEQUENCE_CONFLICT",
            (await second.Content.ReadFromJsonAsync<ErrorResponseDto>())?.ErrorCode);
    }

    [Fact]
    public async Task StaleRestoreRefusesDestructiveOverwrite_AndPreservesAcknowledgedInbox()
    {
        using CentralSyncTestHost host = new();
        using HttpClient admin = host.CreateHttpsClient();
        string token = await host.LoginAsAdminAsync(admin);
        using EnrolledDeviceClient tablet = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, token, "Restore-Tablet");
        CentralSyncDatabase db = host.Factory.Services.GetRequiredService<CentralSyncDatabase>();
        string oldBackup = Path.Combine(host.TempDirectory, "stale-backup.db");
        db.BackupTo(oldBackup);

        SyncOperationEnvelopeV1 op = MakeEnvelope(tablet.DeviceId, host.StoreId, 1,
            SaleCommittedV3Contract.OperationType, "{\"amount\":999}");
        HttpResponseMessage push = await tablet.Http.PostAsJsonAsync(
            "/api/sync/v1/push", new PushOperationsRequest(1, [op]));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        Assert.Throws<InvalidOperationException>(() =>
            CentralSyncDatabase.RestoreAndBumpEpoch(oldBackup, host.DatabasePath, "synthetic stale restore"));
        using Microsoft.Data.Sqlite.SqliteConnection conn = db.OpenConnection();
        Assert.Equal(1L, db.ReadServerState(conn).ServerEpoch);
        using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM sync_inbox_operations WHERE operation_id = $id;";
        cmd.Parameters.AddWithValue("$id", op.OperationId.ToString("D"));
        Assert.Equal(SaleCommittedV1Contract.NeedsReview, (string?)cmd.ExecuteScalar());
        cmd.CommandText = "SELECT COUNT(*) FROM sync_change_feed;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

}

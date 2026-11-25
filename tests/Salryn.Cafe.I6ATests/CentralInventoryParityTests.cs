using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CentralInventoryParityTests
{
    [Theory]
    [InlineData("purchase", true)]
    [InlineData("purchase", false)]
    [InlineData("waste", true)]
    [InlineData("waste", false)]
    [InlineData("adjust", true)]
    [InlineData("adjust", false)]
    [InlineData("count", true)]
    [InlineData("count", false)]
    public async Task InventoryAllFourOperations_ApplyOrDenyPerCanonicalHostPermission_UpdateStockAndChangeFeed_AndSecondDevicePulls(
        string kind,
        bool authorized)
    {
        string root = Path.Combine(Path.GetTempPath(), "central-inv-parity-" + Guid.NewGuid().ToString("N"));
        var source = C4ProofDatabase.Create(Path.Combine(root, "source"));
        var target = C4ProofDatabase.Create(Path.Combine(root, "central"));

        try
        {
            target.Execute(
                "UPDATE users SET password_hash=$hash WHERE id=$id;",
                ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
                ("$id", C4ProofIds.ActorUserId.ToString("D")));

            Guid supplierId = Guid.NewGuid();
            foreach (var db in new[] { source, target })
            {
                db.Execute(
                    "INSERT INTO suppliers(id,name,contact_number,notes,is_active) VALUES($id,'Parity Roasters',NULL,NULL,1);",
                    ("$id", supplierId.ToString("D")));
            }

            long initialTargetStock = target.ScalarLong(
                "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                ("$id", C4ProofIds.ItemIngredientId.ToString("D")));

            using var host = new CentralSyncTestHost(
                target.Paths.DatabasePath,
                canonical: true,
                canonicalStore: C4ProofIds.StoreId);
            using var admin = host.CreateHttpsClient();
            string adminToken = await host.LoginAsAdminAsync(admin);
            using var deviceA = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "InvParityA-" + kind);
            using var deviceB = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "InvParityB-" + kind);

            var invStore = new SqliteCafeInventoryStore(source.Factory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid operationId = Guid.NewGuid();
            string table;
            string requiredPermission;
            long expectedDeltaX1000;

            if (kind == "purchase")
            {
                expectedDeltaX1000 = 5000;
                var purchase = invStore.CommitPurchase(new PurchaseReceiptMutation(
                    operationId,
                    Guid.NewGuid(),
                    deviceA.DeviceId,
                    host.StoreId,
                    supplierId,
                    C4ProofIds.ActorUserId,
                    now,
                    [new PurchaseReceiptLineMutation(Guid.NewGuid(), CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId, expectedDeltaX1000, 12500)],
                    "PARITY-PO-1",
                    "Central parity purchase"));
                Assert.True(purchase.IsSuccess, purchase.IsFailure ? purchase.ErrorDetail.Message : null);
                table = "purchase_receipts";
                requiredPermission = "Purchasing.Receive";
            }
            else if (kind == "waste")
            {
                expectedDeltaX1000 = -1500;
                var waste = invStore.CommitWaste(new InventoryWasteMutation(
                    operationId,
                    Guid.NewGuid(),
                    deviceA.DeviceId,
                    host.StoreId,
                    C4ProofIds.ActorUserId,
                    now,
                    CafeInventoryWasteReasons.Spoilage,
                    [new InventoryWasteLineMutation(Guid.NewGuid(), CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId, 1500)],
                    "Central parity waste"));
                Assert.True(waste.IsSuccess, waste.IsFailure ? waste.ErrorDetail.Message : null);
                table = "inventory_waste_events";
                requiredPermission = "Inventory.Waste";
            }
            else if (kind == "adjust")
            {
                expectedDeltaX1000 = 2500;
                var adj = invStore.CommitAdjustment(new InventoryAdjustmentMutation(
                    operationId,
                    Guid.NewGuid(),
                    deviceA.DeviceId,
                    host.StoreId,
                    CafeStockTargetTypes.Ingredient,
                    C4ProofIds.ItemIngredientId,
                    expectedDeltaX1000,
                    "Central parity adjustment",
                    C4ProofIds.ActorUserId,
                    now));
                Assert.True(adj.IsSuccess, adj.IsFailure ? adj.ErrorDetail.Message : null);
                table = "inventory_adjustment_events";
                requiredPermission = "Inventory.Adjust";
            }
            else
            {
                expectedDeltaX1000 = -1000;
                Guid countId = Guid.NewGuid();
                long currentStock = source.ScalarLong(
                    "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                    ("$id", C4ProofIds.ItemIngredientId.ToString("D")));
                var draft = invStore.SaveCountDraft(new InventoryCountStartMutation(
                    countId,
                    host.StoreId,
                    C4ProofIds.ActorUserId,
                    now,
                    "Central parity count",
                    [new InventoryCountLineMutation(Guid.NewGuid(), CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId, currentStock, currentStock + expectedDeltaX1000)]));
                Assert.True(draft.IsSuccess, draft.IsFailure ? draft.ErrorDetail.Message : null);

                var finalized = invStore.FinalizeCount(new InventoryCountFinalizeMutation(
                    operationId,
                    deviceA.DeviceId,
                    host.StoreId,
                    countId,
                    C4ProofIds.ActorUserId,
                    now.AddSeconds(5)));
                Assert.True(finalized.IsSuccess, finalized.IsFailure ? finalized.ErrorDetail.Message : null);
                table = "inventory_count_sessions";
                requiredPermission = "Inventory.Count";
            }

            if (!authorized)
            {
                target.Execute(
                    "DELETE FROM role_permissions WHERE permission=$permission;",
                    ("$permission", requiredPermission));
            }

            using var loginResponse = await deviceA.Http.PostAsJsonAsync(
                "/api/sync/v1/operators/login",
                new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var login = (await loginResponse.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
            deviceA.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

            var envelopes = new SqliteSyncOutboxStore(source.Factory)
                .ReadPending(deviceA.DeviceId)
                .Select(row => row.Envelope)
                .ToArray();
            Assert.Contains(envelopes, env => env.OperationId == operationId);

            using var pushed = await deviceA.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, envelopes));
            Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
            var body = (await pushed.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            var result = Assert.Single(body.Results, row => row.OperationId == operationId);

            Assert.Equal(authorized, result.Resolution.IsApplied);
            if (!authorized)
            {
                Assert.Equal("Rejected", result.Resolution.Status);
                Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY", result.Resolution.ResultCode);
                Assert.Null(result.ChangeFeedSequence);
            }
            else
            {
                Assert.NotNull(result.ChangeFeedSequence);
                Assert.True(result.ChangeFeedSequence > 0);
            }

            Assert.Equal(authorized ? 1L : 0L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
            long expectedFinalStock = authorized ? initialTargetStock + expectedDeltaX1000 : initialTargetStock;
            Assert.Equal(
                expectedFinalStock,
                target.ScalarLong(
                    "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                    ("$id", C4ProofIds.ItemIngredientId.ToString("D"))));

            long expectedFeedCount = authorized ? 1L : 0L;
            Assert.Equal(expectedFeedCount, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));

            // Second device pull catch-up must observe the applied change feed (or empty feed when rejected).
            using var pullResp = await deviceB.Http.GetAsync(
                $"/api/sync/v1/pull?storeId={host.StoreId:D}&afterSequence=0&limit=50&knownEpoch=1");
            Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
            var pullBody = (await pullResp.Content.ReadFromJsonAsync<PullChangesResponseDto>())!;
            if (authorized)
            {
                var pulledChange = Assert.Single(pullBody.Page.Changes, c => c.SourceOperationId == operationId);
                Assert.Equal(result.ChangeFeedSequence, pulledChange.ServerSequence);
                Assert.Equal(result.ChangeFeedSequence, pullBody.Page.HighWatermarkServerSequence);
            }
            else
            {
                Assert.Empty(pullBody.Page.Changes);
                Assert.Equal(0, pullBody.Page.HighWatermarkServerSequence);
            }

            // Replay push must be idempotent with zero duplicate mutations or double stock changes.
            using var retry = await deviceA.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, envelopes));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var retryBody = (await retry.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.All(retryBody.Results, row => Assert.True(row.IsDuplicateReplay));
            Assert.Equal(authorized ? 1L : 0L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
            Assert.Equal(
                expectedFinalStock,
                target.ScalarLong(
                    "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                    ("$id", C4ProofIds.ItemIngredientId.ToString("D"))));
            Assert.Equal(expectedFeedCount, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task MidSessionPermissionRevocation_RejectsSubsequentInventoryPushWithCanonicalAuthorityError()
    {
        string root = Path.Combine(Path.GetTempPath(), "central-inv-revoke-" + Guid.NewGuid().ToString("N"));
        var source = C4ProofDatabase.Create(Path.Combine(root, "source"));
        var target = C4ProofDatabase.Create(Path.Combine(root, "central"));

        try
        {
            target.Execute(
                "UPDATE users SET password_hash=$hash WHERE id=$id;",
                ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
                ("$id", C4ProofIds.ActorUserId.ToString("D")));

            long initialStock = target.ScalarLong(
                "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                ("$id", C4ProofIds.ItemIngredientId.ToString("D")));

            using var host = new CentralSyncTestHost(
                target.Paths.DatabasePath,
                canonical: true,
                canonicalStore: C4ProofIds.StoreId);
            using var admin = host.CreateHttpsClient();
            string adminToken = await host.LoginAsAdminAsync(admin);
            using var device = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "InvMidRevoke");

            // Login ONCE before revocation
            using var loginResponse = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/operators/login",
                new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var login = (await loginResponse.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
            device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

            var invStore = new SqliteCafeInventoryStore(source.Factory);
            var outbox = new SqliteSyncOutboxStore(source.Factory);

            // Operation 1 while authorized -> must succeed
            Guid op1 = Guid.NewGuid();
            Assert.True(invStore.CommitAdjustment(new InventoryAdjustmentMutation(
                op1, Guid.NewGuid(), device.DeviceId, host.StoreId,
                CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId,
                1000, "Before revocation", C4ProofIds.ActorUserId, DateTimeOffset.UtcNow)).IsSuccess);

            var env1 = outbox.ReadPending(device.DeviceId).Single(r => r.Envelope.OperationId == op1).Envelope;
            using var push1 = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [env1]));
            Assert.Equal(HttpStatusCode.OK, push1.StatusCode);
            var body1 = (await push1.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.True(Assert.Single(body1.Results).Resolution.IsApplied);
            Assert.Equal(
                initialStock + 1000,
                target.ScalarLong("SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;", ("$id", C4ProofIds.ItemIngredientId.ToString("D"))));

            // Revoke Inventory.Adjust MID-SESSION on Central without issuing a new token
            target.Execute("DELETE FROM role_permissions WHERE permission='Inventory.Adjust';");

            // Operation 2 using the ALREADY-ISSUED operator token -> must fail closed!
            Guid op2 = Guid.NewGuid();
            Assert.True(invStore.CommitAdjustment(new InventoryAdjustmentMutation(
                op2, Guid.NewGuid(), device.DeviceId, host.StoreId,
                CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId,
                3000, "After mid-session revocation", C4ProofIds.ActorUserId, DateTimeOffset.UtcNow.AddSeconds(5))).IsSuccess);

            var env2 = outbox.ReadPending(device.DeviceId).Single(r => r.Envelope.OperationId == op2).Envelope;
            using var push2 = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, [env2]));
            Assert.Equal(HttpStatusCode.Forbidden, push2.StatusCode);
            string errText = await push2.Content.ReadAsStringAsync();
            Assert.Contains("ERR_OPERATOR_AUTHORITY_CHANGED", errText, StringComparison.OrdinalIgnoreCase);

            // Stock must remain at initialStock + 1000 (unchanged by op2)
            Assert.Equal(
                initialStock + 1000,
                target.ScalarLong("SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;", ("$id", C4ProofIds.ItemIngredientId.ToString("D"))));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void BookingDetailRead_ReturnsHistoricalDepositFactsWithPartialAndMultiDepositRefundBalances()
    {
        string root = Path.Combine(Path.GetTempPath(), "booking-deposit-facts-" + Guid.NewGuid().ToString("N"));
        var db = C4ProofDatabase.Create(root);

        try
        {
            Guid customerId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            db.Execute(
                "INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Historical Deposit Customer',1,$actor,$at,$at);",
                ("$id", customerId.ToString("D")),
                ("$actor", C4ProofIds.ActorUserId.ToString("D")),
                ("$at", now.ToString("O")));

            var bookingService = new SqliteCafeBookingService(db.Factory);
            Guid bookingId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();

            var config = new CafeBookingConfigurationMutation(
                customerId,
                CafeBookingModes.Event,
                "Corporate Coffee Bar",
                "EVT-101",
                now.AddDays(5),
                now.AddDays(5).AddHours(3),
                "Makati Hall",
                null,
                null,
                null,
                40,
                "Full Espresso Bar",
                C4ProofIds.MenuRevisionId,
                2000,
                null,
                null,
                "Takeout",
                [new CafeBookingLineMutation(Guid.NewGuid(), C4ProofIds.ProductId, null, [], 10000, null, 0)]);

            var created = bookingService.Create(new CafeBookingCreateMutation(
                Guid.NewGuid(), bookingId, revisionId, C4ProofIds.DeviceId,
                C4ProofIds.StoreId, C4ProofIds.ActorUserId, now, config));
            Assert.True(created.IsSuccess, created.IsFailure ? created.ErrorDetail.Message : null);

            var confirmed = bookingService.AdvanceStatus(new CafeBookingStatusMutation(
                Guid.NewGuid(), bookingId, C4ProofIds.DeviceId, C4ProofIds.StoreId,
                1, C4ProofIds.ActorUserId, now.AddSeconds(10), CafeBookingStatuses.Confirmed));
            Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.ErrorDetail.Message : null);

            Guid shiftId = Guid.Parse(
                db.ScalarString("SELECT id FROM cashier_sessions WHERE status=1 LIMIT 1;")
                ?? throw new InvalidOperationException("Seeded open cashier session missing."));

            Guid dep1 = Guid.NewGuid();
            Guid dep2 = Guid.NewGuid();
            var r1 = bookingService.ReceiveDeposit(new CafeBookingDepositReceiveMutation(
                Guid.NewGuid(), dep1, Guid.NewGuid(), C4ProofIds.DeviceId, C4ProofIds.StoreId,
                bookingId, revisionId, C4ProofIds.ActorUserId, shiftId, 3000, "DEP-001", now.AddMinutes(1)));
            Assert.True(r1.IsSuccess, r1.IsFailure ? r1.ErrorDetail.Message : null);
            var r2 = bookingService.ReceiveDeposit(new CafeBookingDepositReceiveMutation(
                Guid.NewGuid(), dep2, Guid.NewGuid(), C4ProofIds.DeviceId, C4ProofIds.StoreId,
                bookingId, revisionId, C4ProofIds.ActorUserId, shiftId, 1500, "DEP-002", now.AddMinutes(2)));
            Assert.True(r2.IsSuccess, r2.IsFailure ? r2.ErrorDetail.Message : null);

            // Commit a partial multi-deposit refund (10 PHP from dep1, 5 PHP from dep2)
            var refund = bookingService.RefundDeposit(new CafeBookingDepositRefundMutation(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), C4ProofIds.DeviceId, C4ProofIds.StoreId,
                bookingId, revisionId, C4ProofIds.ActorUserId, shiftId, 1500, "Partial headcount reduction", "REF-001",
                now.AddMinutes(3),
                [
                    new CafeBookingDepositAllocationMutation(dep1, 1000),
                    new CafeBookingDepositAllocationMutation(dep2, 500),
                ]));
            Assert.True(refund.IsSuccess, refund.IsFailure ? refund.ErrorDetail.Message : null);

            // Read fresh from SQLite via Get(storeId, actorUserId, bookingId) — zero in-memory state required!
            var detailRes = bookingService.Get(C4ProofIds.StoreId, C4ProofIds.ActorUserId, bookingId);
            Assert.True(detailRes.IsSuccess, detailRes.IsFailure ? detailRes.ErrorDetail.Message : null);
            CafeBookingDetail detail = detailRes.Value!;
            Assert.NotNull(detail.DepositFacts);

            var fact1 = Assert.Single(detail.DepositFacts!, f => f.DepositFactId == dep1);
            Assert.Equal("Received", fact1.FactType);
            Assert.Equal("DEP-001", fact1.Reference);
            Assert.Equal(3000, fact1.AmountCents);
            Assert.Equal(1000, fact1.AllocatedAmountCents);
            Assert.Equal(2000, fact1.RemainingRefundableCents);

            var fact2 = Assert.Single(detail.DepositFacts!, f => f.DepositFactId == dep2);
            Assert.Equal("Received", fact2.FactType);
            Assert.Equal("DEP-002", fact2.Reference);
            Assert.Equal(1500, fact2.AmountCents);
            Assert.Equal(500, fact2.AllocatedAmountCents);
            Assert.Equal(1000, fact2.RemainingRefundableCents);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}

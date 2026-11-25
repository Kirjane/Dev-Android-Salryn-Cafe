using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CentralInventoryParityTests
{
    [Theory]
    [InlineData("adjust", true)]
    [InlineData("adjust", false)]
    [InlineData("count", true)]
    [InlineData("count", false)]
    public async Task InventoryAdjustAndCount_ApplyOrDenyPerCanonicalHostPermission_AndReplayExactlyOnce(
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

            using var host = new CentralSyncTestHost(
                target.Paths.DatabasePath,
                canonical: true,
                canonicalStore: C4ProofIds.StoreId);
            using var admin = host.CreateHttpsClient();
            string adminToken = await host.LoginAsAdminAsync(admin);
            using var device = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "InvParity-" + kind);

            var invStore = new SqliteCafeInventoryStore(source.Factory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid operationId = Guid.NewGuid();
            string table;
            string requiredPermission;

            if (kind == "adjust")
            {
                var adj = invStore.CommitAdjustment(new InventoryAdjustmentMutation(
                    operationId,
                    Guid.NewGuid(),
                    device.DeviceId,
                    host.StoreId,
                    CafeStockTargetTypes.Ingredient,
                    C4ProofIds.ItemIngredientId,
                    2500,
                    "Central parity adjustment",
                    C4ProofIds.ActorUserId,
                    now));
                Assert.True(adj.IsSuccess, adj.IsFailure ? adj.ErrorDetail.Message : null);
                table = "inventory_adjustment_events";
                requiredPermission = "Inventory.Adjust";
            }
            else
            {
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
                    [new InventoryCountLineMutation(Guid.NewGuid(), CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId, currentStock, currentStock - 1000)]));
                Assert.True(draft.IsSuccess, draft.IsFailure ? draft.ErrorDetail.Message : null);

                var finalized = invStore.FinalizeCount(new InventoryCountFinalizeMutation(
                    operationId,
                    device.DeviceId,
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

            using var loginResponse = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/operators/login",
                new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var login = (await loginResponse.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
            device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

            var envelopes = new SqliteSyncOutboxStore(source.Factory)
                .ReadPending(device.DeviceId)
                .Select(row => row.Envelope)
                .ToArray();
            Assert.Contains(envelopes, env => env.OperationId == operationId);

            using var pushed = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, envelopes));
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
            Assert.Equal(authorized ? 1L : 0L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));

            // Replay push must be idempotent with zero duplicate mutations.
            using var retry = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, envelopes));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var retryBody = (await retry.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.All(retryBody.Results, row => Assert.True(row.IsDuplicateReplay));
            Assert.Equal(authorized ? 1L : 0L, target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
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

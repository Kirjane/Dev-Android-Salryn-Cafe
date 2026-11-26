using System.Security.Cryptography;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class NativeOrderLifecycleAndDurabilityTests
{
    [Fact]
    public async Task ParkResumeAndCancelOrder_EnforceRbacIdempotencyAndCanonicalOutbox()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();

        Guid orderId = Guid.NewGuid();
        var saved = CanonicalCashierFixture.Value(await fx.Controller.SaveOrderAsync(new(
            Guid.NewGuid(), orderId, 0, fx.Now, CafeOrderServiceModes.DineIn, "Table 12",
            [new(Guid.NewGuid(), fx.ProductId, null, [], 1000, "No sugar", 0)])));

        // Empty operationId fails closed before touching canonical state
        var emptyPark = await fx.Controller.ParkOrderAsync(new(Guid.Empty, orderId, saved.OrderVersion, fx.Now));
        Assert.True(emptyPark.IsFailure);
        Assert.Equal("Mobile.OperationIdRequired", emptyPark.ErrorDetail.Code);

        // Park and idempotent replay
        Guid parkOpId = Guid.NewGuid();
        var parked = CanonicalCashierFixture.Value(await fx.Controller.ParkOrderAsync(new(parkOpId, orderId, saved.OrderVersion, fx.Now)));
        Assert.Equal(CafeOrderStates.Parked, parked.State);
        var parkedReplay = CanonicalCashierFixture.Value(await fx.Controller.ParkOrderAsync(new(parkOpId, orderId, saved.OrderVersion, fx.Now)));
        Assert.True(parkedReplay.Idempotent);
        Assert.Equal(parked.OrderVersion, parkedReplay.OrderVersion);

        // Resume and idempotent replay
        Guid resumeOpId = Guid.NewGuid();
        var resumed = CanonicalCashierFixture.Value(await fx.Controller.ResumeOrderAsync(new(resumeOpId, orderId, parked.OrderVersion, fx.Now)));
        Assert.Equal(CafeOrderStates.Open, resumed.State);
        var resumedReplay = CanonicalCashierFixture.Value(await fx.Controller.ResumeOrderAsync(new(resumeOpId, orderId, parked.OrderVersion, fx.Now)));
        Assert.True(resumedReplay.Idempotent);

        // Cancel requires non-empty reason and Orders.Cancel permission
        var emptyReason = await fx.Controller.CancelOrderAsync(new(Guid.NewGuid(), orderId, resumed.OrderVersion, fx.Now, "   "));
        Assert.True(emptyReason.IsFailure);

        fx.Execute($"DELETE FROM role_permissions WHERE permission='{SalrynPermissions.OrdersCancel}';");
        var deniedCancel = await fx.Controller.CancelOrderAsync(new(Guid.NewGuid(), orderId, resumed.OrderVersion, fx.Now, "Guest left"));
        Assert.True(deniedCancel.IsFailure);
        Assert.Equal("Mobile.PermissionDenied", deniedCancel.ErrorDetail.Code);

        Guid roleId = Guid.Parse(fx.Scalar("SELECT role_id FROM users LIMIT 1;").ToString()!);
        fx.Execute($"INSERT INTO role_permissions(role_id, permission, created_at) VALUES('{roleId:D}', '{SalrynPermissions.OrdersCancel}', '{fx.Now:O}');");

        Guid cancelOpId = Guid.NewGuid();
        var cancelled = CanonicalCashierFixture.Value(await fx.Controller.CancelOrderAsync(new(cancelOpId, orderId, resumed.OrderVersion, fx.Now, "Guest left")));
        Assert.Equal(CafeOrderStates.Cancelled, cancelled.State);
        var cancelReplay = CanonicalCashierFixture.Value(await fx.Controller.CancelOrderAsync(new(cancelOpId, orderId, resumed.OrderVersion, fx.Now, "Guest left")));
        Assert.True(cancelReplay.Idempotent);

        Assert.Equal(4L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sync_outbox WHERE operation_type IN ('OrderRevisionCommittedV1','OrderParkedV1','OrderResumedV1','OrderCancelledV1');")));
    }

    [Fact]
    public async Task PendingIntentStore_PersistsAndResolvesCancelOrderAndDeliveryAdvance()
    {
        InMemorySecretStore secrets = new();
        MobileCashierIntentStore store = new(secrets);
        Guid actorId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Guid cancelOpId = Guid.NewGuid();
        var cancelIntent = new MobilePendingCashierIntent(
            "CancelOrder",
            ActorUserId: actorId,
            CancelOrder: new(cancelOpId, Guid.NewGuid(), 2, now, "Customer changed order"));
        await store.SaveBeforeDispatchAsync(cancelIntent);
        var loadedCancel = await new MobileCashierIntentStore(secrets).ReadAsync();
        Assert.NotNull(loadedCancel);
        Assert.Equal(cancelOpId, loadedCancel!.OperationId);
        await store.ClearConfirmedAsync(cancelOpId);
        Assert.Null(await store.ReadAsync());

        Guid deliveryOpId = Guid.NewGuid();
        var deliveryIntent = new MobilePendingCashierIntent(
            "DeliveryAdvance",
            ActorUserId: actorId,
            DeliveryAdvance: new(deliveryOpId, Guid.NewGuid(), 1, now, CafeDeliveryStatuses.Dispatched, null));
        await store.SaveBeforeDispatchAsync(deliveryIntent);
        await store.RecordAttemptFailureAsync(deliveryOpId, "CafeDelivery.VersionConflict", "Stale delivery version.");
        var resolved = await store.ResolveRejectedAsync(deliveryOpId, actorId, "Delivery already dispatched on host.", now.AddMinutes(1));
        Assert.Equal("DeliveryAdvance", resolved.Kind);
        Assert.Equal(deliveryOpId, resolved.OperationId);
        Assert.Null(await store.ReadAsync());
        Assert.Single(await store.ReadResolutionHistoryAsync());
    }

    [Fact]
    public async Task OfflineLeaseGuard_BlocksClockRollbackBootRegressionAndPersistedRevocationFence()
    {
        InMemorySecretStore secrets = new();
        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string spki = Convert.ToBase64String(serverKey.ExportSubjectPublicKeyInfo());
        Guid deviceId = Guid.NewGuid(), tenantId = Guid.NewGuid(), storeId = Guid.NewGuid();
        DateTimeOffset issued = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset expires = issued.AddHours(2);
        string payload = $"{deviceId:D}|{tenantId:D}|{storeId:D}|Salryn.Cafe|1|{issued:O}|{expires:O}";
        string sig = Convert.ToBase64String(serverKey.SignData(System.Text.Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));
        MobileSignedOfflineLease lease = new(deviceId, tenantId, storeId, "Salryn.Cafe", 1, issued, expires, sig, spki);

        MobileOfflineLeaseGuard guard = new(secrets, spki);
        DateTimeOffset t0 = issued.AddMinutes(10);
        var ok = await guard.EvaluateAndAdvanceAsync(lease, deviceId, tenantId, storeId, t0, 50_000, 3);
        Assert.True(ok.IsAuthorized);

        // Wall-clock rollback > 2m tolerance must lock
        var wallRollback = await guard.EvaluateAndAdvanceAsync(lease, deviceId, tenantId, storeId, t0.AddMinutes(-5), 60_000, 3);
        Assert.False(wallRollback.IsAuthorized);
        Assert.Equal("LEASE_CLOCK_TAMPER_LOCKED", wallRollback.Code);

        // Boot counter regression must lock
        var bootRegression = await guard.EvaluateAndAdvanceAsync(lease, deviceId, tenantId, storeId, t0.AddMinutes(1), 65_000, 2);
        Assert.False(bootRegression.IsAuthorized);
        Assert.Equal("LEASE_CLOCK_TAMPER_LOCKED", bootRegression.Code);

        // Revocation fence persists across new guard instances and blocks even valid leases
        await guard.MarkRevokedFencedAsync(t0.AddMinutes(2), 70_000, 3);
        MobileOfflineLeaseGuard restartedGuard = new(secrets, spki);
        var fenced = await restartedGuard.EvaluateAndAdvanceAsync(lease, deviceId, tenantId, storeId, t0.AddMinutes(3), 80_000, 3);
        Assert.False(fenced.IsAuthorized);
        Assert.Equal("DEVICE_REVOKED_FENCED", fenced.Code);
    }
}

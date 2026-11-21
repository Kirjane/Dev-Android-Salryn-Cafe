using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Printing;
using Salryn.Mobile.Foundation.Security;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class NativeOperationalCashierTests
{
    [Fact]
    public async Task EnrollmentWriter_FailsClosedOnUnpinnedOrForgedLease_AndPersistsOnlyVerifiedBinding()
    {
        InMemorySecretStore secrets = new();
        ProtectedMobileEnrollmentWriter writer = new(secrets);
        ProtectedMobileEnrollmentReader reader = new(secrets);

        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa rogueKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pinnedPublicKey = Convert.ToBase64String(serverKey.ExportSubjectPublicKeyInfo());
        string roguePublicKey = Convert.ToBase64String(rogueKey.ExportSubjectPublicKeyInfo());

        Guid deviceId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MobileClockSnapshot clock = new(now, 10_000, 1);

        MobileSignedOfflineLease validLease = SignLease(serverKey, pinnedPublicKey, deviceId, tenantId, storeId, "Salryn.Cafe", 1, now.AddMinutes(-1), now.AddHours(1));
        MobileEnrollmentBinding validBinding = new(deviceId, tenantId, storeId, "Salryn.Cafe", 1, pinnedPublicKey, validLease);

        // 1. Missing pinned key
        var missingPin = await writer.WriteVerifiedAsync(validBinding, "", clock);
        Assert.True(missingPin.IsFailure);
        Assert.Equal("Mobile.PinnedKeyRequired", missingPin.ErrorDetail.Code);
        Assert.Null(await reader.ReadAsync());

        // 2. Unauthenticated/rogue server key embedded in lease
        MobileSignedOfflineLease rogueLease = SignLease(rogueKey, roguePublicKey, deviceId, tenantId, storeId, "Salryn.Cafe", 1, now.AddMinutes(-1), now.AddHours(1));
        MobileEnrollmentBinding rogueBinding = new(deviceId, tenantId, storeId, "Salryn.Cafe", 1, roguePublicKey, rogueLease);
        var untrusted = await writer.WriteVerifiedAsync(rogueBinding, pinnedPublicKey, clock);
        Assert.True(untrusted.IsFailure);
        Assert.Equal("Mobile.UntrustedServerKey", untrusted.ErrorDetail.Code);
        Assert.Null(await reader.ReadAsync());

        // 3. Forged signature claiming pinned public key
        MobileSignedOfflineLease forgedLease = validLease with { SignatureBase64 = Convert.ToBase64String(new byte[64]) };
        var forged = await writer.WriteVerifiedAsync(validBinding with { Lease = forgedLease }, pinnedPublicKey, clock);
        Assert.True(forged.IsFailure);
        Assert.Equal("LEASE_SIGNATURE_INVALID", forged.ErrorDetail.Code);
        Assert.Null(await reader.ReadAsync());

        // 4. Wrong product
        MobileSignedOfflineLease wrongProductLease = SignLease(serverKey, pinnedPublicKey, deviceId, tenantId, storeId, "Salryn.Retail", 1, now.AddMinutes(-1), now.AddHours(1));
        var wrongProduct = await writer.WriteVerifiedAsync(
            new(deviceId, tenantId, storeId, "Salryn.Retail", 1, pinnedPublicKey, wrongProductLease), pinnedPublicKey, clock);
        Assert.True(wrongProduct.IsFailure);
        Assert.Equal("ERR_WRONG_PRODUCT", wrongProduct.ErrorDetail.Code);
        Assert.Null(await reader.ReadAsync());

        // 5. Expired lease
        MobileSignedOfflineLease expiredLease = SignLease(serverKey, pinnedPublicKey, deviceId, tenantId, storeId, "Salryn.Cafe", 1, now.AddHours(-2), now.AddMinutes(-1));
        var expired = await writer.WriteVerifiedAsync(validBinding with { Lease = expiredLease }, pinnedPublicKey, clock);
        Assert.True(expired.IsFailure);
        Assert.Equal("LEASE_EXPIRED", expired.ErrorDetail.Code);
        Assert.Null(await reader.ReadAsync());

        // 6. Verified enrollment succeeds and authorizes guard after restart
        var written = await writer.WriteVerifiedAsync(validBinding, pinnedPublicKey, clock);
        Assert.True(written.IsSuccess);
        MobileEnrollmentBinding? persisted = await new ProtectedMobileEnrollmentReader(secrets).ReadAsync();
        Assert.NotNull(persisted);
        Assert.Equal(deviceId, persisted!.DeviceId);
        Assert.Equal(pinnedPublicKey, persisted.TrustedServerPublicKeySpkiBase64);

        MobileCashierDeviceGuard guard = new(new ProtectedMobileEnrollmentReader(secrets), secrets, () => new(now.AddSeconds(5), 15_000, 1));
        var authorized = await guard.AuthorizeAsync();
        Assert.True(authorized.IsSuccess);
    }

    [Fact]
    public async Task PendingIntentRecovery_PreservesAuditabilityAndAllowsResolvingRejectedIntents()
    {
        InMemorySecretStore secrets = new();
        MobileCashierIntentStore store = new(secrets);
        Guid actorId = Guid.NewGuid();
        Guid resolverId = Guid.NewGuid();
        Guid opId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        MobilePendingCashierIntent intent = new(
            "SaveOrder",
            Draft: new(opId, Guid.NewGuid(), 0, now, CafeOrderServiceModes.Takeout, null, []),
            ActorUserId: actorId);

        await store.SaveBeforeDispatchAsync(intent);
        var failedOnce = await store.RecordAttemptFailureAsync(opId, "Orders.EmptyLines", "Order must contain at least one line.");
        Assert.Equal(1, failedOnce.AttemptCount);
        Assert.Equal("Orders.EmptyLines", failedOnce.LastErrorCode);

        // Retrying the same semantic intent after recording failure is allowed; a different intent is still blocked.
        MobileCashierIntentStore restarted = new(secrets);
        await restarted.SaveBeforeDispatchAsync(intent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SaveBeforeDispatchAsync(
            intent with { Draft = intent.Draft! with { OperationId = Guid.NewGuid() } }));

        // Resolving rejected intent records original actor/operationId and unblocks the store.
        var resolution = await restarted.ResolveRejectedAsync(opId, resolverId, "Invalid empty draft rejected by domain.", now.AddMinutes(1));
        Assert.Equal(opId, resolution.OperationId);
        Assert.Equal(actorId, resolution.OriginalActorUserId);
        Assert.Equal(resolverId, resolution.ResolvedByUserId);
        Assert.Equal("DismissedRejected", resolution.ResolutionStatus);
        Assert.Null(await restarted.ReadAsync());

        var history = await restarted.ReadResolutionHistoryAsync();
        Assert.Single(history);
        Assert.Equal(opId, history[0].OperationId);
        Assert.Equal(actorId, history[0].OriginalActorUserId);
    }

    [Fact]
    public async Task ShiftOpenAndClose_ParticipateInOperationIdIdempotencyAcrossRetriesAndRestart()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();

        var emptyOpen = await fx.Controller.OpenShiftAsync(new MobileShiftOpenIntent(Guid.Empty, 10000, fx.Now));
        Assert.True(emptyOpen.IsFailure);
        Assert.Equal("Mobile.OperationIdRequired", emptyOpen.ErrorDetail.Code);

        Guid openOpId = Guid.NewGuid();
        var opened = CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new MobileShiftOpenIntent(openOpId, 10000, fx.Now)));
        var openedRetry = CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new MobileShiftOpenIntent(openOpId, 10000, fx.Now)));
        Assert.Equal(opened.Id, openedRetry.Id);

        Guid closeOpId = Guid.NewGuid();
        var closed = CanonicalCashierFixture.Value(await fx.Controller.CloseShiftAsync(new MobileShiftCloseIntent(closeOpId, opened.Id, 10000, fx.Now)));
        Assert.Equal(CashierSessionStatus.Closed, closed.Status);

        // Simulate restart and retry of the same close operationId after lost acknowledgement.
        var restarted = fx.NewController();
        await fx.LoginAsync(restarted);
        var closedRetry = CanonicalCashierFixture.Value(await restarted.CloseShiftAsync(new MobileShiftCloseIntent(closeOpId, opened.Id, 10000, fx.Now)));
        Assert.Equal(closed.Id, closedRetry.Id);
        Assert.Equal(CashierSessionStatus.Closed, closedRetry.Status);
    }

    [Fact]
    public async Task PrintWorkflow_RecordsFailureAndAllowsRetryOnSameJobId_ForReceiptAndKitchen()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new MobileShiftOpenIntent(Guid.NewGuid(), 10000, fx.Now)));
        var order = await fx.ReadyOrderAsync();
        var checkoutIntent = CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(
            Guid.NewGuid(), order.Order.OrderId, order.Order.OrderVersion, fx.Now, 12500, 0, null));
        var sale = CanonicalCashierFixture.Value(await fx.Controller.CompleteAsync(checkoutIntent));
        fx.Execute($"UPDATE sales SET receipt_snapshot = 'SALRYN CAFE RECEIPT {sale.ReceiptNumber}' WHERE id = '{sale.SaleId:D}';");

        // Publish receipt and kitchen stations
        Guid receiptStationId = Guid.NewGuid();
        Guid kitchenStationId = Guid.NewGuid();
        CanonicalCashierFixture.Value(fx.Runtime.CafePrint.PublishStation(new(
            Guid.NewGuid(), receiptStationId, fx.StoreId, fx.ActorId, "Counter Receipt",
            CafePrinterStationKind.CustomerReceipt, "BT:00:11:22:33:44:55", true, 0, fx.Now)));
        CanonicalCashierFixture.Value(fx.Runtime.CafePrint.PublishStation(new(
            Guid.NewGuid(), kitchenStationId, fx.StoreId, fx.ActorId, "Bar Kitchen",
            CafePrinterStationKind.Kitchen, "BT:AA:BB:CC:DD:EE:FF", true, 0, fx.Now)));

        var stations = CanonicalCashierFixture.Value(await fx.Controller.ListPrinterStationsAsync());
        Assert.Equal(2, stations.Count);

        // Create receipt print job and fail first attempt, then retry same jobId and succeed
        Guid receiptJobId = Guid.NewGuid();
        var receiptJob = CanonicalCashierFixture.Value(await fx.Controller.CreateReceiptPrintJobAsync(new(
            Guid.NewGuid(), receiptJobId, receiptStationId, sale.SaleId, CafePrintCopyKind.Original, null, fx.Now)));
        Assert.Equal(receiptJobId, receiptJob.Job.JobId);

        SimulatedEscPosPrinterTransport transport = new() { SimulateOfflineFailure = true };
        MobilePrinterTransportAdapter adapter = new(transport, "BT:00:11:22:33:44:55", "Counter Receipt");

        var firstAttempt = CanonicalCashierFixture.Value(await fx.Controller.ExecutePrintAttemptAsync(receiptJobId, adapter));
        Assert.Equal(CafePrintAttemptStatus.Failed, firstAttempt.Attempt.Status);
        Assert.Equal(1, firstAttempt.Attempt.AttemptNumber);

        transport.SimulateOfflineFailure = false;
        var retryAttempt = CanonicalCashierFixture.Value(await fx.Controller.ExecutePrintAttemptAsync(receiptJobId, adapter));
        Assert.Equal(CafePrintAttemptStatus.Succeeded, retryAttempt.Attempt.Status);
        Assert.Equal(2, retryAttempt.Attempt.AttemptNumber);
        Assert.Single(transport.CapturedPrints);

        // Create and execute kitchen ticket print job
        Guid kitchenJobId = Guid.NewGuid();
        var kitchenJob = CanonicalCashierFixture.Value(await fx.Controller.CreateKitchenPrintJobAsync(new(
            Guid.NewGuid(), kitchenJobId, kitchenStationId, order.Order.OrderId, order.CurrentRevision.RevisionId,
            CafePrintCopyKind.Original, null, fx.Now)));
        var kitchenAttempt = CanonicalCashierFixture.Value(await fx.Controller.ExecutePrintAttemptAsync(kitchenJob.Job.JobId, adapter));
        Assert.Equal(CafePrintAttemptStatus.Succeeded, kitchenAttempt.Attempt.Status);
        Assert.Equal(2, transport.CapturedPrints.Count);
    }

    [Fact]
    public async Task RefundAndDeliveryWorkflows_ExecuteThroughCanonicalAuthority()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        var shift = CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new MobileShiftOpenIntent(Guid.NewGuid(), 10000, fx.Now)));

        // Prepare a Ready order, attach delivery before completion, complete sale, then advance delivery
        var order = await fx.ReadyOrderAsync();

        Guid deliveryId = Guid.NewGuid();
        var createdDelivery = CanonicalCashierFixture.Value(await fx.Controller.CreateDeliveryAsync(new(
            Guid.NewGuid(), deliveryId, order.Order.OrderId, fx.Now, "Synthetic Recipient", "09170000000",
            "123 Synthetic Street", "Near Cafe", "Leave at gate", 0, "RIDER-1")));
        Assert.Equal(CafeDeliveryStatuses.Pending, createdDelivery.Status);

        var checkoutIntent = CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(
            Guid.NewGuid(), order.Order.OrderId, order.Order.OrderVersion, fx.Now, 12500, 0, null));
        var sale = CanonicalCashierFixture.Value(await fx.Controller.CompleteAsync(checkoutIntent));

        var deliveries = CanonicalCashierFixture.Value(await fx.Controller.ListDeliveriesAsync());
        Assert.Single(deliveries);

        var dispatched = CanonicalCashierFixture.Value(await fx.Controller.AdvanceDeliveryAsync(new(
            Guid.NewGuid(), deliveryId, createdDelivery.Version, fx.Now, CafeDeliveryStatuses.Dispatched, null)));
        Assert.Equal(CafeDeliveryStatuses.Dispatched, dispatched.Status);

        var delivered = CanonicalCashierFixture.Value(await fx.Controller.AdvanceDeliveryAsync(new(
            Guid.NewGuid(), deliveryId, dispatched.Version, fx.Now, CafeDeliveryStatuses.Delivered, null)));
        Assert.Equal(CafeDeliveryStatuses.Delivered, delivered.Status);

        // Run Request -> Approve -> Commit refund on the completed sale
        Guid saleItemId = Guid.Parse(fx.Scalar($"SELECT id FROM sale_items WHERE sale_id = '{sale.SaleId:D}' LIMIT 1;").ToString()!);

        Guid refundId = Guid.NewGuid();
        var requested = CanonicalCashierFixture.Value(await fx.Controller.RequestRefundAsync(new(
            Guid.NewGuid(), refundId, sale.SaleId, 0, fx.Now, "Customer changed mind",
            [new(Guid.NewGuid(), saleItemId, 1000, CafeRefundStockDispositions.ServiceNoStock, null)])));
        var approved = CanonicalCashierFixture.Value(await fx.Controller.ResolveRefundAsync(new(
            Guid.NewGuid(), refundId, sale.SaleId, requested.Version, fx.Now, true, "Approved")));
        var committed = CanonicalCashierFixture.Value(await fx.Controller.CommitRefundAsync(new(
            Guid.NewGuid(), refundId, sale.SaleId, approved.Version, shift.Id, fx.Now,
            [new(Guid.NewGuid(), CafeRefundSettlementMethods.Cash, 12500)])));
        Assert.Equal(CafeRefundStates.Committed, committed.State);

        var foundRefund = CanonicalCashierFixture.Value(await fx.Controller.FindRefundAsync(refundId));
        Assert.Equal(12500L, foundRefund.Refund.TotalRefundCents);
    }

    private static MobileSignedOfflineLease SignLease(
        ECDsa key,
        string publicKeySpkiBase64,
        Guid deviceId,
        Guid tenantId,
        Guid storeId,
        string productId,
        long epoch,
        DateTimeOffset issued,
        DateTimeOffset expires)
    {
        string payload = $"{deviceId:D}|{tenantId:D}|{storeId:D}|{productId}|{epoch}|{issued:O}|{expires:O}";
        string sig = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));
        return new(deviceId, tenantId, storeId, productId, epoch, issued, expires, sig, publicKeySpkiBase64);
    }
}

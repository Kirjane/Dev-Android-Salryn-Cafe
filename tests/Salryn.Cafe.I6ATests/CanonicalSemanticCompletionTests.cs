using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CanonicalSemanticCompletionTests
{
    private const string OperatorPassword = CanonicalDomainTransactionTests.Password;

    [Fact]
    public void CanonicalSaleCommittedV2_AppliesViaExecuteC5WithDiscountsFeesAndReplaysIdempotently()
    {
        var (target, service, context, token) = CreateCanonicalAuthority();
        SyncOperationEnvelopeV1 envelope = BuildSaleV2Envelope(context, localSequence: 1);

        var push = service.PushOperations(context, new PushOperationsRequest(1, [envelope]), token);
        Assert.Equal(200, push.StatusCode);
        var result = Assert.Single(push.Value!.Results);
        Assert.True(result.Resolution.IsApplied, result.Resolution.ResultCode);
        Assert.Equal("SaleCommittedV2.Applied", result.Resolution.ResultCode);
        Assert.Equal(1L, result.ChangeFeedSequence);

        // Gross subtotal = 1000, line discount = 100, fee = 50 -> total = 950
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(950, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
        Assert.Equal(1000, target.ScalarLong("SELECT SUM(subtotal_cents) FROM sales;"));
        Assert.Equal(100, target.ScalarLong("SELECT SUM(discount_cents) FROM sales;"));
        Assert.Equal(950, target.ScalarLong("SELECT SUM(amount_cents) FROM payments;"));
        Assert.Equal(9000, target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type='SaleCommittedV2AppliedV1';"));

        // Duplicate replay must be idempotent
        var replay = service.PushOperations(context, new PushOperationsRequest(1, [envelope]), token);
        Assert.Equal(200, replay.StatusCode);
        Assert.True(Assert.Single(replay.Value!.Results).IsDuplicateReplay);
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
    }

    [Theory]
    [InlineData("Sales.DiscountApply")]
    [InlineData("Sales.FeeApply")]
    public async Task CanonicalSaleCommittedV2_RejectsRevokedFinancialPermissionWithoutAnyBusinessWrite(string permission)
    {
        var (target, service, context, _) = CreateCanonicalAuthority();
        target.Execute("DELETE FROM role_permissions WHERE permission=$permission;", ("$permission", permission));
        // Re-login deliberately: a fresh reduced-authority token must not substitute for operation-specific permission.
        var login=await service.LoginOperatorAsync(context,new("c4-proof",OperatorPassword));
        Assert.Equal(200,login.StatusCode);
        var envelope=BuildSaleV2Envelope(context,localSequence:1);
        var response=service.PushOperations(context,new(1,[envelope]),login.Value!.OperatorToken);
        Assert.Equal(200,response.StatusCode);
        var result=Assert.Single(response.Value!.Results);
        Assert.Equal(SaleCommittedV1Contract.Rejected,result.Resolution.Status);
        Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY",result.Resolution.ResultCode);
        Assert.Null(result.ChangeFeedSequence);
        foreach(string table in new[]{"sales","payments","stock_movements","accounts_receivable_entries","cafe_sale_financial_decisions","sync_change_feed"})
            Assert.Equal(0L,target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
        Assert.Equal(10000L,target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
    }

    [Fact]
    public void MalformedSupportedDelivery_IsRejectedWithoutDomainOrChangeFeed()
    {
        var (target, service, context, token) = CreateCanonicalAuthority();
        SyncOperationEnvelopeV1 unsupported = SyncProtocolV1.CreateEnvelope(
            deviceId: context.DeviceId,
            storeId: context.StoreId,
            actorUserId: C4ProofIds.ActorUserId,
            localSequence: 1,
            operationType: CafeDeliverySyncContract.DeliveryConfiguredV1,
            schemaVersion: 1,
            aggregateId: Guid.NewGuid(),
            baseVersion: null,
            occurredAtLocal: DateTimeOffset.UtcNow,
            payloadJson: "{\"delivery\":\"synthetic\"}");

        var push = service.PushOperations(context, new PushOperationsRequest(1, [unsupported]), token);
        Assert.Equal(200, push.StatusCode);
        var result = Assert.Single(push.Value!.Results);
        Assert.False(result.Resolution.IsApplied);
        Assert.Equal(SaleCommittedV1Contract.Rejected, result.Resolution.Status);
        Assert.Equal("CafeDeliverySync.PayloadInvalid", result.Resolution.ResultCode);
        Assert.Equal(0, target.ScalarLong("SELECT COUNT(*) FROM cafe_delivery_fulfillments;"));
        Assert.Null(result.ChangeFeedSequence);
        Assert.Equal(0, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
    }

    [Fact]
    public void FreshInitializationInterruption_RecoversCleanlyAndStillRejectsUnclassifiedTransportDb()
    {
        string root = Path.Combine(Path.GetTempPath(), "central-init-recover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string canonicalDbPath = Path.Combine(root, "canonical.db");
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();

        var options = new CentralServerOptions
        {
            DatabasePath = canonicalDbPath,
            EnableCanonicalDomainProcessing = true,
            CanonicalTenantId = tenantId,
            CanonicalStoreId = storeId
        };

        var db = new CentralSyncDatabase(options);

        // Simulate crash after migrations + seeding but before authority binding commits
        Assert.Throws<IOException>(() => db.Initialize(() =>
            throw new IOException("Simulated crash between migration/seeding and authority binding.")));
        Assert.True(File.Exists(canonicalDbPath + ".canonical-init-pending"));

        // Retry with same authority must recover cleanly
        db.Initialize();
        Assert.False(File.Exists(canonicalDbPath + ".canonical-init-pending"));

        // Verify authority is bound and idempotent on subsequent restarts
        db.Initialize();

        // Unclassified transport-only DB must still fail closed when opened in canonical mode
        string transportDbPath = Path.Combine(root, "transport-only.db");
        new CentralSyncDatabase(new CentralServerOptions
        {
            DatabasePath = transportDbPath,
            EnableCanonicalDomainProcessing = false
        }).Initialize();

        var promotedTransportDb = new CentralSyncDatabase(new CentralServerOptions
        {
            DatabasePath = transportDbPath,
            EnableCanonicalDomainProcessing = true,
            CanonicalTenantId = tenantId,
            CanonicalStoreId = storeId
        });
        Assert.ThrowsAny<InvalidOperationException>(() => promotedTransportDb.Initialize());
    }

    [Fact]
    public void CanonicalBootstrapAndMultiDeviceChangeFeedReplay_SynchronizesTwoDevicesAndRecoversAfterEpochBump()
    {
        var (target, service, deviceA, tokenA) = CreateCanonicalAuthority();
        Guid deviceBId = Guid.NewGuid();
        var deviceB = new AuthenticatedDeviceContext(
            deviceBId, deviceA.TenantId, deviceA.StoreId,
            CentralServerOptions.CanonicalProductId, "OfflineAndroidCashier", "Active", "synthetic-b");

        // 1. Device B bootstraps canonical snapshot at baseline 0
        var boot0 = service.GetBootstrap(deviceB, deviceA.StoreId);
        Assert.Equal(200, boot0.StatusCode);
        Assert.NotNull(boot0.Value);
        Assert.Equal(1, boot0.Value!.ServerEpoch);
        Assert.Equal(0, boot0.Value.HeadSequence);
        Assert.True(SyncBootstrapSnapshotProtocolV1.Validate(boot0.Value.Snapshot).IsValid);
        Assert.NotEmpty(boot0.Value.Snapshot.Products);
        Assert.NotEmpty(boot0.Value.Snapshot.Users);

        // 2. Device A pushes canonical SaleCommittedV2
        SyncOperationEnvelopeV1 saleV2 = BuildSaleV2Envelope(deviceA, localSequence: 1);
        var push = service.PushOperations(deviceA, new PushOperationsRequest(1, [saleV2]), tokenA);
        Assert.Equal(200, push.StatusCode);
        Assert.Equal(1, push.Value!.HeadSequence);
        Assert.True(Assert.Single(push.Value.Results).Resolution.IsApplied);

        // 3. Device B pulls change-feed from sequence 0 and receives the applied SaleCommittedV2
        var pull = service.PullChanges(deviceB, deviceA.StoreId, afterSequence: 0, limit: 50, knownEpoch: 1);
        Assert.Equal(200, pull.StatusCode);
        var change = Assert.Single(pull.Value!.Page.Changes);
        Assert.Equal(1L, change.ServerSequence);
        Assert.Equal(AppliedBusinessChangeContract.SaleCommittedV2AppliedV1, change.ChangeType);
        Assert.Equal(saleV2.OperationId, change.SourceOperationId);

        // 4. Admin bumps epoch (simulating lossless epoch/restore recovery)
        var admin = new AdminSessionDto(
            Guid.NewGuid(), deviceA.TenantId, deviceA.StoreId,
            CentralServerOptions.CanonicalProductId, "owner", "Store Owner", "Owner", DateTimeOffset.UtcNow.AddHours(1));
        var bump = service.BumpEpoch(admin, new EpochBumpRequest("Lossless recovery epoch bump"));
        Assert.Equal(200, bump.StatusCode);
        Assert.Equal(2, bump.Value!.NewEpoch);

        // 5. Device B pulling with stale epoch 1 is fenced with 409 ERR_EPOCH_MISMATCH
        var stalePull = service.PullChanges(deviceB, deviceA.StoreId, afterSequence: 0, limit: 50, knownEpoch: 1);
        Assert.Equal(409, stalePull.StatusCode);
        Assert.Equal("EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED", stalePull.Error?.ErrorCode);

        // 6. Device B re-bootstraps at epoch 2 and sees consistent HeadSequence == 1 with updated stock
        var bootAfterBump = service.GetBootstrap(deviceB, deviceA.StoreId);
        Assert.Equal(200, bootAfterBump.StatusCode);
        Assert.Equal(2, bootAfterBump.Value!.ServerEpoch);
        Assert.Equal(1, bootAfterBump.Value.HeadSequence);
        Assert.True(SyncBootstrapSnapshotProtocolV1.Validate(bootAfterBump.Value.Snapshot).IsValid);
        var coffeeProduct = Assert.Single(bootAfterBump.Value.Snapshot.Products, p => p.Sku == "C4-PRODUCT");
        Assert.Equal(9000, coffeeProduct.StockOnHandX1000);

        // 7. Device B pulling with epoch 2 also succeeds losslessly
        var recoveredPull = service.PullChanges(deviceB, deviceA.StoreId, afterSequence: 0, limit: 50, knownEpoch: 2);
        Assert.Equal(200, recoveredPull.StatusCode);
        Assert.Equal(2, recoveredPull.Value!.ServerEpoch);
        Assert.Single(recoveredPull.Value.Page.Changes);
    }

    private static (C4ProofDatabase Target, CentralSyncService Service, AuthenticatedDeviceContext Context, string Token) CreateCanonicalAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "central-semantic-" + Guid.NewGuid().ToString("N"));
        var target = C4ProofDatabase.Create(root);
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", Salryn.Core.Security.SalrynPasswordHasher.HashPassword(OperatorPassword).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));
        Guid tenant = Guid.NewGuid();
        var options = new CentralServerOptions
        {
            DatabasePath = target.Paths.DatabasePath,
            EnableCanonicalDomainProcessing = true,
            CanonicalTenantId = tenant,
            CanonicalStoreId = C4ProofIds.StoreId
        };
        var database = new CentralSyncDatabase(options);
        database.Initialize();
        var context = new AuthenticatedDeviceContext(
            C4ProofIds.DeviceId, tenant, C4ProofIds.StoreId,
            CentralServerOptions.CanonicalProductId, "OfflineAndroidCashier", "Active", "synthetic");
        var service = new CentralSyncService(database, options);
        var login = service.LoginOperatorAsync(context, new OperatorLoginRequest("c4-proof", OperatorPassword))
            .GetAwaiter().GetResult();
        Assert.Equal(200, login.StatusCode);
        return (target, service, context, login.Value!.OperatorToken);
    }

    private static SyncOperationEnvelopeV1 BuildSaleV2Envelope(AuthenticatedDeviceContext context, long localSequence)
    {
        Guid saleId = Guid.NewGuid();
        Guid saleItemId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Gross subtotal = 1000, line discount = 100, fee = 50 -> total = 950
        var saleV1 = new SaleCommittedV1(
            new SaleCommittedV1Sale(
                saleId, "SYN-V2-" + saleId.ToString("N")[..8], now,
                C4ProofIds.ActorUserId, C4ProofIds.CashierSessionId,
                1000, 100, 0, 950,
                950, 0, null, "Paid",
                0, null, "Promo", "Fixed", now),
            [new SaleCommittedV1Item(
                saleItemId, saleId, C4ProofIds.ProductId, "C4 Coffee", null, "Piece",
                1000, 1000, 250, 100, 900)],
            [new SaleCommittedV1Payment(Guid.NewGuid(), saleId, "Cash", 950, now)],
            [new SaleCommittedV1StockMovement(
                Guid.NewGuid(), C4ProofIds.ProductId, SaleCommittedV1Contract.StockMovementSale,
                1000, 250, SaleCommittedV1Contract.StockReferenceSale, saleId, null, C4ProofIds.ActorUserId, now)]);

        var lineDiscounts = new[]
        {
            new SaleCommittedV2LineDiscount(saleItemId, LineDiscountCents: 100, OrderDiscountAllocatedCents: 0, TotalDiscountCents: 100)
        };
        Guid feeId = Guid.NewGuid();
        var fees = new[]
        {
            new SaleCommittedV2Fee(feeId, "Service", "Service Charge", 50, C4ProofIds.ActorUserId, "Synthetic fee", now)
        };

        Guid financialOpId = Guid.NewGuid();
        Guid financialDecisionId = Guid.NewGuid();
        var decision = new CafeSaleFinancialDecision(
            financialDecisionId,
            financialOpId,
            context.StoreId,
            null,
            null,
            null,
            saleId,
            C4ProofIds.CashierSessionId,
            C4ProofIds.ActorUserId,
            1000,
            100,
            0,
            50,
            0,
            950,
            950,
            0,
            950,
            0,
            0,
            "Synthetic C5 promo",
            string.Empty,
            now,
            [new CafeSaleFinancialLineDecision(saleItemId, 100, 0)],
            [new CafeSaleFeeDecision(feeId, "Service", "Service Charge", 50, C4ProofIds.ActorUserId, "Synthetic fee", now)]);
        string decisionHash = Salryn.Core.Services.CafeSaleFinancialCalculator.ComputeContentHash(decision);
        var payload = new SaleCommittedV2(
            SaleEvidence: saleV1,
            FinancialOperationId: financialOpId,
            FinancialDecisionId: financialDecisionId,
            OrderId: null,
            OrderRevisionId: null,
            ExpectedOrderVersion: null,
            FinancialDecisionCreatedAtUtc: now,
            FinancialDecisionHash: decisionHash,
            DiscountReason: "Synthetic C5 promo",
            FeeTotalCents: 50,
            LineDiscounts: lineDiscounts,
            Fees: fees);

        string json = SaleCommittedV2CanonicalJson.Serialize(payload);
        return SyncProtocolV1.CreateEnvelope(
            deviceId: context.DeviceId,
            storeId: context.StoreId,
            actorUserId: C4ProofIds.ActorUserId,
            localSequence: localSequence,
            operationType: SaleCommittedV2Contract.OperationType,
            schemaVersion: SaleCommittedV2Contract.SchemaVersion,
            aggregateId: saleId,
            baseVersion: null,
            occurredAtLocal: now,
            payloadJson: json);
    }
}

using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CanonicalDomainTransactionTests
{
    [Fact]
    public async Task CanonicalOrderCommit_ReplaysOneDomainTransactionAndAppliedFeedAfterRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "central-canonical-" + Guid.NewGuid().ToString("N"));
        var source = C4ProofDatabase.Create(Path.Combine(root,"source"));
        var target = C4ProofDatabase.Create(Path.Combine(root,"central"));
        Guid operation = Guid.NewGuid(), order = Guid.NewGuid(), tenant = Guid.NewGuid();
        var saved = source.Orders.SaveRevision(new CafeOrderRevisionMutation(operation, C4ProofIds.DeviceId,
            order, C4ProofIds.StoreId, 0, C4ProofIds.ActorUserId, DateTimeOffset.UtcNow, "DineIn", "T1",
            [new(Guid.NewGuid(), C4ProofIds.ProductId, null, [], 1000, null, 0)]));
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.ErrorDetail.Message : null);
        var envelope = new SqliteSyncOutboxStore(source.Factory).Find(operation)!.Envelope;
        var options = new CentralServerOptions { DatabasePath = target.Paths.DatabasePath, EnableCanonicalDomainProcessing = true, CanonicalTenantId = tenant, CanonicalStoreId = C4ProofIds.StoreId };
        var database = new CentralSyncDatabase(options);
        database.Initialize();
        var context = new AuthenticatedDeviceContext(C4ProofIds.DeviceId, tenant, C4ProofIds.StoreId,
            "Salryn.Cafe", "OfflineAndroidCashier", "Active", "synthetic");
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;", ("$hash", Salryn.Core.Security.SalrynPasswordHasher.HashPassword(Password).Value), ("$id", C4ProofIds.ActorUserId.ToString("D")));
        var service = new CentralSyncService(database, options);
        var login = await service.LoginOperatorAsync(context,new("c4-proof", Password));
        Assert.Equal(200,login.StatusCode);
        string token=login.Value!.OperatorToken;
        var response = service.PushOperations(context, new(1,[envelope]), token);
        Assert.Equal(200, response.StatusCode);
        Assert.True(Assert.Single(response.Value!.Results).Resolution.IsApplied);
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM cafe_orders;"));
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM cafe_order_operations;"));
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        var replay = new CentralSyncService(new CentralSyncDatabase(options), options).PushOperations(context, new(1,[envelope]), token);
        Assert.True(Assert.Single(replay.Value!.Results).IsDuplicateReplay);
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM cafe_orders;"));
        Assert.Equal(1, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
    }
    internal const string Password="Canonical-Operator-Test!2026";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalCashSale_CommitsMoneyStockInboxAndFeedAtomically(bool failFeed)
    {
        var (target,service,context,token)=Authority();
        var envelope=SaleEnvelope(context);
        if(failFeed)
        {
            target.Execute("CREATE TRIGGER synthetic_feed_fault BEFORE INSERT ON sync_change_feed BEGIN SELECT RAISE(ABORT,'synthetic feed fault'); END;");
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(()=>service.PushOperations(context,new(1,[envelope]),token));
            Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM stock_movements;"));
            Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_operations;"));
            target.Execute("DROP TRIGGER synthetic_feed_fault;");
        }
        var response=service.PushOperations(context,new(1,[envelope]),token);
        Assert.Equal(200,response.StatusCode);
        var result=Assert.Single(response.Value!.Results);
        Assert.True(result.Resolution.IsApplied,result.Resolution.ResultCode);
        Assert.Equal(500,target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
        Assert.Equal(500,target.ScalarLong("SELECT SUM(amount_cents) FROM payments;"));
        Assert.Equal(9000,target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
        Assert.Equal(1,target.ScalarLong("SELECT COUNT(*) FROM stock_movements;"));
        Assert.Equal(1,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.True(Assert.Single(service.PushOperations(context,new(1,[envelope]),token).Value!.Results).IsDuplicateReplay);
        Assert.Equal(1,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-device")]
    [InlineData("foreign-actor")]
    [InlineData("revoked")]
    [InlineData("permissions")]
    [InlineData("expired")]
    public void CanonicalMutationRequiresIndependentCurrentOperatorProof(string scenario)
    {
        var (target,service,context,token)=Authority();
        var envelope=SaleEnvelope(context);
        if(scenario=="missing") token="";
        if(scenario=="foreign-device") { context=context with{DeviceId=Guid.NewGuid()}; envelope=SaleEnvelope(context); }
        if(scenario=="foreign-actor") envelope=SyncProtocolV1.CreateEnvelope(context.DeviceId,context.StoreId,Guid.NewGuid(),1,
            envelope.OperationType,1,envelope.AggregateId,null,DateTimeOffset.UtcNow,envelope.PayloadJson);
        if(scenario=="revoked") service.LogoutOperator(context,token);
        if(scenario=="permissions") target.Execute("DELETE FROM role_permissions WHERE permission='Sales.Create';");
        if(scenario=="expired") target.Execute("UPDATE sync_operator_sessions SET expires_at_utc='2000-01-01T00:00:00+00:00';");
        var response=service.PushOperations(context,new(1,[envelope]),token);
        Assert.Equal(403,response.StatusCode);
        Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_operations;"));
        Assert.Equal(0,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
    }

    private static (C4ProofDatabase Target, CentralSyncService Service, AuthenticatedDeviceContext Context,string Token) Authority()
    {
        string root=Path.Combine(Path.GetTempPath(),"central-financial-"+Guid.NewGuid().ToString("N"));
        var target=C4ProofDatabase.Create(root);
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",Salryn.Core.Security.SalrynPasswordHasher.HashPassword(Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        Guid tenant=Guid.NewGuid();
        var options=new CentralServerOptions{DatabasePath=target.Paths.DatabasePath,EnableCanonicalDomainProcessing=true,CanonicalTenantId=tenant,CanonicalStoreId=C4ProofIds.StoreId};
        var database=new CentralSyncDatabase(options); database.Initialize();
        var context=new AuthenticatedDeviceContext(C4ProofIds.DeviceId,tenant,C4ProofIds.StoreId,"Salryn.Cafe","OfflineAndroidCashier","Active","synthetic");
        var service=new CentralSyncService(database,options);
        var login=service.LoginOperatorAsync(context,new("c4-proof",Password)).GetAwaiter().GetResult();
        Assert.Equal(200,login.StatusCode);
        return(target,service,context,login.Value!.OperatorToken);
    }

    internal static SyncOperationEnvelopeV1 SaleEnvelope(AuthenticatedDeviceContext context)
    {
        Guid sale=Guid.NewGuid(); var now=DateTimeOffset.UtcNow;
        var payload=new SaleCommittedV1(new(sale,"SYN-"+sale.ToString("N"),now,C4ProofIds.ActorUserId,C4ProofIds.CashierSessionId,
            500,0,0,500,500,0,null,"Paid",0,null,null,null,now),
            [new(Guid.NewGuid(),sale,C4ProofIds.ProductId,"C4 Coffee",null,"Piece",1000,500,250,0,500)],
            [new(Guid.NewGuid(),sale,"Cash",500,now)],
            [new(Guid.NewGuid(),C4ProofIds.ProductId,SaleCommittedV1Contract.StockMovementSale,1000,250,SaleCommittedV1Contract.StockReferenceSale,sale,null,C4ProofIds.ActorUserId,now)]);
        string json=SaleCommittedV1CanonicalJson.Serialize(payload);
        return SyncProtocolV1.CreateEnvelope(context.DeviceId,context.StoreId,C4ProofIds.ActorUserId,1,
            SaleCommittedV1Contract.OperationType,1,sale,null,now,json);
    }

}


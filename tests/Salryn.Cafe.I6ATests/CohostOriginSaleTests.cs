using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Infrastructure.Data.Services;
using Salryn.Core.Security;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;
public sealed class CohostOriginSaleTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExistingLocalSale_RequiresExactFactsBeforeFeedAdmission(bool tampered)
    {
        var target=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),"cohost-origin-"+Guid.NewGuid().ToString("N")));
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        Guid tenant=Guid.NewGuid();var options=new CentralServerOptions{DatabasePath=target.Paths.DatabasePath,EnableCanonicalDomainProcessing=true,CanonicalTenantId=tenant,CanonicalStoreId=C4ProofIds.StoreId};
        var database=new CentralSyncDatabase(options);database.Initialize();var service=new CentralSyncService(database,options);
        var context=new AuthenticatedDeviceContext(C4ProofIds.DeviceId,tenant,C4ProofIds.StoreId,"Salryn.Cafe","OfflineAndroidCashier","Active","synthetic");
        var baseEnvelope=CanonicalDomainTransactionTests.SaleEnvelope(context);
        var payload=SaleCommittedV1CanonicalJson.Parse(baseEnvelope.PayloadJson).Payload!;
        var package=SaleCommittedV1Mapper.ToCore(payload);Assert.True(package.IsSuccess);
        var outbox=new SqliteSyncOutboxStore(target.Factory);
        var local=new LocalSaleCommittedV1Coordinator(new SqliteSharedTransactionBoundary(target.Factory),new SharedSqliteCompletedSaleWriteParticipant(),outbox);
        var committed=await local.CommitAsync(new(baseEnvelope.OperationId,context.DeviceId,context.StoreId,package.Value,baseEnvelope.OccurredAtLocal));
        Assert.True(committed.IsSuccess,committed.IsFailure?committed.ErrorDetail.Message:null);
        var envelope=committed.Value.Envelope;
        if(tampered) target.Execute("UPDATE products SET name='Changed catalog label' WHERE sku='C4-PRODUCT';"); // Catalog is not historical evidence.
        if(tampered) target.Execute("UPDATE sale_items SET product_name_snapshot='Tampered receipt item';");
        var login=await service.LoginOperatorAsync(context,new("c4-proof",CanonicalDomainTransactionTests.Password));Assert.Equal(200,login.StatusCode);
        var result=Assert.Single(service.PushOperations(context,new(1,[envelope]),login.Value!.OperatorToken).Value!.Results);
        Assert.Equal(!tampered,result.Resolution.IsApplied);
        Assert.Equal(tampered?0L:1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(500L,target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
        Assert.Equal(9000L,target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
        if(!tampered)
        {
            var replay=service.PushOperations(context,new(1,[envelope]),login.Value.OperatorToken);
            Assert.True(Assert.Single(replay.Value!.Results).IsDuplicateReplay);
            outbox.RecordCentralResult(result.Resolution);
            Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        }
    }
}

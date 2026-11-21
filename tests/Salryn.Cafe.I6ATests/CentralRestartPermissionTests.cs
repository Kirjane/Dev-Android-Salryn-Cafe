using Salryn.Core.Security;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CentralRestartPermissionTests
{
    [Theory]
    [InlineData("Sales.Create")]
    [InlineData("Receivables.CreateCreditSale")]
    public async Task ExistingCanonicalAuthority_RestartDoesNotReseedRevokedFinancialPermission(string permission)
    {
        var target=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),"central-revoked-restart-"+Guid.NewGuid().ToString("N")));
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        Guid tenant=Guid.NewGuid();
        var options=new CentralServerOptions {DatabasePath=target.Paths.DatabasePath,EnableCanonicalDomainProcessing=true,CanonicalTenantId=tenant,CanonicalStoreId=C4ProofIds.StoreId};
        var database=new CentralSyncDatabase(options);database.Initialize();
        target.Execute("DELETE FROM role_permissions WHERE permission=$permission;",("$permission",permission));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM role_permissions WHERE permission=$permission;",("$permission",permission)));
        var restartedDatabase=new CentralSyncDatabase(options);restartedDatabase.Initialize();
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM role_permissions WHERE permission=$permission;",("$permission",permission)));
        var restartedService=new CentralSyncService(restartedDatabase,options);
        var device=new AuthenticatedDeviceContext(C4ProofIds.DeviceId,tenant,C4ProofIds.StoreId,"Salryn.Cafe","OfflineAndroidCashier","Active","synthetic");
        var login=await restartedService.LoginOperatorAsync(device,new("c4-proof",CanonicalDomainTransactionTests.Password));
        Assert.Equal(200,login.StatusCode);Assert.DoesNotContain(permission,login.Value!.Permissions);
        var envelope=CanonicalDomainTransactionTests.SaleEnvelope(device);
        if(permission=="Receivables.CreateCreditSale")
        {
            Guid customer=Guid.NewGuid();
            target.Execute("INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Synthetic revoked AR',1,$actor,$at,$at);",("$id",customer.ToString("D")),("$actor",C4ProofIds.ActorUserId.ToString("D")),("$at",DateTimeOffset.UtcNow.ToString("O")));
            var cash=SaleCommittedV1CanonicalJson.Parse(envelope.PayloadJson).Payload!;
            var credit=cash with {Sale=cash.Sale with {CustomerId=customer,PaymentStatus=SaleCommittedV1Contract.PaymentStatusCredit,BalanceDueCents=cash.Sale.TotalCents},Payments=cash.Payments.Select(payment=>payment with {Method="AccountsReceivable"}).ToArray()};
            envelope=SyncProtocolV1.CreateEnvelope(device.DeviceId,device.StoreId,C4ProofIds.ActorUserId,envelope.LocalSequence,SaleCommittedV1Contract.OperationType,1,envelope.AggregateId,null,envelope.OccurredAtLocal,SaleCommittedV1CanonicalJson.Serialize(credit),envelope.OperationId);
        }
        var response=restartedService.PushOperations(device,new(1,[envelope]),login.Value.OperatorToken);
        Assert.Equal(200,response.StatusCode);
        var result=Assert.Single(response.Value!.Results);
        Assert.Equal(SaleCommittedV1Contract.Rejected,result.Resolution.Status);
        Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY",result.Resolution.ResultCode);Assert.Null(result.ChangeFeedSequence);
        foreach(string table in new[]{"sales","payments","accounts_receivable_entries","stock_movements","sync_change_feed"})
            Assert.Equal(0L,target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
    }
}

using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class ShiftSyncCentralTests
{
    [Fact]
    public async Task OverlappingCrossDeviceOpens_PreserveOneDrawerPerOperator()
    {
        var target=Fixture("shift-central-race");
        using var host=new CentralSyncTestHost(target.Paths.DatabasePath,true,C4ProofIds.StoreId);
        using var admin=host.CreateHttpsClient();string adminToken=await host.LoginAsAdminAsync(admin);
        using var a=await host.EnrollApproveAndAuthenticateDeviceAsync(admin,adminToken,"Shift A");
        using var b=await host.EnrollApproveAndAuthenticateDeviceAsync(admin,adminToken,"Shift B");
        await Login(a);await Login(b);
        var sourceA=Fixture("shift-source-a");var sourceB=Fixture("shift-source-b");
        var opA=Open(sourceA,a.DeviceId);var opB=Open(sourceB,b.DeviceId);
        using var gate=new SemaphoreSlim(0,2);
        async Task<PushOperationResultDto> Push(EnrolledDeviceClient device,SyncOperationEnvelopeV1 envelope)
        {await gate.WaitAsync();using var response=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[envelope]));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Assert.Single((await response.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);}
        var first=Push(a,opA);var second=Push(b,opB);gate.Release(2);
        var results=await Task.WhenAll(first,second);
        Assert.Single(results.Where(x=>x.Resolution.IsApplied));
        Assert.Equal("CafeShift.OpenSessionExists",Assert.Single(results.Where(x=>!x.Resolution.IsApplied)).Resolution.ResultCode);
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=1;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM cash_movements;"));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM cash_drawer_events;"));
        int winningIndex=results[0].Resolution.IsApplied?0:1;
        var winningSource=winningIndex==0?sourceA:sourceB;var winningDevice=winningIndex==0?a:b;
        var winningOpen=winningIndex==0?opA:opB;var losingDevice=winningIndex==0?b:a;var losingOpen=winningIndex==0?opB:opA;
        Guid closeId=Guid.NewGuid();
        Assert.True(new SqliteCafeShiftStore(winningSource.Factory).Close(closeId,winningDevice.DeviceId,C4ProofIds.StoreId,
            C4ProofIds.ActorUserId,winningOpen.AggregateId,10000,DateTimeOffset.UtcNow.AddMinutes(1)).IsSuccess);
        var closeEnvelope=new SqliteSyncOutboxStore(winningSource.Factory).Find(closeId)!.Envelope;
        using var closed=await winningDevice.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[closeEnvelope]));
        Assert.True(Assert.Single((await closed.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results).Resolution.IsApplied);
        using var late=await losingDevice.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[losingOpen]));
        var lateResult=Assert.Single((await late.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);
        Assert.True(lateResult.Resolution.IsNeedsReview);Assert.Equal("CafeShift.OpenHistoryConflict",lateResult.Resolution.ResultCode);
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions;"));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=1;"));
    }

    [Fact]
    public async Task OutOfOrderClose_RecoversAfterOpeningThenProjectsOtherClientAndOriginEchoExactlyOnce()
    {
        var target=Fixture("shift-central");Guid tenant=Guid.NewGuid();
        var options=new CentralServerOptions{DatabasePath=target.Paths.DatabasePath,EnableCanonicalDomainProcessing=true,CanonicalTenantId=tenant,CanonicalStoreId=C4ProofIds.StoreId};
        var database=new CentralSyncDatabase(options);database.Initialize();var service=new CentralSyncService(database,options);
        var context=new AuthenticatedDeviceContext(C4ProofIds.DeviceId,tenant,C4ProofIds.StoreId,"Salryn.Cafe","OfflineAndroidCashier","Active","synthetic");
        string token=(await service.LoginOperatorAsync(context,new("c4-proof",CanonicalDomainTransactionTests.Password))).Value!.OperatorToken;
        var baseline=service.GetBootstrap(context,C4ProofIds.StoreId).Value!.Snapshot;
        var source=Fixture("shift-source");var opened=Open(source,context.DeviceId);
        var store=new SqliteCafeShiftStore(source.Factory);Guid closeId=Guid.NewGuid();
        var close=store.Close(closeId,context.DeviceId,C4ProofIds.StoreId,C4ProofIds.ActorUserId,opened.AggregateId,10000,opened.OccurredAtLocal.AddMinutes(1));
        Assert.True(close.IsSuccess,close.IsFailure?close.ErrorDetail.Message:null);
        var closed=new SqliteSyncOutboxStore(source.Factory).Find(closeId)!.Envelope;
        var early=Assert.Single(service.PushOperations(context,new(1,[closed]),token).Value!.Results);
        Assert.True(early.Resolution.IsNeedsReview);Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions;"));
        Assert.True(Assert.Single(service.PushOperations(context,new(1,[opened]),token).Value!.Results).Resolution.IsApplied);
        Assert.True(Assert.Single(service.PushOperations(context,new(1,[closed]),token).Value!.Results).Resolution.IsApplied);
        Assert.True(Assert.Single(service.PushOperations(context,new(1,[closed]),token).Value!.Results).IsDuplicateReplay);
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=2;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cash_drawer_reconciliations;"));
        Assert.Equal(2L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_inbox_status_transitions;"));
        var second=Fixture("shift-other-client");
        foreach(var client in new[]{source,second})
        {
            var pull=new SqliteSyncPullStore(client.Factory);
            Assert.True(pull.InitializeRemoteState(C4ProofIds.StoreId,baseline.CentralInstanceId).IsSuccess);
            var page=service.PullChanges(context,C4ProofIds.StoreId,0,50,1).Value!.Page;
            Assert.True(pull.AcceptPage(C4ProofIds.StoreId,page).IsSuccess);
            var projector=new SqliteSyncProjectionService(client.Factory);
            Guid localDevice=ReferenceEquals(client,source)?context.DeviceId:Guid.NewGuid();
            for(int i=0;i<2;i++) {var projected=await projector.ProjectNextAsync(C4ProofIds.StoreId,localDevice);Assert.True(projected.IsSuccess,projected.Message);}
            Assert.Equal(2L,pull.ReadRemoteState(C4ProofIds.StoreId)!.LastAppliedServerSequence);
            Assert.Equal(1L,client.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=2;"));
            Assert.Equal(1L,client.ScalarLong("SELECT COUNT(*) FROM cash_drawer_reconciliations;"));
            Assert.Equal(0L,client.ScalarLong("SELECT COUNT(*) FROM cash_movements;"));
        }
    }

    private static C4ProofDatabase Fixture(string prefix)
    {
        var db=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),prefix+Guid.NewGuid().ToString("N")));
        db.Execute("DELETE FROM cashier_sessions;");
        db.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        return db;
    }
    private static SyncOperationEnvelopeV1 Open(C4ProofDatabase source,Guid device)
    {
        Guid operation=Guid.NewGuid();var result=new SqliteCafeShiftStore(source.Factory).Open(operation,device,C4ProofIds.StoreId,C4ProofIds.ActorUserId,10000,DateTimeOffset.UtcNow);
        Assert.True(result.IsSuccess,result.IsFailure?result.ErrorDetail.Message:null);return new SqliteSyncOutboxStore(source.Factory).Find(operation)!.Envelope;
    }
    private static async Task Login(EnrolledDeviceClient device)
    {
        var response=await device.Http.PostAsJsonAsync("/api/sync/v1/operators/login",new OperatorLoginRequest("c4-proof",CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var login=(await response.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader,login.OperatorToken);
    }
}

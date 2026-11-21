using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Mobile.Foundation.P0;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class WindowsCohostPairingTests
{
    [Fact]
    public async Task ActualCentralCohost_EnrollApproveBindShiftSaleReceiptsAndReplay_DoNotDuplicateCanonicalFacts()
    {
        var target=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),"windows-cohost-paired-"+Guid.NewGuid().ToString("N")));
        target.Execute("DELETE FROM cashier_sessions;");
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        using var central=new CentralSyncTestHost(target.Paths.DatabasePath,true,C4ProofIds.StoreId);
        using var admin=central.CreateHttpsClient();string token=await central.LoginAsAdminAsync(admin);
        using var inviteRequest=new HttpRequestMessage(HttpMethod.Post,"/api/sync/v1/admin/devices/invites");
        inviteRequest.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);inviteRequest.Content=JsonContent.Create(new CreateInviteRequest("OwnerAdminClient"));
        using var invited=await admin.SendAsync(inviteRequest);invited.EnsureSuccessStatusCode();
        var invite=(await invited.Content.ReadFromJsonAsync<CreateInviteResponse>())!;
        string pin=target.ScalarString("SELECT lease_signing_public_key_spki FROM sync_server_state;")!;
        var secrets=new MemorySecrets();
        // Actual ASP.NET Core Central transport in process; HTTPS scheme enforced, no real TLS socket/hardware claim.
        var connector=new CafeCentralConnection(SalrynLocalComposition.Create(target.Paths),secrets,
            ()=>central.Factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost:5443")}));
        Guid device=await connector.EnrollAsync(new("https://localhost:5443",central.TenantId,central.StoreId,pin),invite.JoinCode,"Windows QA Cohost");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password));
        using var approval=new HttpRequestMessage(HttpMethod.Post,$"/api/sync/v1/admin/devices/{device:D}/approve");approval.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var approved=await admin.SendAsync(approval);approved.EnsureSuccessStatusCode();
        var connected=await connector.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        Assert.Equal(0,connected.Pending);Assert.Equal(0,connected.Applied);
        Assert.Equal(device.ToString("D"),target.ScalarString("SELECT terminal_id FROM deployment_identity;"));
        var shift=new SqliteCafeShiftStore(target.Factory);Guid openId=Guid.NewGuid();DateTimeOffset at=DateTimeOffset.UtcNow;
        var opened=shift.Open(openId,device,central.StoreId,C4ProofIds.ActorUserId,0,at);Assert.True(opened.IsSuccess,opened.IsFailure?opened.ErrorDetail.Message:null);
        var context=new AuthenticatedDeviceContext(device,central.TenantId,central.StoreId,"Salryn.Cafe","OwnerAdminClient","Active","synthetic");
        var original=CanonicalDomainTransactionTests.SaleEnvelope(context);var evidence=SaleCommittedV1CanonicalJson.Parse(original.PayloadJson).Payload!;
        evidence=evidence with{Sale=evidence.Sale with{CashierSessionId=opened.Value}};
        var package=SaleCommittedV1Mapper.ToCore(evidence);Assert.True(package.IsSuccess);
        var outbox=new SqliteSyncOutboxStore(target.Factory);
        var coordinator=new LocalSaleCommittedV1Coordinator(new SqliteSharedTransactionBoundary(target.Factory),new SharedSqliteCompletedSaleWriteParticipant(),outbox);
        var sale=await coordinator.CommitAsync(new(original.OperationId,device,central.StoreId,package.Value,original.OccurredAtLocal));
        Assert.True(sale.IsSuccess,sale.IsFailure?sale.ErrorDetail.Message:null);
        var synced=await connector.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        // One shift-open plus the committed sale and its separate recipe-ingredient consumption envelope.
        Assert.Equal(3,synced.Applied);Assert.Equal(0,synced.Pending);
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;",("$type",AppliedBusinessChangeContract.CafeShiftOpenedAppliedV1)));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;",("$type",AppliedBusinessChangeContract.SaleCommittedV1AppliedV1)));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;",("$type",AppliedBusinessChangeContract.IngredientConsumptionCommittedV1AppliedV1)));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sales;"));Assert.Equal(500L,target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions;"));Assert.Equal(3L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(9000L,target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
        Assert.True(shift.Close(Guid.NewGuid(),device,central.StoreId,C4ProofIds.ActorUserId,opened.Value,500,DateTimeOffset.UtcNow.AddSeconds(1)).IsSuccess);
        var closed=await connector.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        Assert.Equal(1,closed.Applied);Assert.Equal(0,closed.Pending);
        var replay=await connector.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        Assert.Equal(0,replay.Applied);Assert.Equal(0,replay.Pending);
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sales;"));Assert.Equal(4L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(4L,target.ScalarLong("SELECT COUNT(DISTINCT source_operation_id) FROM sync_change_feed;"));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;",("$type",AppliedBusinessChangeContract.CafeShiftClosedAppliedV1)));
        Assert.Equal(1L,target.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=2 AND counted_cash_cents=500;"));
    }
    [Fact]
    public async Task CopiedCanonicalDatabase_SameStaticInstanceEpochAndScope_RejectsEnrollmentAndCleansProbe()
    {
        var target=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),"windows-cohost-copy-origin-"+Guid.NewGuid().ToString("N")));
        target.Execute("DELETE FROM cashier_sessions;");
        using var actual=new CentralSyncTestHost(target.Paths.DatabasePath,true,C4ProofIds.StoreId);
        using(var client=actual.CreateHttpsClient())
        {
            using var health=await client.GetAsync("/api/sync/v1/health");health.EnsureSuccessStatusCode();
        }
        string originalInstance=target.ScalarString("SELECT central_instance_id FROM sync_server_state;")!;
        long originalEpoch=target.ScalarLong("SELECT server_epoch FROM sync_server_state;");
        string originalTerminal=target.ScalarString("SELECT terminal_id FROM deployment_identity;")!;
        string copiedPath=CopyCanonicalDatabase(target);
        using var copied=new CentralSyncTestHost(copiedPath,true,C4ProofIds.StoreId,actual.TenantId);
        using var copiedHttp=copied.CreateHttpsClient();
        var copiedHealth=(await copiedHttp.GetFromJsonAsync<HealthResponseDto>("/api/sync/v1/health"))!;
        Assert.Equal(originalInstance,copiedHealth.CentralInstanceId.ToString("D"));
        Assert.Equal(originalEpoch,copiedHealth.ServerEpoch);
        var secrets=new MemorySecrets();
        var connector=new CafeCentralConnection(SalrynLocalComposition.Create(target.Paths),secrets,
            ()=>copied.Factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost:5443")}));
        string pin=target.ScalarString("SELECT lease_signing_public_key_spki FROM sync_server_state;")!;
        var config=new CafeCentralConfiguration("https://localhost:5443",actual.TenantId,C4ProofIds.StoreId,pin);
        var denied=await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.EnrollAsync(config,"unused-invite","Copied DB QA"));
        Assert.Contains("exact local canonical SQLite file",denied.Message);
        Assert.Empty(secrets.Values);
        Assert.Equal(originalTerminal,target.ScalarString("SELECT terminal_id FROM deployment_identity;"));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM app_metadata WHERE key LIKE 'salryn.cafe.cohost.probe.%';"));
    }

    [Fact]
    public async Task CopiedCanonicalDatabase_AfterValidPairing_RejectsReconnectBeforeFinancialSync()
    {
        var target=C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(),"windows-cohost-copy-paired-"+Guid.NewGuid().ToString("N")));
        target.Execute("DELETE FROM cashier_sessions;");
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        using var actual=new CentralSyncTestHost(target.Paths.DatabasePath,true,C4ProofIds.StoreId);
        using var admin=actual.CreateHttpsClient();string token=await actual.LoginAsAdminAsync(admin);
        using var inviteRequest=new HttpRequestMessage(HttpMethod.Post,"/api/sync/v1/admin/devices/invites");
        inviteRequest.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        inviteRequest.Content=JsonContent.Create(new CreateInviteRequest("OwnerAdminClient"));
        using var invited=await admin.SendAsync(inviteRequest);invited.EnsureSuccessStatusCode();
        var invite=(await invited.Content.ReadFromJsonAsync<CreateInviteResponse>())!;
        string pin=target.ScalarString("SELECT lease_signing_public_key_spki FROM sync_server_state;")!;
        var secrets=new MemorySecrets();
        var composition=SalrynLocalComposition.Create(target.Paths);
        var paired=new CafeCentralConnection(composition,secrets,
            ()=>actual.Factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost:5443")}));
        Guid device=await paired.EnrollAsync(new("https://localhost:5443",actual.TenantId,actual.StoreId,pin),invite.JoinCode,"Windows Copied DB QA");
        using var approval=new HttpRequestMessage(HttpMethod.Post,$"/api/sync/v1/admin/devices/{device:D}/approve");
        approval.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var approved=await admin.SendAsync(approval);approved.EnsureSuccessStatusCode();
        var first=await paired.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        Assert.Equal(0,first.Pending);
        Assert.Equal(device.ToString("D"),target.ScalarString("SELECT terminal_id FROM deployment_identity;"));

        string copiedPath=CopyCanonicalDatabase(target);
        using var copied=new CentralSyncTestHost(copiedPath,true,C4ProofIds.StoreId,actual.TenantId);
        using var copiedHttp=copied.CreateHttpsClient();
        var copiedHealth=(await copiedHttp.GetFromJsonAsync<HealthResponseDto>("/api/sync/v1/health"))!;
        Assert.Equal(target.ScalarString("SELECT central_instance_id FROM sync_server_state;"),copiedHealth.CentralInstanceId.ToString("D"));
        Assert.Equal(target.ScalarLong("SELECT server_epoch FROM sync_server_state;"),copiedHealth.ServerEpoch);
        var impersonated=new CafeCentralConnection(composition,secrets,
            ()=>copied.Factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost:5443")}));
        var denied=await Assert.ThrowsAsync<InvalidOperationException>(()=>
            impersonated.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password));
        Assert.Contains("exact local canonical SQLite file",denied.Message);
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM app_metadata WHERE key LIKE 'salryn.cafe.cohost.probe.%';"));
        Assert.Equal(device.ToString("D"),target.ScalarString("SELECT terminal_id FROM deployment_identity;"));
        Assert.Equal(0L,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        var recovered=await paired.ConnectAsync(C4ProofIds.ActorUserId,"c4-proof",CanonicalDomainTransactionTests.Password);
        Assert.Equal(0,recovered.Applied);
        Assert.Equal(0,recovered.Pending);
    }

    private static string CopyCanonicalDatabase(C4ProofDatabase original)
    {
        string path=Path.Combine(Path.GetTempPath(),"windows-cohost-copied-"+Guid.NewGuid().ToString("N"),"copy.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var source=original.Factory.CreateConnection();source.Open();
        using var destination=new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=path,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        return path;
    }

    private sealed class MemorySecrets:IDeviceSecretStore
    {
        private readonly Dictionary<string,byte[]> values=[];
        public Task<byte[]?> ReadAsync(string key,CancellationToken ct=default)=>Task.FromResult(values.TryGetValue(key,out var value)?value.ToArray():null);
        public Task WriteAsync(string key,byte[] value,CancellationToken ct=default){values[key]=value.ToArray();return Task.CompletedTask;}
    }
}

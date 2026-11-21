using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Central.Server;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class ExtendedCafeDomainHttpTests
{
    [Theory]
    [InlineData("delivery", true)] [InlineData("delivery", false)]
    [InlineData("booking", true)] [InlineData("booking", false)]
    [InlineData("attendance", true)] [InlineData("attendance", false)]
    public async Task ExistingCanonicalCafeParticipants_ApplyOrDenyOverRealHttp_AndReplayExactlyOnce(string family, bool authorized)
    {
        string root=Path.Combine(Path.GetTempPath(),"central-extended-"+Guid.NewGuid().ToString("N"));
        var source=C4ProofDatabase.Create(Path.Combine(root,"source"));
        var target=C4ProofDatabase.Create(Path.Combine(root,"central"));
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        Guid customer=Guid.NewGuid();
        foreach(var db in new[]{source,target}) db.Execute("INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Synthetic Cross-System Customer',1,$actor,$at,$at);",("$id",customer.ToString("D")),("$actor",C4ProofIds.ActorUserId.ToString("D")),("$at",DateTimeOffset.UtcNow.ToString("O")));
        using var host=new CentralSyncTestHost(target.Paths.DatabasePath,canonical:true,canonicalStore:C4ProofIds.StoreId);
        using var admin=host.CreateHttpsClient(); string adminToken=await host.LoginAsAdminAsync(admin);
        using var device=await host.EnrollApproveAndAuthenticateDeviceAsync(admin,adminToken,"Cross-system "+family);
        Guid operation=Guid.NewGuid(); DateTimeOffset now=DateTimeOffset.UtcNow;
        string table,permission;
        if(family=="delivery")
        {
            Guid order=Guid.NewGuid();
            var saved=source.Orders.SaveRevision(new(Guid.NewGuid(),device.DeviceId,order,host.StoreId,0,C4ProofIds.ActorUserId,now,"Takeout","Synthetic delivery",[new(Guid.NewGuid(),C4ProofIds.ProductId,null,[],1000,null,0)]));
            Assert.True(saved.IsSuccess,saved.IsFailure?saved.ErrorDetail.Message:null);
            var created=new SqliteCafeDeliveryService(source.Factory).Create(new(operation,device.DeviceId,Guid.NewGuid(),order,host.StoreId,C4ProofIds.ActorUserId,now,"Synthetic recipient",null,"Synthetic address",null,null,0,null));
            Assert.True(created.IsSuccess,created.IsFailure?created.ErrorDetail.Message:null);
            table="cafe_deliveries";permission="Delivery.Manage";
        }
        else if(family=="booking")
        {
            var configuration=new CafeBookingConfigurationMutation(customer,"Event","Synthetic Cafe Event",null,now.AddDays(1),null,"Synthetic venue",null,null,null,1,null,C4ProofIds.MenuRevisionId,0,null,null,"Takeout",[new(Guid.NewGuid(),C4ProofIds.ProductId,null,[],1000,null,0)]);
            var created=new SqliteCafeBookingService(source.Factory).Create(new(operation,Guid.NewGuid(),Guid.NewGuid(),device.DeviceId,host.StoreId,C4ProofIds.ActorUserId,now,configuration));
            Assert.True(created.IsSuccess,created.IsFailure?created.ErrorDetail.Message:null);
            table="cafe_bookings";permission="Booking.Manage";
        }
        else
        {
            var created=new SqliteCafeAttendanceIncidentService(source.Factory).RecordAttendance(new(operation,Guid.NewGuid(),host.StoreId,C4ProofIds.ActorUserId,C4ProofIds.ActorUserId,device.DeviceId,"ClockIn",now));
            Assert.True(created.IsSuccess,created.IsFailure?created.ErrorDetail.Message:null);
            table="cafe_attendance_events";permission="Attendance.Record";
        }
        if(!authorized) target.Execute("DELETE FROM role_permissions WHERE permission=$permission;",("$permission",permission));
        using var loginResponse=await device.Http.PostAsJsonAsync("/api/sync/v1/operators/login",new OperatorLoginRequest("c4-proof",CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK,loginResponse.StatusCode);
        var login=(await loginResponse.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader,login.OperatorToken);
        var envelopes=new SqliteSyncOutboxStore(source.Factory).ReadPending(device.DeviceId).Select(row=>row.Envelope).ToArray();
        Assert.Contains(envelopes,env=>env.OperationId==operation);
        using var pushed=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,envelopes));
        Assert.Equal(HttpStatusCode.OK,pushed.StatusCode);
        var body=(await pushed.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        var result=Assert.Single(body.Results.Where(row=>row.OperationId==operation));
        Assert.Equal(authorized,result.Resolution.IsApplied);
        if(!authorized) {Assert.Equal("Rejected",result.Resolution.Status);Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY",result.Resolution.ResultCode);Assert.Null(result.ChangeFeedSequence);}
        Assert.Equal(authorized?1L:0L,target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
        long expectedFeed=envelopes.Length-(authorized?0:1);
        Assert.Equal(expectedFeed,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        using var retry=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,envelopes));
        Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
        Assert.All((await retry.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results,row=>Assert.True(row.IsDuplicateReplay));
        Assert.Equal(expectedFeed,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(authorized?1L:0L,target.ScalarLong($"SELECT COUNT(*) FROM {table};"));
    }
}

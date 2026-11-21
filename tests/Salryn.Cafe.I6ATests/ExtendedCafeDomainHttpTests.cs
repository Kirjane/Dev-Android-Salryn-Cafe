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
            table="cafe_delivery_fulfillments";permission="Delivery.Manage";
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BookingBoundSaleCommittedV3_AppliesOrDeniesOverRealHttp_AndReplaysExactlyOnce(bool authorized)
    {
        string root = Path.Combine(Path.GetTempPath(), "central-v3-" + Guid.NewGuid().ToString("N"));
        var source = C4ProofDatabase.Create(Path.Combine(root, "source"));
        var target = C4ProofDatabase.Create(Path.Combine(root, "central"));
        target.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));
        Guid customer = Guid.NewGuid();
        foreach (var db in new[] { source, target })
        {
            db.Execute(
                "INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Synthetic Booking V3 Customer',1,$actor,$at,$at);",
                ("$id", customer.ToString("D")),
                ("$actor", C4ProofIds.ActorUserId.ToString("D")),
                ("$at", DateTimeOffset.UtcNow.ToString("O")));
        }

        using var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using var admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using var device = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "Booking V3 Cashier");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid bookingId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid saleOpId = Guid.NewGuid();

        var bookingSvc = new SqliteCafeBookingService(source.Factory);
        var config = new CafeBookingConfigurationMutation(
            customer,
            CafeBookingModes.Event,
            "Synthetic Cafe Event",
            null,
            now.AddDays(1),
            null,
            "Synthetic venue",
            null,
            null,
            null,
            1,
            null,
            C4ProofIds.MenuRevisionId,
            0,
            null,
            null,
            CafeOrderServiceModes.Takeout,
            [new(Guid.NewGuid(), C4ProofIds.ProductId, null, [], 1000, null, 0)]);

        var created = bookingSvc.Create(new(Guid.NewGuid(), bookingId, revisionId, device.DeviceId, host.StoreId, C4ProofIds.ActorUserId, now, config));
        Assert.True(created.IsSuccess, created.IsFailure ? created.ErrorDetail.Message : null);
        var confirmed = bookingSvc.AdvanceStatus(new(Guid.NewGuid(), bookingId, device.DeviceId, host.StoreId, 1, C4ProofIds.ActorUserId, now.AddSeconds(1), CafeBookingStatuses.Confirmed));
        Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.ErrorDetail.Message : null);
        var readyBooking = bookingSvc.AdvanceStatus(new(Guid.NewGuid(), bookingId, device.DeviceId, host.StoreId, 2, C4ProofIds.ActorUserId, now.AddSeconds(2), CafeBookingStatuses.ReadyForFulfillment));
        Assert.True(readyBooking.IsSuccess, readyBooking.IsFailure ? readyBooking.ErrorDetail.Message : null);
        var bind = bookingSvc.BindOrder(new(Guid.NewGuid(), bookingId, orderId, device.DeviceId, host.StoreId, C4ProofIds.ActorUserId, 3, now.AddSeconds(3)));
        Assert.True(bind.IsSuccess, bind.IsFailure ? bind.ErrorDetail.Message : null);

        var submitPrep = source.Orders.SubmitPreparation(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 1, C4ProofIds.ActorUserId, now.AddSeconds(4)));
        Assert.True(submitPrep.IsSuccess, submitPrep.IsFailure ? submitPrep.ErrorDetail.Message : null);
        var preparing = source.Orders.SetPreparationStatus(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 2, C4ProofIds.ActorUserId, now.AddSeconds(5), CafeOrderStates.Preparing));
        Assert.True(preparing.IsSuccess, preparing.IsFailure ? preparing.ErrorDetail.Message : null);
        var readyOrder = source.Orders.SetPreparationStatus(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 3, C4ProofIds.ActorUserId, now.AddSeconds(6), CafeOrderStates.Ready));
        Assert.True(readyOrder.IsSuccess, readyOrder.IsFailure ? readyOrder.ErrorDetail.Message : null);

        var completed = source.Completion.Complete(
            new(Guid.NewGuid(), saleOpId, device.DeviceId, orderId, host.StoreId, 4, C4ProofIds.ActorUserId, now.AddSeconds(7), [new(PaymentMethod.Cash, 500)])
            {
                FinancialDecisionOperationId = Guid.NewGuid(),
                FinancialDecisionId = Guid.NewGuid(),
                BookingFinalization = new(bookingId, revisionId, bind.Value.BookingOrderLinkId, 4)
            });
        Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

        var allEnvelopes = new SqliteSyncOutboxStore(source.Factory).ReadPending(device.DeviceId)
            .Select(row => row.Envelope)
            .OrderBy(env => env.LocalSequence)
            .ToArray();
        var saleEnvelope = Assert.Single(allEnvelopes, env => env.OperationId == saleOpId);
        Assert.Equal(SaleCommittedV3Contract.OperationType, saleEnvelope.OperationType);
        var prereqEnvelopes = allEnvelopes.Where(env => env.LocalSequence < saleEnvelope.LocalSequence).ToArray();
        var postSaleEnvelopes = allEnvelopes.Where(env => env.LocalSequence > saleEnvelope.LocalSequence).ToArray();
        Assert.NotEmpty(prereqEnvelopes);

        using var initialLogin = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);
        var initialToken = (await initialLogin.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, initialToken.OperatorToken);

        using var prereqPush = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, prereqEnvelopes));
        Assert.Equal(HttpStatusCode.OK, prereqPush.StatusCode);
        var prereqBody = (await prereqPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.All(prereqBody.Results, row => Assert.True(row.Resolution.IsApplied, row.Resolution.ResultCode));
        long feedAfterPrereqs = target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;");

        if (!authorized)
        {
            target.Execute("DELETE FROM role_permissions WHERE permission='Booking.Fulfill';");
            using var reducedLogin = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/operators/login",
                new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
            Assert.Equal(HttpStatusCode.OK, reducedLogin.StatusCode);
            var reducedToken = (await reducedLogin.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
            device.Http.DefaultRequestHeaders.Remove(CentralSyncEndpoints.OperatorTokenHeader);
            device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, reducedToken.OperatorToken);
        }

        using var salePush = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, salePush.StatusCode);
        var saleBody = (await salePush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        var saleResult = Assert.Single(saleBody.Results);
        Assert.Equal(authorized, saleResult.Resolution.IsApplied);

        if (!authorized)
        {
            Assert.Equal("Rejected", saleResult.Resolution.Status);
            Assert.Equal("ERR_CANONICAL_OPERATOR_AUTHORITY", saleResult.Resolution.ResultCode);
            Assert.Null(saleResult.ChangeFeedSequence);
            Assert.Equal(0L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(0L, target.ScalarLong("SELECT COUNT(*) FROM cafe_booking_sale_links;"));
            Assert.Equal(10000L, target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
            Assert.Equal(feedAfterPrereqs, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        }
        else
        {
            Assert.NotNull(saleResult.ChangeFeedSequence);
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(500L, target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_booking_sale_links;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_bookings WHERE booking_id=$id AND status='Fulfilled' AND fulfillment_state='SaleBound';", ("$id", bookingId.ToString("D"))));
            Assert.Equal(9000L, target.ScalarLong("SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';"));
            Assert.True(target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;") > feedAfterPrereqs);
        }

        long feedAfterSale = target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;");
        using var replayPush = await device.Http.PostAsJsonAsync(
            "/api/sync/v1/push",
            new PushOperationsRequest(1, [saleEnvelope]));
        Assert.Equal(HttpStatusCode.OK, replayPush.StatusCode);
        var replayResult = Assert.Single((await replayPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);
        Assert.True(replayResult.IsDuplicateReplay);
        Assert.Equal(feedAfterSale, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.Equal(authorized ? 1L : 0L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(authorized ? 1L : 0L, target.ScalarLong("SELECT COUNT(*) FROM cafe_booking_sale_links;"));

        if (authorized && postSaleEnvelopes.Length > 0)
        {
            using var postPush = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, postSaleEnvelopes));
            Assert.Equal(HttpStatusCode.OK, postPush.StatusCode);
            var postBody = (await postPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.All(postBody.Results, row => Assert.True(row.Resolution.IsApplied, row.Resolution.ResultCode));
        }
    }
}

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
    [InlineData("incident", true)] [InlineData("incident", false)]
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
        else if(family=="attendance")
        {
            var created=new SqliteCafeAttendanceIncidentService(source.Factory).RecordAttendance(new(operation,Guid.NewGuid(),host.StoreId,C4ProofIds.ActorUserId,C4ProofIds.ActorUserId,device.DeviceId,"ClockIn",now));
            Assert.True(created.IsSuccess,created.IsFailure?created.ErrorDetail.Message:null);
            table="cafe_attendance_events";permission="Attendance.Record";
        }
        else
        {
            var created=new SqliteCafeAttendanceIncidentService(source.Factory).CreateIncident(new(operation,Guid.NewGuid(),Guid.NewGuid(),host.StoreId,C4ProofIds.ActorUserId,device.DeviceId,C4ProofIds.ActorUserId,now,"Grinder jam","Burr motor tripped","Equipment","Medium",now));
            Assert.True(created.IsSuccess,created.IsFailure?created.ErrorDetail.Message:null);
            table="cafe_incidents";permission="Incident.Record";
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
        Assert.NotEmpty(postSaleEnvelopes);
        Assert.Contains(postSaleEnvelopes, env => env.OperationType == CafeOrderOperationContract.OrderCompletedV1);
        Assert.Contains(postSaleEnvelopes, env => env.OperationType == IngredientConsumptionCommittedV1Contract.OperationType);

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

        if (authorized)
        {
            using var postPush = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, postSaleEnvelopes));
            Assert.Equal(HttpStatusCode.OK, postPush.StatusCode);
            var postBody = (await postPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
            Assert.All(postBody.Results, row => Assert.True(row.Resolution.IsApplied, row.Resolution.ResultCode));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_orders WHERE order_id=$id AND state=$state;", ("$id", orderId.ToString("D")), ("$state", CafeOrderStates.Completed)));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_order_sale_links;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sale_ingredient_consumption_decisions;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;", ("$type", AppliedBusinessChangeContract.OrderCompletedV1AppliedV1)));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed WHERE change_type=$type;", ("$type", AppliedBusinessChangeContract.IngredientConsumptionCommittedV1AppliedV1)));

            long feedBeforeMismatch = target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;");
            var mismatchedEnvelope = SyncProtocolV1.CreateEnvelope(
                deviceId: device.DeviceId,
                storeId: host.StoreId,
                actorUserId: C4ProofIds.ActorUserId,
                localSequence: allEnvelopes.Max(env => env.LocalSequence) + 1,
                operationType: SaleCommittedV3Contract.OperationType,
                schemaVersion: SaleCommittedV3Contract.SchemaVersion,
                aggregateId: Guid.NewGuid(),
                baseVersion: null,
                occurredAtLocal: saleEnvelope.OccurredAtLocal,
                payloadJson: saleEnvelope.PayloadJson);
            using var mismatchPush = await device.Http.PostAsJsonAsync(
                "/api/sync/v1/push",
                new PushOperationsRequest(1, [mismatchedEnvelope]));
            Assert.Equal(HttpStatusCode.OK, mismatchPush.StatusCode);
            var mismatchResult = Assert.Single((await mismatchPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results);
            Assert.Equal("Rejected", mismatchResult.Resolution.Status);
            Assert.Equal("SaleCommittedV3.EnvelopeAuthorityMismatch", mismatchResult.Resolution.ResultCode);
            Assert.Null(mismatchResult.ChangeFeedSequence);
            Assert.Equal(feedBeforeMismatch, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM sales;"));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_booking_sale_links;"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaleRefundRequestApproveCommit_AppliesOrDeniesOverRealHttp_AndReplaysExactlyOnce(bool authorized)
    {
        string root = Path.Combine(Path.GetTempPath(), "central-refund-" + Guid.NewGuid().ToString("N"));
        var source = C4ProofDatabase.Create(Path.Combine(root, "source"));
        var target = C4ProofDatabase.Create(Path.Combine(root, "central"));
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));

        using var host = new CentralSyncTestHost(target.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using var admin = host.CreateHttpsClient();
        string adminToken = await host.LoginAsAdminAsync(admin);
        using var device = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, adminToken, "Refund C5 Cashier");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid orderId = Guid.NewGuid();
        Guid saleOpId = Guid.NewGuid();
        var saved = source.Orders.SaveRevision(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 0, C4ProofIds.ActorUserId,
            now, CafeOrderServiceModes.DineIn, "T4", [new(Guid.NewGuid(), C4ProofIds.ProductId, null, [], 1000, null, 0)]));
        Assert.True(saved.IsSuccess);
        Assert.True(source.Orders.SubmitPreparation(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 1, C4ProofIds.ActorUserId, now.AddSeconds(1))).IsSuccess);
        Assert.True(source.Orders.SetPreparationStatus(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 2, C4ProofIds.ActorUserId, now.AddSeconds(2), CafeOrderStates.Preparing)).IsSuccess);
        Assert.True(source.Orders.SetPreparationStatus(new(Guid.NewGuid(), device.DeviceId, orderId, host.StoreId, 3, C4ProofIds.ActorUserId, now.AddSeconds(3), CafeOrderStates.Ready)).IsSuccess);
        var completed = source.Completion.Complete(new(Guid.NewGuid(), saleOpId, device.DeviceId, orderId, host.StoreId, 4, C4ProofIds.ActorUserId,
            now.AddSeconds(4), [new(PaymentMethod.Cash, 500)])
        {
            FinancialDecisionOperationId = Guid.NewGuid(),
            FinancialDecisionId = Guid.NewGuid()
        });
        Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

        Guid saleId = completed.Value.SaleId;
        Guid saleItemId;
        Guid cashierSessionId;
        using (var conn = source.Factory.CreateConnection())
        {
            conn.Open();
            using var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = "SELECT id FROM sale_items WHERE sale_id=$s LIMIT 1;";
            itemCmd.Parameters.AddWithValue("$s", saleId.ToString("D"));
            saleItemId = Guid.Parse((string)itemCmd.ExecuteScalar()!);
            using var sessCmd = conn.CreateCommand();
            sessCmd.CommandText = "SELECT id FROM cashier_sessions WHERE status=1 LIMIT 1;";
            cashierSessionId = Guid.Parse((string)sessCmd.ExecuteScalar()!);
        }

        Guid refundId = Guid.NewGuid();
        Guid reqOpId = Guid.NewGuid(), resolveOpId = Guid.NewGuid(), commitOpId = Guid.NewGuid();
        var refundSvc = new SqliteCafeRefundService(source.Factory);
        var requested = refundSvc.Request(new(reqOpId, device.DeviceId, refundId, saleId, host.StoreId, 0, C4ProofIds.ActorUserId,
            now.AddSeconds(5), "Wrong drink", [new(Guid.NewGuid(), saleItemId, 1000, CafeRefundStockDispositions.Restock, null)]));
        Assert.True(requested.IsSuccess, requested.IsFailure ? requested.ErrorDetail.Message : null);
        var approved = refundSvc.Resolve(new(resolveOpId, device.DeviceId, refundId, saleId, host.StoreId, 1, C4ProofIds.ActorUserId,
            now.AddSeconds(6), true, "Approved by lead"));
        Assert.True(approved.IsSuccess, approved.IsFailure ? approved.ErrorDetail.Message : null);
        var committed = refundSvc.Commit(new(commitOpId, device.DeviceId, refundId, saleId, host.StoreId, 2, C4ProofIds.ActorUserId,
            cashierSessionId, now.AddSeconds(7), [new(Guid.NewGuid(), CafeRefundSettlementMethods.Cash, 500)]));
        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.ErrorDetail.Message : null);

        var allEnvelopes = new SqliteSyncOutboxStore(source.Factory).ReadPending(device.DeviceId)
            .Select(r => r.Envelope)
            .OrderBy(e => e.LocalSequence)
            .ToArray();
        var prereq = allEnvelopes.Where(e => e.OperationId != commitOpId && e.OperationId != resolveOpId).ToArray();
        var approveAndCommit = allEnvelopes.Where(e => e.OperationId == resolveOpId || e.OperationId == commitOpId).ToArray();

        if (!authorized)
        {
            target.Execute("DELETE FROM role_permissions WHERE permission='Sales.RefundApprove';");
        }

        using var loginResp = await device.Http.PostAsJsonAsync("/api/sync/v1/operators/login",
            new OperatorLoginRequest("c4-proof", CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = (await loginResp.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader, login.OperatorToken);

        using var prereqPush = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, prereq));
        Assert.Equal(HttpStatusCode.OK, prereqPush.StatusCode);
        var prereqBody = (await prereqPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.All(prereqBody.Results, r => Assert.True(r.Resolution.IsApplied, r.Resolution.ResultCode));

        using var finalPush = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, approveAndCommit));
        Assert.Equal(HttpStatusCode.OK, finalPush.StatusCode);
        var finalBody = (await finalPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        if (authorized)
        {
            Assert.All(finalBody.Results, r => Assert.True(r.Resolution.IsApplied, r.Resolution.ResultCode));
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_sale_refunds WHERE refund_id=$r AND state='Committed';", ("$r", refundId.ToString("D"))));
            Assert.Equal(500L, target.ScalarLong("SELECT COALESCE(SUM(-delta_cents), 0) FROM cash_movements WHERE delta_cents < 0;"));
        }
        else
        {
            Assert.All(finalBody.Results, r =>
            {
                Assert.False(r.Resolution.IsApplied);
                Assert.True(r.Resolution.IsNeedsReview);
                Assert.Equal("NeedsReview", r.Resolution.Status);
                Assert.Equal("CafeRefundSync.PermissionRequired", r.Resolution.ResultCode);
                Assert.Null(r.ChangeFeedSequence);
            });
            Assert.Equal(1L, target.ScalarLong("SELECT COUNT(*) FROM cafe_sale_refunds WHERE refund_id=$r AND state='Requested';", ("$r", refundId.ToString("D"))));
            Assert.Equal(0L, target.ScalarLong("SELECT COUNT(*) FROM cash_movements WHERE delta_cents < 0;"));
        }

        long feedAfter = target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;");
        using var replayPush = await device.Http.PostAsJsonAsync("/api/sync/v1/push", new PushOperationsRequest(1, approveAndCommit));
        Assert.Equal(HttpStatusCode.OK, replayPush.StatusCode);
        Assert.All((await replayPush.Content.ReadFromJsonAsync<PushOperationsResponse>())!.Results, r => Assert.True(r.IsDuplicateReplay));
        Assert.Equal(feedAfter, target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
    }
}

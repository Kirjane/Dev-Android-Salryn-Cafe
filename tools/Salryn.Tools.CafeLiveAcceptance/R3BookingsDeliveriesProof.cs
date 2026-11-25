using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;

internal static class R3BookingsDeliveriesProof
{
    public static async Task RunAsync()
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        string? priorDevMode = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        string root = Path.Combine(Path.GetTempPath(), "salryn-r3-bookings-deliveries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fixture = R3LiveProofFixture.Create(root);
            var db = fixture.Db;
            // Start with no open cashier shift so we prove 409 CafeBooking.OpenCashierSessionRequired before opening a live shift via HTTP.
            db.Execute("UPDATE cashier_sessions SET status=2, closed_at=$at WHERE status=1;", ("$at", DateTimeOffset.UtcNow.ToString("O")));
            string nonce = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "true");

            int checks = 0;
            void Check(bool ok, string name)
            {
                if (!ok) throw new InvalidOperationException($"R3 Bookings/Deliveries assertion failed: {name}; root: {root}");
                checks++;
            }

            async Task<(JsonElement Body, bool ReplayedHeader)> ReadWithHeader(HttpResponseMessage response, HttpStatusCode expected, string name)
            {
                using (response)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.StatusCode != expected)
                    {
                        throw new InvalidOperationException($"{name}: expected {(int)expected} got {(int)response.StatusCode}: {text}");
                    }
                    bool replayedHeader = response.Headers.TryGetValues("X-Idempotency-Replayed", out var vals) &&
                        vals.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
                    checks++;
                    return (string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone(), replayedHeader);
                }
            }

            async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected, string name) =>
                (await ReadWithHeader(response, expected, name).ConfigureAwait(false)).Body;

            async Task<HttpResponseMessage> PostIdem(HttpClient http, string path, Guid opId, object body)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = JsonContent.Create(body)
                };
                req.Headers.Add("Idempotency-Key", opId.ToString("D"));
                return await http.SendAsync(req).ConfigureAwait(false);
            }

            Guid bookingId = Guid.NewGuid();
            Guid bookingRevisionId = Guid.NewGuid();
            Guid depositReceiveOpId = Guid.NewGuid();
            Guid depositFactId = Guid.NewGuid();
            Guid depositCashMovementId = Guid.NewGuid();
            Guid depositRefundOpId = Guid.NewGuid();
            Guid refundFactId = Guid.NewGuid();
            Guid deliveryId = Guid.NewGuid();
            Guid deliveryCreateOpId = Guid.NewGuid();
            Guid deliveryResolveOpId = Guid.NewGuid();

            object? savedDepositReceiveBody = null;
            object? savedDeliveryCreateBody = null;
            object? savedDeliveryResolveBody = null;

            await using (var host = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(db.Paths),
                Url = "http://127.0.0.1:0",
                TestHostNonce = nonce,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }))
            {
                await host.StartAsync();
                string baseUrl = host.Urls.Single();

                using var ownerHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl) };
                using var cashierHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl) };
                await R3LiveProofFixture.LoginAsync(ownerHttp, R3LiveProofFixture.OwnerUsername, R3LiveProofFixture.OwnerPassword);
                await R3LiveProofFixture.LoginAsync(cashierHttp, R3LiveProofFixture.CashierUsername, R3LiveProofFixture.CashierPassword);

                DateTimeOffset now = DateTimeOffset.UtcNow;

                // --- INT-03: Booking Create (Event + nested configuration) & Confirm ---
                Guid createBookingOpId = Guid.NewGuid();
                var createBookingBody = new
                {
                    operationId = createBookingOpId,
                    bookingId,
                    revisionId = bookingRevisionId,
                    occurredAtUtc = now,
                    configuration = new
                    {
                        customerId = R3LiveProofFixture.CustomerId,
                        bookingMode = "Event",
                        eventName = "Synthetic Roastery Launch",
                        eventStartUtc = now.AddDays(2),
                        menuPublicationRevisionId = C4ProofIds.MenuRevisionId,
                        depositRequirementCents = 500L,
                        intendedServiceMode = "Takeout",
                        lines = new[]
                        {
                            new
                            {
                                bookingLineId = Guid.NewGuid(),
                                productId = C4ProofIds.ProductId,
                                quantityX1000 = 2000L,
                                displayOrder = 0
                            }
                        }
                    }
                };

                // Cashier denied Booking.Manage (403).
                await Read(await PostIdem(cashierHttp, "/api/cafe/bookings", createBookingOpId, createBookingBody), HttpStatusCode.Forbidden, "cashier denied booking create");

                var createdBooking = await Read(await PostIdem(ownerHttp, "/api/cafe/bookings", createBookingOpId, createBookingBody), HttpStatusCode.OK, "owner creates booking");
                Check(createdBooking.GetProperty("bookingId").GetGuid() == bookingId, "bookingId echoed");
                Check(createdBooking.GetProperty("status").GetString() == "Draft", "initial booking status is Draft");
                int bookingVersion = createdBooking.GetProperty("bookingVersion").GetInt32();

                Guid confirmOpId = Guid.NewGuid();
                var confirmedBooking = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/bookings/{bookingId:D}/confirm", confirmOpId, new
                    {
                        operationId = confirmOpId,
                        expectedVersion = bookingVersion,
                        occurredAtUtc = now.AddSeconds(5)
                    }),
                    HttpStatusCode.OK,
                    "owner confirms booking");
                Check(confirmedBooking.GetProperty("status").GetString() == "Confirmed", "booking confirmed");

                // --- INT-04: Booking Deposit Receive (requires open shift) -> Replay -> Refund with Allocations ---
                savedDepositReceiveBody = new
                {
                    operationId = depositReceiveOpId,
                    depositFactId,
                    cashMovementId = depositCashMovementId,
                    bookingRevisionId,
                    amountCents = 500L,
                    reference = "DEP-CASH-500",
                    occurredAtUtc = now.AddSeconds(10)
                };

                // Must fail closed with 409 CafeBooking.OpenCashierSessionRequired when shift is closed.
                Guid closedShiftOpId = Guid.NewGuid();
                var closedShiftErr = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/bookings/{bookingId:D}/deposits/receive", closedShiftOpId, new
                    {
                        operationId = closedShiftOpId,
                        depositFactId = Guid.NewGuid(),
                        cashMovementId = Guid.NewGuid(),
                        bookingRevisionId,
                        amountCents = 500L,
                        reference = "DEP-CLOSED-SHIFT",
                        occurredAtUtc = now.AddSeconds(8)
                    }),
                    HttpStatusCode.Conflict,
                    "deposit denied without open shift");
                Check(closedShiftErr.GetProperty("code").GetString() == "CafeBooking.OpenCashierSessionRequired", "closed shift error code");

                // Open cashier shift, then receive deposit.
                await Read(
                    await ownerHttp.PostAsJsonAsync("/api/shift/open", new { openingCashCents = 100000L, clientRequestId = Guid.NewGuid().ToString("D") }),
                    HttpStatusCode.OK,
                    "open shift for deposit");

                var depReceived = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/bookings/{bookingId:D}/deposits/receive", depositReceiveOpId, savedDepositReceiveBody),
                    HttpStatusCode.OK,
                    "receive booking deposit");
                Check(depReceived.GetProperty("depositFactId").GetGuid() == depositFactId, "depositFactId persisted");
                Check(depReceived.GetProperty("amountCents").GetInt64() == 500L, "deposit amount 500");

                // HTTP replay of identical deposit receive must signal replay and not duplicate.
                var (_, depReplayHdr) = await ReadWithHeader(
                    await PostIdem(ownerHttp, $"/api/cafe/bookings/{bookingId:D}/deposits/receive", depositReceiveOpId, savedDepositReceiveBody),
                    HttpStatusCode.OK,
                    "replay booking deposit receive");
                Check(depReplayHdr, "deposit receive replay header true");

                // Refund 200 cents allocated to the exact received depositFactId.
                var depRefund = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/bookings/{bookingId:D}/deposits/refund", depositRefundOpId, new
                    {
                        operationId = depositRefundOpId,
                        depositFactId = refundFactId,
                        cashMovementId = Guid.NewGuid(),
                        bookingRevisionId,
                        amountCents = 200L,
                        reason = "Headcount reduced",
                        reference = "REF-CASH-200",
                        allocations = new[] { new { receivedDepositFactId = depositFactId, amountCents = 200L } },
                        occurredAtUtc = now.AddSeconds(20)
                    }),
                    HttpStatusCode.OK,
                    "refund booking deposit with allocations");
                Check(depRefund.GetProperty("depositFactId").GetGuid() == refundFactId, "refund depositFactId persisted");
                Check(depRefund.GetProperty("amountCents").GetInt64() == 200L, "refund amount 200");

                // --- INT-05: Order-Bound Delivery (DineIn rejected -> Takeout created -> Pending -> Dispatched -> Delivered) ---
                Guid dineInOrderId = Guid.NewGuid();
                Guid dineInOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, "/api/cafe/orders/revision", dineInOpId, new
                    {
                        operationId = dineInOpId,
                        orderId = dineInOrderId,
                        expectedVersion = 0,
                        serviceMode = "DineIn",
                        notes = "Dine-in should not accept delivery",
                        lines = new[] { new { orderLineId = Guid.NewGuid(), productId = C4ProofIds.ProductId, quantityX1000 = 1000L, displayOrder = 0 } }
                    }),
                    HttpStatusCode.OK,
                    "create DineIn order");

                // Delivery against DineIn order MUST be rejected (409).
                Guid badDeliveryOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, "/api/cafe/deliveries", badDeliveryOpId, new
                    {
                        operationId = badDeliveryOpId,
                        deliveryId = Guid.NewGuid(),
                        orderId = dineInOrderId,
                        occurredAtUtc = now.AddSeconds(25),
                        recipientName = "Should Fail",
                        deliveryAddress = "123 DineIn St",
                        deliveryFeeCents = 5000L
                    }),
                    HttpStatusCode.Conflict,
                    "delivery rejected for DineIn order");

                // Create eligible non-terminal Takeout order.
                Guid takeoutOrderId = Guid.NewGuid();
                Guid takeoutOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, "/api/cafe/orders/revision", takeoutOpId, new
                    {
                        operationId = takeoutOpId,
                        orderId = takeoutOrderId,
                        expectedVersion = 0,
                        serviceMode = "Takeout",
                        notes = "Eligible delivery order",
                        lines = new[] { new { orderLineId = Guid.NewGuid(), productId = C4ProofIds.ProductId, quantityX1000 = 1000L, displayOrder = 0 } }
                    }),
                    HttpStatusCode.OK,
                    "create Takeout order");

                savedDeliveryCreateBody = new
                {
                    operationId = deliveryCreateOpId,
                    deliveryId,
                    orderId = takeoutOrderId,
                    occurredAtUtc = now.AddSeconds(30),
                    recipientName = "Maria Santos",
                    deliveryAddress = "456 Ayala Ave, Makati",
                    deliveryFeeCents = 5000L
                };

                var createdDel = await Read(
                    await PostIdem(ownerHttp, "/api/cafe/deliveries", deliveryCreateOpId, savedDeliveryCreateBody),
                    HttpStatusCode.OK,
                    "create Takeout order-bound delivery");
                Check(createdDel.GetProperty("status").GetString() == "Pending", "initial delivery status is Pending (not PendingDispatch)");
                int delVersion = createdDel.GetProperty("version").GetInt32();

                // Dispatch before C4 order is completed into an accepted Sale MUST return 409 CafeDelivery.CompletedSaleRequired.
                Guid earlyDispatchOpId = Guid.NewGuid();
                var earlyDispatchErr = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/deliveries/{deliveryId:D}/dispatch", earlyDispatchOpId, new
                    {
                        operationId = earlyDispatchOpId,
                        expectedVersion = delVersion,
                        occurredAtUtc = now.AddSeconds(35)
                    }),
                    HttpStatusCode.Conflict,
                    "dispatch denied before order completed into sale");
                Check(earlyDispatchErr.GetProperty("code").GetString() == "CafeDelivery.CompletedSaleRequired", "completed sale required before dispatch");

                // Submit and complete the Takeout order into a Cash sale (500 cents for 1x product + 5000 delivery fee handled by order total).
                Guid submitOrdOpId = Guid.NewGuid();
                var submittedOrd = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{takeoutOrderId:D}/submit", submitOrdOpId, new
                    {
                        operationId = submitOrdOpId,
                        expectedVersion = 1
                    }),
                    HttpStatusCode.OK,
                    "submit Takeout order");
                int submittedVer = submittedOrd.GetProperty("orderVersion").GetInt32();

                Guid prepOpId = Guid.NewGuid();
                var prepOrd = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{takeoutOrderId:D}/preparation", prepOpId, new
                    {
                        operationId = prepOpId,
                        expectedVersion = submittedVer,
                        targetState = "Preparing"
                    }),
                    HttpStatusCode.OK,
                    "advance Takeout order to Preparing");
                int prepVer = prepOrd.GetProperty("orderVersion").GetInt32();

                Guid readyOpId = Guid.NewGuid();
                var readyOrd = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{takeoutOrderId:D}/preparation", readyOpId, new
                    {
                        operationId = readyOpId,
                        expectedVersion = prepVer,
                        targetState = "Ready"
                    }),
                    HttpStatusCode.OK,
                    "advance Takeout order to Ready");
                int readyVer = readyOrd.GetProperty("orderVersion").GetInt32();

                Guid completeOrdOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{takeoutOrderId:D}/complete", completeOrdOpId, new
                    {
                        completionOperationId = completeOrdOpId,
                        saleCommittedOperationId = Guid.NewGuid(),
                        expectedVersion = readyVer,
                        fees = new[] { new { feeId = Guid.NewGuid(), feeType = "Delivery", displayLabel = "Delivery Fee", amountCents = 5000L } },
                        payments = new[] { new { method = "Cash", amountCents = 5500L } }
                    }),
                    HttpStatusCode.OK,
                    "complete Takeout order into Cash sale with exact Delivery fee");

                Guid dispatchOpId = Guid.NewGuid();
                var dispatchedDel = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/deliveries/{deliveryId:D}/dispatch", dispatchOpId, new
                    {
                        operationId = dispatchOpId,
                        expectedVersion = delVersion,
                        occurredAtUtc = now.AddSeconds(40)
                    }),
                    HttpStatusCode.OK,
                    "dispatch delivery");
                Check(dispatchedDel.GetProperty("status").GetString() == "Dispatched", "delivery advanced to Dispatched");
                int dispatchedVersion = dispatchedDel.GetProperty("version").GetInt32();

                savedDeliveryResolveBody = new
                {
                    operationId = deliveryResolveOpId,
                    expectedVersion = dispatchedVersion,
                    targetStatus = "Delivered",
                    occurredAtUtc = now.AddSeconds(50)
                };
                var deliveredDel = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/deliveries/{deliveryId:D}/resolve", deliveryResolveOpId, savedDeliveryResolveBody),
                    HttpStatusCode.OK,
                    "resolve delivery as Delivered");
                Check(deliveredDel.GetProperty("status").GetString() == "Delivered", "delivery resolved as Delivered");

                await host.StopAsync();
            }

            // Host restart replay proof + SQLite persistence assertions.
            await using (var restarted = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(db.Paths),
                Url = "http://127.0.0.1:0",
                TestHostNonce = nonce,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }))
            {
                await restarted.StartAsync();
                using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(restarted.Urls.Single()) };
                await R3LiveProofFixture.LoginAsync(http, R3LiveProofFixture.OwnerUsername, R3LiveProofFixture.OwnerPassword);

                var (_, restartDepHdr) = await ReadWithHeader(
                    await PostIdem(http, $"/api/cafe/bookings/{bookingId:D}/deposits/receive", depositReceiveOpId, savedDepositReceiveBody!),
                    HttpStatusCode.OK,
                    "restart replay booking deposit");
                Check(restartDepHdr, "restart booking deposit replay signaled");

                var (_, restartDelHdr) = await ReadWithHeader(
                    await PostIdem(http, $"/api/cafe/deliveries/{deliveryId:D}/resolve", deliveryResolveOpId, savedDeliveryResolveBody!),
                    HttpStatusCode.OK,
                    "restart replay delivery resolve");
                Check(restartDelHdr, "restart delivery resolve replay signaled");

                Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_bookings WHERE booking_id=$id AND status='Confirmed';", ("$id", bookingId.ToString("D"))) == 1, "1 confirmed booking in SQLite");
                Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE booking_id=$id;", ("$id", bookingId.ToString("D"))) == 2, "2 deposit facts (receive+refund) in SQLite");
                Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE delivery_id=$id AND status='Delivered';", ("$id", deliveryId.ToString("D"))) == 1, "1 delivered fulfillment in SQLite");

                await restarted.StopAsync();
            }

            Console.WriteLine($"PASS: {checks} R3 Bookings & Deliveries HTTP/SQLite/restart assertions (INT-03/04/05); DEBUG_DEVELOPMENT_LICENSE; not Release acceptance. Root: {root}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", priorDevMode);
        }
#endif
    }
}

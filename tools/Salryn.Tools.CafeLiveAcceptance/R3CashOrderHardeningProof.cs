using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

internal static class R3CashOrderHardeningProof
{
    public static async Task RunAsync()
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        string? priorDevMode = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        string root = Path.Combine(Path.GetTempPath(), "salryn-r3-cash-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fixture = R3LiveProofFixture.Create(root);
            var db = fixture.Db;
            // Close pre-seeded legacy shift row so Operator A opens a fresh verified shift via HTTP.
            db.Execute("UPDATE cashier_sessions SET status=2, closed_at=$at WHERE status=1;", ("$at", DateTimeOffset.UtcNow.ToString("O")));
            // Seed a second independent Admin operator (with Audit.View) to prove non-self variance review.
            const string reviewerUser = "r3-auditor";
            const string reviewerPass = "Synthetic-R3-Auditor!2026";
            db.Execute(
                """
                INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at,updated_at)
                VALUES($id,$user,'R3 Independent Auditor',$hash,$role,1,$at,NULL);
                """,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$user", reviewerUser),
                ("$hash", SalrynPasswordHasher.HashPassword(reviewerPass).Value),
                ("$role", DefaultSeedData.AdminRoleId),
                ("$at", DateTimeOffset.UtcNow.ToString("O")));

            string nonce = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "true");

            int checks = 0;
            void Check(bool ok, string name)
            {
                if (!ok) throw new InvalidOperationException($"R3 Cash/Order assertion failed: {name}; root: {root}");
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

            Guid closedShiftId = Guid.Empty;
            Guid orderId = Guid.NewGuid();
            Guid completeOpId = Guid.NewGuid();
            object? savedCompleteBody = null;

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
                using var managerHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl) };
                using var cashierHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl) };
                await R3LiveProofFixture.LoginAsync(ownerHttp, R3LiveProofFixture.OwnerUsername, R3LiveProofFixture.OwnerPassword);
                await R3LiveProofFixture.LoginAsync(managerHttp, reviewerUser, reviewerPass);
                await R3LiveProofFixture.LoginAsync(cashierHttp, R3LiveProofFixture.CashierUsername, R3LiveProofFixture.CashierPassword);

                // --- INT-07 / INT-08: Cash Drawer History & Independent Non-Self Variance Review ---
                var opened = await Read(
                    await ownerHttp.PostAsJsonAsync("/api/shift/open", new { openingCashCents = 100000L, clientRequestId = Guid.NewGuid().ToString("D") }),
                    HttpStatusCode.OK,
                    "Operator A opens shift");
                closedShiftId = Guid.Parse(opened.GetProperty("shift").GetProperty("shiftId").GetString()!);

                var mvSnap = await Read(
                    await ownerHttp.PostAsJsonAsync("/api/cash-drawer/movements", new
                    {
                        eventType = "CashIn",
                        amountCents = 5000L,
                        clientRequestId = Guid.NewGuid().ToString("D"),
                        reason = "Change fund top-up"
                    }),
                    HttpStatusCode.OK,
                    "Operator A records CashIn movement");
                Check(mvSnap.GetProperty("expectedCashCents").GetInt64() == 105000L, "expected cash 105000 after CashIn");

                // Blind close with 102000 counted -> -3000 variance, PendingReview.
                await Read(
                    await ownerHttp.PostAsJsonAsync("/api/shift/close", new
                    {
                        countedCashCents = 102000L,
                        physicalCountConfirmed = true,
                        clientRequestId = Guid.NewGuid().ToString("D"),
                        note = "Short by 30 PHP"
                    }),
                    HttpStatusCode.OK,
                    "Operator A closes shift with variance");

                // Cashier lacks Audit.View -> 403 on history & review.
                await Read(await cashierHttp.GetAsync("/api/cash-drawer/history?limit=10&offset=0"), HttpStatusCode.Forbidden, "cashier denied drawer history");
                await Read(
                    await cashierHttp.PostAsJsonAsync($"/api/cash-drawer/{closedShiftId:D}/review", new
                    {
                        decision = "Acknowledged",
                        reason = "Unauthorized review attempt",
                        clientRequestId = Guid.NewGuid().ToString("D")
                    }),
                    HttpStatusCode.Forbidden,
                    "cashier denied variance review");

                // Operator A self-review MUST be denied with 409 CashDrawer.SelfReviewDenied.
                var selfReviewErr = await Read(
                    await ownerHttp.PostAsJsonAsync($"/api/cash-drawer/{closedShiftId:D}/review", new
                    {
                        decision = "Acknowledged",
                        reason = "Self review attempt",
                        clientRequestId = Guid.NewGuid().ToString("D")
                    }),
                    HttpStatusCode.Conflict,
                    "Operator A self-review denied");
                Check(selfReviewErr.GetProperty("code").GetString() == "CashDrawer.SelfReviewDenied", "self-review error code");

                // Operator B (Manager with Audit.View) reads history and acknowledges variance.
                var history = await Read(await managerHttp.GetAsync("/api/cash-drawer/history?limit=10&offset=0"), HttpStatusCode.OK, "Operator B reads drawer history");
                var targetRow = history.GetProperty("items").EnumerateArray().First(x => x.GetProperty("sessionId").GetString() == closedShiftId.ToString("D"));
                Check(targetRow.GetProperty("varianceCents").GetInt64() == -3000L, "history variance is -3000");
                Check(targetRow.GetProperty("reviewStatus").GetString() == "PendingReview", "history status PendingReview");

                string reviewReqId = Guid.NewGuid().ToString("D");
                var reviewed = await Read(
                    await managerHttp.PostAsJsonAsync($"/api/cash-drawer/{closedShiftId:D}/review", new
                    {
                        decision = "Acknowledged",
                        reason = "Verified short count by independent manager",
                        clientRequestId = reviewReqId
                    }),
                    HttpStatusCode.OK,
                    "Operator B acknowledges variance");
                Check(reviewed.GetProperty("reviewStatus").GetString() == "Acknowledged", "variance status Acknowledged");

                // Replay with identical clientRequestId succeeds idempotently; changed reason returns 409 CashDrawer.RequestConflict.
                await Read(
                    await managerHttp.PostAsJsonAsync($"/api/cash-drawer/{closedShiftId:D}/review", new
                    {
                        decision = "Acknowledged",
                        reason = "Verified short count by independent manager",
                        clientRequestId = reviewReqId
                    }),
                    HttpStatusCode.OK,
                    "Operator B review replay idempotent");
                var conflictReview = await Read(
                    await managerHttp.PostAsJsonAsync($"/api/cash-drawer/{closedShiftId:D}/review", new
                    {
                        decision = "Acknowledged",
                        reason = "Mutated reason for same requestId",
                        clientRequestId = reviewReqId
                    }),
                    HttpStatusCode.Conflict,
                    "changed review replay rejected");
                Check(conflictReview.GetProperty("code").GetString() == "CashDrawer.RequestConflict", "review conflict code");

                // --- INT-09: Order Concurrency, Cash/AR/Partial-Only Tender Policy, Revoked Permission, No Completed Order Re-Open ---
                // Open a shift for Operator A to complete an order.
                await Read(
                    await ownerHttp.PostAsJsonAsync("/api/shift/open", new { openingCashCents = 50000L, clientRequestId = Guid.NewGuid().ToString("D") }),
                    HttpStatusCode.OK,
                    "re-open shift for order hardening");

                Guid createOrdOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, "/api/cafe/orders/revision", createOrdOpId, new
                    {
                        operationId = createOrdOpId,
                        orderId,
                        expectedVersion = 0,
                        serviceMode = "DineIn",
                        notes = "INT-09 order",
                        lines = new[] { new { orderLineId = Guid.NewGuid(), productId = C4ProofIds.ProductId, quantityX1000 = 1000L, displayOrder = 0 } }
                    }),
                    HttpStatusCode.OK,
                    "create INT-09 order");

                // Stale positive expectedVersion (99 instead of 1) MUST fail with 409.
                Guid staleSubOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/submit", staleSubOpId, new
                    {
                        operationId = staleSubOpId,
                        expectedVersion = 99
                    }),
                    HttpStatusCode.Conflict,
                    "stale expectedVersion rejected");

                // Valid Submit -> Preparing -> Ready.
                Guid subOpId = Guid.NewGuid();
                var subOrd = await Read(await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/submit", subOpId, new { operationId = subOpId, expectedVersion = 1 }), HttpStatusCode.OK, "submit order");
                int v = subOrd.GetProperty("orderVersion").GetInt32();

                Guid prepOpId = Guid.NewGuid();
                var prepOrd = await Read(await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/preparation", prepOpId, new { operationId = prepOpId, expectedVersion = v, targetState = "Preparing" }), HttpStatusCode.OK, "order Preparing");
                v = prepOrd.GetProperty("orderVersion").GetInt32();

                Guid readyOpId = Guid.NewGuid();
                var readyOrd = await Read(await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/preparation", readyOpId, new { operationId = readyOpId, expectedVersion = v, targetState = "Ready" }), HttpStatusCode.OK, "order Ready");
                v = readyOrd.GetProperty("orderVersion").GetInt32();

                // Unsupported tender method ("Card") MUST be rejected (400) — Cash/AR/Partial only!
                Guid badTenderOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/complete", badTenderOpId, new
                    {
                        completionOperationId = badTenderOpId,
                        saleCommittedOperationId = Guid.NewGuid(),
                        expectedVersion = v,
                        payments = new[] { new { method = "Card", amountCents = 500L } }
                    }),
                    HttpStatusCode.BadRequest,
                    "unsupported Card tender rejected");

                // Revoke Orders.Complete from Cashier and verify Cashier is denied completion (403).
                db.Execute(
                    "DELETE FROM role_permissions WHERE role_id=$role AND permission=$perm;",
                    ("$role", DefaultSeedData.CashierRoleId),
                    ("$perm", DefaultSeedData.Permissions.OrdersComplete));
                Guid revokedOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(cashierHttp, $"/api/cafe/orders/{orderId:D}/complete", revokedOpId, new
                    {
                        completionOperationId = revokedOpId,
                        saleCommittedOperationId = Guid.NewGuid(),
                        expectedVersion = v,
                        payments = new[] { new { method = "Cash", amountCents = 500L } }
                    }),
                    HttpStatusCode.Forbidden,
                    "revoked permission denied order completion");

                // Owner completes order with Cash.
                savedCompleteBody = new
                {
                    completionOperationId = completeOpId,
                    saleCommittedOperationId = Guid.NewGuid(),
                    expectedVersion = v,
                    payments = new[] { new { method = "Cash", amountCents = 500L } }
                };
                var completed = await Read(
                    await PostIdem(ownerHttp, $"/api/cafe/orders/{orderId:D}/complete", completeOpId, savedCompleteBody),
                    HttpStatusCode.OK,
                    "complete order with Cash");
                int completedVersion = completed.GetProperty("orderVersion").GetInt32();

                // Attempting to re-open / revise a Completed order MUST fail with 409!
                Guid reopenOpId = Guid.NewGuid();
                await Read(
                    await PostIdem(ownerHttp, "/api/cafe/orders/revision", reopenOpId, new
                    {
                        operationId = reopenOpId,
                        orderId,
                        expectedVersion = completedVersion,
                        serviceMode = "DineIn",
                        notes = "Attempted reopen of Completed order",
                        lines = new[] { new { orderLineId = Guid.NewGuid(), productId = C4ProofIds.ProductId, quantityX1000 = 2000L, displayOrder = 0 } }
                    }),
                    HttpStatusCode.Conflict,
                    "completed order cannot be re-opened or revised");

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

                var (_, restartCompleteHdr) = await ReadWithHeader(
                    await PostIdem(http, $"/api/cafe/orders/{orderId:D}/complete", completeOpId, savedCompleteBody!),
                    HttpStatusCode.OK,
                    "restart replay order completion");
                Check(restartCompleteHdr, "restart order completion replay signaled");

                Check(db.ScalarLong("SELECT COUNT(*) FROM cash_drawer_reviews WHERE cashier_session_id=$id AND decision='Acknowledged';", ("$id", closedShiftId.ToString("D"))) == 1, "1 acknowledged variance review in SQLite");
                Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_orders WHERE order_id=$id AND state='Completed';", ("$id", orderId.ToString("D"))) == 1, "1 completed order in SQLite");

                await restarted.StopAsync();
            }

            Console.WriteLine($"PASS: {checks} R3 Cash Drawer & Order Hardening HTTP/SQLite/restart assertions (INT-07/08/09); DEBUG_DEVELOPMENT_LICENSE; not Release acceptance. Root: {root}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", priorDevMode);
        }
#endif
    }
}

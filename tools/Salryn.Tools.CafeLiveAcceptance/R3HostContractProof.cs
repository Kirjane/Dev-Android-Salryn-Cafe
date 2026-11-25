using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;

internal static class R3HostContractProof
{
    public static async Task RunAsync()
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        string? priorDevMode = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        string root = Path.Combine(Path.GetTempPath(), "salryn-r3-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = R3LiveProofFixture.Create(root);
            var db = fixture.Db;
            string nonce = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");

            int checks = 0;
            void Check(bool ok, string name)
            {
                if (!ok) throw new InvalidOperationException("R3 contract assertion failed: " + name + "; root: " + root);
                checks++;
            }

            async Task<(JsonElement Body, bool ReplayedHeader)> ReadWithHeader(HttpResponseMessage response, HttpStatusCode expected, string name)
            {
                string raw = await response.Content.ReadAsStringAsync();
                Check(response.StatusCode == expected, name + " HTTP " + (int)response.StatusCode + ": " + raw);
                bool replayed = response.Headers.TryGetValues("X-Idempotency-Replayed", out var vals) &&
                    vals.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
                using var json = JsonDocument.Parse(raw);
                return (json.RootElement.Clone(), replayed);
            }

            async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected, string name)
            {
                var (body, _) = await ReadWithHeader(response, expected, name);
                return body;
            }

            Guid ingredient = C4ProofIds.ItemIngredientId;
            long Stock() => db.ScalarLong("SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;", ("$id", ingredient.ToString("D")));

            Guid adjustmentId = Guid.NewGuid();
            var adjustment = new
            {
                operationId = Guid.NewGuid(),
                adjustmentEventId = adjustmentId,
                stockTargetType = "Ingredient",
                stockTargetId = ingredient,
                quantityDeltaX1000 = 2000L,
                reason = "Synthetic R3 adjustment",
                occurredAtUtc = DateTimeOffset.UtcNow
            };

            Guid countId = Guid.NewGuid();
            Guid draftOp = Guid.NewGuid();
            Guid countLineId = Guid.NewGuid();
            object? draft = null;
            var finalize = new { operationId = Guid.NewGuid(), finalizedAtUtc = DateTimeOffset.UtcNow };
            string finalizePath = $"/api/cafe/inventory/counts/{countId:D}/finalize";
            string countPath = $"/api/cafe/inventory/counts/{countId:D}";

            async Task<HttpResponseMessage> PostDraft(HttpClient http, Guid opId, object payload)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/cafe/inventory/counts/draft");
                request.Headers.Add("Idempotency-Key", opId.ToString("D"));
                request.Content = JsonContent.Create(payload);
                return await http.SendAsync(request);
            }

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
                Uri baseUri = new(host.Urls.Single());
                using var anonHttp = new HttpClient { BaseAddress = baseUri };
                using var ownerHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = baseUri };
                using var reportsOnlyHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = baseUri };
                using var cashierHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = baseUri };

                var identity = await Read(await anonHttp.GetAsync("/api/test/host-identity"), HttpStatusCode.OK, "identity");
                Check(identity.GetProperty("testHostNonce").GetString() == nonce, "disposable host nonce");
                Check((await anonHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.TodayStartManila, fixture.TodayStartManila.AddDays(1)))).StatusCode == HttpStatusCode.Unauthorized, "report anonymous denied");
                Check((await anonHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", adjustment)).StatusCode == HttpStatusCode.Unauthorized, "adjust anonymous denied");

                await R3LiveProofFixture.LoginAsync(ownerHttp, R3LiveProofFixture.OwnerUsername, R3LiveProofFixture.OwnerPassword);
                await R3LiveProofFixture.LoginAsync(reportsOnlyHttp, R3LiveProofFixture.ReportsOnlyUsername, R3LiveProofFixture.ReportsOnlyPassword);
                await R3LiveProofFixture.LoginAsync(cashierHttp, R3LiveProofFixture.CashierUsername, R3LiveProofFixture.CashierPassword);

                // Seed nonzero multi-period sales, partial refund, purchase receipt (real COGS), expense, and waste via real Host HTTP endpoints.
                await fixture.SeedLiveFinancialHistoryAsync(ownerHttp);

                // RBAC 403 checks for Cashier (no Reports.View, no Inventory.Adjust/Count).
                Check((await cashierHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.TodayStartManila, fixture.TodayStartManila.AddDays(1)))).StatusCode == HttpStatusCode.Forbidden, "cashier report denied 403");
                Check((await cashierHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", adjustment)).StatusCode == HttpStatusCode.Forbidden, "cashier adjust denied 403");
                Check((await PostDraft(cashierHttp, Guid.NewGuid(), new
                {
                    countSessionId = Guid.NewGuid(),
                    startedAtUtc = DateTimeOffset.UtcNow,
                    notes = "Denied draft",
                    lines = new[] { new { lineId = Guid.NewGuid(), stockTargetType = "Ingredient", stockTargetId = ingredient, expectedBeforeX1000 = Stock(), countedX1000 = Stock() } }
                })).StatusCode == HttpStatusCode.Forbidden, "cashier count draft denied 403");

                // Scoped Manila daily / weekly / monthly nonzero financial assertions for Owner.
                var daily = await Read(await ownerHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.TodayStartManila, fixture.TodayStartManila.AddDays(1))), HttpStatusCode.OK, "owner daily report");
                var weekly = await Read(await ownerHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.WeekStartManila, fixture.WeekStartManila.AddDays(7))), HttpStatusCode.OK, "owner weekly report");
                var monthly = await Read(await ownerHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.MonthStartManila, fixture.MonthStartManila.AddMonths(1))), HttpStatusCode.OK, "owner monthly report");

                Check(monthly.GetProperty("generatedAt").GetDateTimeOffset() < fixture.MonthStartManila.AddMonths(1), "top-level generatedAt is actual clock, not future month-end");

                var dailyCafe = daily.GetProperty("cafeManagement");
                var weeklyCafe = weekly.GetProperty("cafeManagement");
                var monthlyCafe = monthly.GetProperty("cafeManagement");

                Check(dailyCafe.GetProperty("rangeStartUtc").GetDateTimeOffset() == fixture.TodayStartManila && dailyCafe.GetProperty("rangeEndUtc").GetDateTimeOffset() == fixture.TodayStartManila.AddDays(1), "daily exact range");
                Check(weeklyCafe.GetProperty("rangeStartUtc").GetDateTimeOffset() == fixture.WeekStartManila && weeklyCafe.GetProperty("rangeEndUtc").GetDateTimeOffset() == fixture.WeekStartManila.AddDays(7), "weekly exact range");
                Check(monthlyCafe.GetProperty("rangeStartUtc").GetDateTimeOffset() == fixture.MonthStartManila && monthlyCafe.GetProperty("rangeEndUtc").GetDateTimeOffset() == fixture.MonthStartManila.AddMonths(1), "monthly exact range");

                Check(dailyCafe.GetProperty("sales").GetProperty("recognizedCompletedSaleRevenueCents").GetInt64() == fixture.ExpectedDailyCompletedRevenueCents, "daily completed sales revenue");
                Check(dailyCafe.GetProperty("sales").GetProperty("committedRefundCents").GetInt64() == fixture.ExpectedDailyRefundCents, "daily committed refund");
                Check(dailyCafe.GetProperty("profitability").GetProperty("recognizedNetRevenueCents").GetInt64() == fixture.ExpectedDailyNetRevenueCents, "daily net revenue");
                Check(weeklyCafe.GetProperty("profitability").GetProperty("recognizedNetRevenueCents").GetInt64() == fixture.ExpectedWeeklyNetRevenueCents, "weekly net revenue");
                Check(monthlyCafe.GetProperty("profitability").GetProperty("recognizedNetRevenueCents").GetInt64() == fixture.ExpectedMonthlyNetRevenueCents, "monthly net revenue");
                Check(dailyCafe.GetProperty("profitability").GetProperty("historicalCogsCents").GetInt64() > 0, "daily nonzero historical COGS");
                Check(dailyCafe.GetProperty("profitability").GetProperty("operationalExpenseCents").GetInt64() == 150, "daily operational expense");
                Check(dailyCafe.GetProperty("profitability").GetProperty("recognizedWasteCostCents").GetInt64() == 200, "daily recognized waste cost");
                Check(dailyCafe.GetProperty("waste").GetProperty("costCoverage").GetProperty("state").GetString() == "Incomplete", "uncosted oat milk waste marks waste coverage Incomplete");
                Check(dailyCafe.GetProperty("profitability").GetProperty("authority").GetString() != "Authoritative", "provisional profitability disclosed");

                // Recipe costing rows include both Complete (Espresso Base) and Incomplete (Oat Latte Special).
                var recipes = monthlyCafe.GetProperty("recipeCosts").EnumerateArray().ToArray();
                Check(recipes.Length == 2, "two published recipes in recipeCosts");
                Check(recipes.Any(r => r.GetProperty("recipeName").GetString() == "Espresso Base" && r.GetProperty("costCoverage").GetString() == "Complete" && r.GetProperty("knownCostCents").GetInt64() == 200), "complete recipe cost matches purchase cost");
                Check(recipes.Any(r => r.GetProperty("recipeName").GetString() == "Oat Latte Special" && r.GetProperty("costCoverage").GetString() == "Incomplete"), "incomplete recipe cost disclosed");

                // Owner has unredacted Cash & AR.
                Check(dailyCafe.GetProperty("cash").ValueKind == JsonValueKind.Object && dailyCafe.GetProperty("cash").GetProperty("saleCashReceiptCents").GetInt64() == 600, "owner cash unredacted");
                Check(dailyCafe.GetProperty("accountsReceivable").ValueKind == JsonValueKind.Object && dailyCafe.GetProperty("accountsReceivable").GetProperty("outstandingBalanceCents").GetInt64() == 400, "owner AR unredacted");

                // Narrow Reports.View actor without Receivables.View / Inventory.View gets strict redaction + 403 on low-stock.
                var redacted = await Read(await reportsOnlyHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.TodayStartManila, fixture.TodayStartManila.AddDays(1))), HttpStatusCode.OK, "reports-only daily report");
                var redactedCafe = redacted.GetProperty("cafeManagement");
                Check(redacted.GetProperty("receivables").ValueKind == JsonValueKind.Null, "top-level receivables redacted");
                Check(redactedCafe.GetProperty("cash").ValueKind == JsonValueKind.Null, "cafeManagement.cash redacted");
                Check(redactedCafe.GetProperty("accountsReceivable").ValueKind == JsonValueKind.Null, "cafeManagement.accountsReceivable redacted");
                Check(redactedCafe.GetProperty("cashierSessions").ValueKind == JsonValueKind.Null, "cafeManagement.cashierSessions redacted");
                Check(redactedCafe.GetProperty("profitability").GetProperty("recognizedNetRevenueCents").GetInt64() == fixture.ExpectedDailyNetRevenueCents, "reports-only still sees profitability");
                Check((await reportsOnlyHttp.GetAsync("/api/cafe/inventory/low-stock")).StatusCode == HttpStatusCode.Forbidden, "reports-only low-stock denied 403");

                Check((await ownerHttp.GetAsync(R3LiveProofFixture.ReportPath(fixture.TodayStartManila, fixture.TodayStartManila))).StatusCode == HttpStatusCode.BadRequest, "invalid report range denied");

                // Inventory adjustment: commit, replay, conflicting payload 409, negative-stock 409.
                long stockBeforeAdjust = Stock();
                var adj1 = await Read(await ownerHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", adjustment), HttpStatusCode.OK, "adjust");
                Check(adj1.GetProperty("isReplay").GetBoolean() == false && adj1.GetProperty("evidence").GetProperty("nameSnapshot").GetString() == "Espresso Beans", "adjust evidence");
                var (adjReplay, adjReplayedHdr) = await ReadWithHeader(await ownerHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", adjustment), HttpStatusCode.OK, "adjust replay");
                Check(adjReplayedHdr || adjReplay.GetProperty("isReplay").GetBoolean(), "adjust replay signaled");
                Check(Stock() == stockBeforeAdjust + 2000L, "adjust once");

                var differentAdj = new
                {
                    adjustment.operationId,
                    adjustment.adjustmentEventId,
                    adjustment.stockTargetType,
                    adjustment.stockTargetId,
                    quantityDeltaX1000 = 3000L,
                    adjustment.reason,
                    adjustment.occurredAtUtc
                };
                Check((await ownerHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", differentAdj)).StatusCode == HttpStatusCode.Conflict, "changed adjustment replay rejected");

                var negativeAdj = new
                {
                    operationId = Guid.NewGuid(),
                    adjustmentEventId = Guid.NewGuid(),
                    stockTargetType = "Ingredient",
                    stockTargetId = ingredient,
                    quantityDeltaX1000 = -(Stock() + 5000L),
                    reason = "Impossible negative stock",
                    occurredAtUtc = DateTimeOffset.UtcNow
                };
                var negErr = await Read(await ownerHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", negativeAdj), HttpStatusCode.Conflict, "negative stock adjustment denied");
                Check(negErr.GetProperty("code").GetString() == "CafeInventory.InsufficientStock", "negative stock error code");

                // Physical count: stale capture 409 -> stale finalize 409 -> valid draft & replay -> valid finalize & replay -> finalized immutability 409.
                long stockBeforeCount = Stock();
                var staleCapture = await Read(await PostDraft(ownerHttp, Guid.NewGuid(), new
                {
                    countSessionId = Guid.NewGuid(),
                    startedAtUtc = DateTimeOffset.UtcNow,
                    notes = "Stale capture draft",
                    lines = new[] { new { lineId = Guid.NewGuid(), stockTargetType = "Ingredient", stockTargetId = ingredient, expectedBeforeX1000 = stockBeforeCount - 500L, countedX1000 = stockBeforeCount - 1000L } }
                }), HttpStatusCode.Conflict, "stale capture draft rejected");
                Check(staleCapture.GetProperty("code").GetString() == "CafeInventory.CountCaptureStale", "stale capture error code");

                // Create a draft, then move stock underneath it to prove CafeInventory.CountStale on finalize!
                Guid staleCountId = Guid.NewGuid();
                await Read(await PostDraft(ownerHttp, Guid.NewGuid(), new
                {
                    countSessionId = staleCountId,
                    startedAtUtc = DateTimeOffset.UtcNow,
                    notes = "Will go stale before finalize",
                    lines = new[] { new { lineId = Guid.NewGuid(), stockTargetType = "Ingredient", stockTargetId = ingredient, expectedBeforeX1000 = stockBeforeCount, countedX1000 = stockBeforeCount - 1000L } }
                }), HttpStatusCode.OK, "draft for stale finalize proof");

                await Read(await ownerHttp.PostAsJsonAsync("/api/cafe/inventory/adjust", new
                {
                    operationId = Guid.NewGuid(),
                    adjustmentEventId = Guid.NewGuid(),
                    stockTargetType = "Ingredient",
                    stockTargetId = ingredient,
                    quantityDeltaX1000 = 1000L,
                    reason = "Concurrent stock movement",
                    occurredAtUtc = DateTimeOffset.UtcNow
                }), HttpStatusCode.OK, "concurrent adjustment before finalize");

                var staleFinalizeErr = await Read(
                    await ownerHttp.PostAsJsonAsync($"/api/cafe/inventory/counts/{staleCountId:D}/finalize", new { operationId = Guid.NewGuid(), finalizedAtUtc = DateTimeOffset.UtcNow }),
                    HttpStatusCode.Conflict,
                    "stale count finalize rejected");
                Check(staleFinalizeErr.GetProperty("code").GetString() == "CafeInventory.CountStale", "stale finalize error code");

                // Valid draft -> replay -> finalize -> replay -> immutability checks.
                long stockForValidCount = Stock();
                long targetCountedStock = stockForValidCount - 3000L;
                draft = new
                {
                    countSessionId = countId,
                    startedAtUtc = DateTimeOffset.UtcNow,
                    notes = "Synthetic R3 physical count",
                    lines = new[]
                    {
                        new
                        {
                            lineId = countLineId,
                            stockTargetType = "Ingredient",
                            stockTargetId = ingredient,
                            expectedBeforeX1000 = stockForValidCount,
                            countedX1000 = targetCountedStock
                        }
                    }
                };
                await Read(await PostDraft(ownerHttp, draftOp, draft), HttpStatusCode.OK, "draft");
                await Read(await PostDraft(ownerHttp, draftOp, draft), HttpStatusCode.OK, "draft replay");
                Check(Stock() == stockForValidCount, "draft does not change stock");

                var fin1 = await Read(await ownerHttp.PostAsJsonAsync(finalizePath, finalize), HttpStatusCode.OK, "finalize");
                Check(fin1.GetProperty("isReplay").GetBoolean() == false && fin1.GetProperty("evidence").GetProperty("state").GetString() == "Finalized", "finalize committed");
                var (finReplay, finReplayedHdr) = await ReadWithHeader(await ownerHttp.PostAsJsonAsync(finalizePath, finalize), HttpStatusCode.OK, "finalize replay");
                Check(finReplayedHdr || finReplay.GetProperty("isReplay").GetBoolean(), "finalize replay signaled");
                Check(Stock() == targetCountedStock, "count applied once");

                var editFinalizedErr = await Read(await PostDraft(ownerHttp, Guid.NewGuid(), draft), HttpStatusCode.Conflict, "overwrite finalized draft denied");
                Check(editFinalizedErr.GetProperty("code").GetString() == "CafeInventory.CountFinalized", "finalized draft immutability code");

                var refinalizeErr = await Read(
                    await ownerHttp.PostAsJsonAsync(finalizePath, new { operationId = Guid.NewGuid(), finalizedAtUtc = DateTimeOffset.UtcNow }),
                    HttpStatusCode.Conflict,
                    "re-finalize with new operationId denied");
                Check(refinalizeErr.GetProperty("code").GetString() == "CafeInventory.CountAlreadyFinalized", "already finalized code");

                await host.StopAsync();
            }

            long expectedStockAfterRestart = Stock();
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

                var (restartAdj, restartAdjHdr) = await ReadWithHeader(await http.PostAsJsonAsync("/api/cafe/inventory/adjust", adjustment), HttpStatusCode.OK, "restart adjustment replay");
                Check(restartAdjHdr || restartAdj.GetProperty("isReplay").GetBoolean(), "restart adjustment replay signaled");
                var (restartFin, restartFinHdr) = await ReadWithHeader(await http.PostAsJsonAsync(finalizePath, finalize), HttpStatusCode.OK, "restart finalize replay");
                Check(restartFinHdr || restartFin.GetProperty("isReplay").GetBoolean(), "restart finalize replay signaled");
                var count = await Read(await http.GetAsync(countPath), HttpStatusCode.OK, "read finalized count");
                Check(count.GetProperty("state").GetString() == "Finalized", "finalized count persisted");
                Check(Stock() == expectedStockAfterRestart, "restart did not duplicate stock");
                Check(db.ScalarLong("SELECT COUNT(*) FROM inventory_adjustment_events WHERE adjustment_event_id=$id;", ("$id", adjustmentId.ToString("D"))) == 1, "one adjustment evidence");
                Check(db.ScalarLong("SELECT COUNT(*) FROM inventory_count_sessions WHERE count_session_id=$id AND state='Finalized';", ("$id", countId.ToString("D"))) == 1, "one finalized count row");
                Check(db.ScalarLong("SELECT COUNT(*) FROM sales WHERE status=$completed;", ("$completed", (int)Salryn.Core.Models.SaleStatus.Completed)) >= 1, "completed sales persisted");
                Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_sale_refunds WHERE state='Committed';") == 1, "committed refund persisted");
                await restarted.StopAsync();
            }

            Console.WriteLine($"PASS: {checks} R3 authenticated HTTP / SQLite assertions (nonzero reports, redaction, RBAC, stale-count & immutability, restart replay); DEBUG_DEVELOPMENT_LICENSE; not Release acceptance. Root: {root}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", priorDevMode);
        }
#endif
    }
}

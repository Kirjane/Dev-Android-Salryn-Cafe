using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Security;

internal static class R3HostContractProof
{
    public static async Task RunAsync()
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        var root = Path.Combine(Path.GetTempPath(), "salryn-r3-contract-" + Guid.NewGuid().ToString("N"));
        var db = C4ProofDatabase.Create(root);
        const string password = "Synthetic-R3-Contract!2026";
        db.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(password).Value), ("$id", C4ProofIds.ActorUserId.ToString("D")));
        var nonce = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("R3 contract assertion failed: " + name + "; root: " + root);
            checks++;
        }
        async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected, string name)
        {
            var raw = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == expected, name + " HTTP " + (int)response.StatusCode + ": " + raw);
            using var json = JsonDocument.Parse(raw);
            return json.RootElement.Clone();
        }
        var ingredient = C4ProofIds.ItemIngredientId;
        var adjustmentId = Guid.NewGuid();
        var adjustment = new { operationId = Guid.NewGuid(), adjustmentEventId = adjustmentId,
            stockTargetType = "Ingredient", stockTargetId = ingredient, quantityDeltaX1000 = 2000L,
            reason = "Synthetic R3 adjustment", occurredAtUtc = DateTimeOffset.UtcNow };
        var countId = Guid.NewGuid();
        var draftOp = Guid.NewGuid();
        var draft = new { countSessionId = countId, startedAtUtc = DateTimeOffset.UtcNow,
            notes = "Synthetic R3 physical count", lines = new[] { new { lineId = Guid.NewGuid(),
                stockTargetType = "Ingredient", stockTargetId = ingredient, expectedBeforeX1000 = 102000L, countedX1000 = 99000L } } };
        var finalize = new { operationId = Guid.NewGuid(), finalizedAtUtc = DateTimeOffset.UtcNow };
        long Stock() => db.ScalarLong("SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;", ("$id", ingredient.ToString("D")));
        async Task<HttpResponseMessage> PostDraft(HttpClient http)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/cafe/inventory/counts/draft");
            request.Headers.Add("Idempotency-Key", draftOp.ToString("D"));
            request.Content = JsonContent.Create(draft);
            return await http.SendAsync(request);
        }
        async Task Authenticate(HttpClient http)
        {
            await Read(await http.PostAsJsonAsync("/api/auth/login", new { username = "c4-proof", password }), HttpStatusCode.OK, "login");
        }
        string finalizePath = $"/api/cafe/inventory/counts/{countId:D}/finalize";
        string countPath = $"/api/cafe/inventory/counts/{countId:D}";
        string reportPath(DateTimeOffset from, DateTimeOffset to) =>
            "/api/owner-reports/summary?fromUtc=" + Uri.EscapeDataString(from.ToString("O")) + "&toUtc=" + Uri.EscapeDataString(to.ToString("O"));
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var midnight = new DateTimeOffset(today.Year,today.Month,today.Day,0,0,0,today.Offset);
        await using (var host = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
            EnableStaticWebAssets = false, WriteDevelopmentDatabasePath = false
        }))
        {
            await host.StartAsync();
            using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(host.Urls.Single()) };
            var identity = await Read(await http.GetAsync("/api/test/host-identity"),HttpStatusCode.OK,"identity");
            Check(identity.GetProperty("testHostNonce").GetString() == nonce, "disposable host nonce");
            Check((await http.GetAsync(reportPath(midnight,midnight.AddDays(1)))).StatusCode == HttpStatusCode.Unauthorized,"report anonymous denied");
            await Authenticate(http);
            var week = midnight.AddDays(-(((int)midnight.DayOfWeek+6)%7));
            var month = new DateTimeOffset(today.Year,today.Month,1,0,0,0,today.Offset);
            foreach (var (from,to) in new[] { (midnight,midnight.AddDays(1)),(week,week.AddDays(7)),(month,month.AddMonths(1)) })
            {
                var result = await Read(await http.GetAsync(reportPath(from,to)),HttpStatusCode.OK,"scoped report");
                var cafe = result.GetProperty("cafeManagement");
                Check(cafe.GetProperty("rangeStartUtc").GetDateTimeOffset() == from && cafe.GetProperty("rangeEndUtc").GetDateTimeOffset() == to,"exact report range");
                Check(cafe.GetProperty("profitability").GetProperty("recognizedNetRevenueCents").GetInt64() == 0,"empty fixture revenue");
            }
            Check((await http.GetAsync(reportPath(midnight,midnight))).StatusCode == HttpStatusCode.BadRequest,"invalid report range denied");
            Check(Stock()==100000,"seed stock");
            await Read(await http.PostAsJsonAsync("/api/cafe/inventory/adjust",adjustment),HttpStatusCode.OK,"adjust");
            await Read(await http.PostAsJsonAsync("/api/cafe/inventory/adjust",adjustment),HttpStatusCode.OK,"adjust replay");
            Check(Stock()==102000,"adjust once");
            var different = new { adjustment.operationId, adjustment.adjustmentEventId, adjustment.stockTargetType, adjustment.stockTargetId,
                quantityDeltaX1000 = 3000L, adjustment.reason, adjustment.occurredAtUtc };
            Check((await http.PostAsJsonAsync("/api/cafe/inventory/adjust",different)).StatusCode==HttpStatusCode.Conflict,"changed adjustment replay rejected");
            await Read(await PostDraft(http),HttpStatusCode.OK,"draft");
            await Read(await PostDraft(http),HttpStatusCode.OK,"draft replay");
            Check(Stock()==102000,"draft does not change stock");
            await Read(await http.PostAsJsonAsync(finalizePath,finalize),HttpStatusCode.OK,"finalize");
            await Read(await http.PostAsJsonAsync(finalizePath,finalize),HttpStatusCode.OK,"finalize replay");
            Check(Stock()==99000,"count applied once");
            await host.StopAsync();
        }
        await using (var restarted = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
            EnableStaticWebAssets = false, WriteDevelopmentDatabasePath = false
        }))
        {
            await restarted.StartAsync();
            using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(restarted.Urls.Single()) };
            await Authenticate(http);
            await Read(await http.PostAsJsonAsync("/api/cafe/inventory/adjust",adjustment),HttpStatusCode.OK,"restart adjustment replay");
            await Read(await http.PostAsJsonAsync(finalizePath,finalize),HttpStatusCode.OK,"restart finalize replay");
            var count = await Read(await http.GetAsync(countPath),HttpStatusCode.OK,"read finalized count");
            Check(count.GetProperty("state").GetString()=="Finalized","finalized count persisted");
            Check(Stock()==99000,"restart did not duplicate stock");
            Check(db.ScalarLong("SELECT COUNT(*) FROM inventory_adjustment_events WHERE adjustment_event_id=$id;",("$id",adjustmentId.ToString("D")))==1,"one adjustment evidence");
            await restarted.StopAsync();
        }
        Console.WriteLine($"PASS: {checks} R3 authenticated HTTP / SQLite assertions; DEBUG_DEVELOPMENT_LICENSE; not browser or Release acceptance. Root: {root}");
#endif
    }
}

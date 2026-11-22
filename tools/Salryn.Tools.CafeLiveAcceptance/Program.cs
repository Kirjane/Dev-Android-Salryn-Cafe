using System.Diagnostics;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;

#if !DEBUG
throw new InvalidOperationException("This disposable acceptance runner requires Debug. Release licensing is a separate authorized-fixture gate.");
#else
string root = Path.Combine(Path.GetTempPath(), "salryn-cafe-live-" + Guid.NewGuid().ToString("N"));
var db = C4ProofDatabase.Create(root);
const string password = "Synthetic-Cafe-Live!2026";
db.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",
    ("$hash", SalrynPasswordHasher.HashPassword(password).Value), ("$id", C4ProofIds.ActorUserId.ToString("D")));
Guid customer = Guid.NewGuid();
db.Execute("INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Synthetic Cafe Account',1,$actor,$at,$at);",
    ("$id", customer.ToString("D")), ("$actor", C4ProofIds.ActorUserId.ToString("D")), ("$at", DateTimeOffset.UtcNow.ToString("O")));

// Seed Receipt + Kitchen logical print stations and draft menu variant for live B1/B2/B3 acceptance.
var seedComp = SalrynLocalComposition.Create(db.Paths);
var printService = new SqliteCafePrintService(db.Factory);
var receiptStationRes = printService.PublishStation(new CafePrinterStationMutation(
    Guid.NewGuid(), Guid.NewGuid(), C4ProofIds.StoreId, C4ProofIds.ActorUserId,
    "Counter Receipt", CafePrinterStationKind.CustomerReceipt, "front-counter", true, 0, DateTimeOffset.UtcNow));
if (receiptStationRes.IsFailure) throw new InvalidOperationException("Seed receipt station failed: " + receiptStationRes.ErrorDetail.Message);
var kitchenStationRes = printService.PublishStation(new CafePrinterStationMutation(
    Guid.NewGuid(), Guid.NewGuid(), C4ProofIds.StoreId, C4ProofIds.ActorUserId,
    "Barista Kitchen", CafePrinterStationKind.Kitchen, "espresso-bar", true, 0, DateTimeOffset.UtcNow));
if (kitchenStationRes.IsFailure) throw new InvalidOperationException("Seed kitchen station failed: " + kitchenStationRes.ErrorDetail.Message);
var draftMenu = seedComp.MenuCatalog.GetDraft();
if (draftMenu.IsSuccess)
{
    foreach (var item in draftMenu.Value.Items.Where(i => i.IsActive))
    {
        if (!draftMenu.Value.Variants.Any(v => v.ProductId == item.ProductId && v.IsActive))
        {
            var savedVar = seedComp.MenuCatalog.SaveVariant(new MenuVariantMutation(
                null, item.ProductId, "Regular", $"LIVE-{item.ProductId:N}"[..16], null,
                item.SellingPriceCents, item.CostPriceCents, true, false, 1));
            if (savedVar.IsFailure) throw new InvalidOperationException("Seed draft variant failed: " + savedVar.ErrorDetail.Message);
        }
    }
}
string nonce = Guid.NewGuid().ToString("N");
Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");
await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
{
    Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
    EnableStaticWebAssets = true, WriteDevelopmentDatabasePath = false
});
await host.StartAsync();
string url = host.Urls.Single();
var start = new ProcessStartInfo("node") { UseShellExecute = false };
start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "browser.mjs"));
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Salryn.sln"))) repo = repo.Parent;
if (repo is null) throw new InvalidOperationException("Repository root required.");
start.Environment["CAFE_QA_PLAYWRIGHT_PACKAGE"] = Path.Combine(repo.FullName, "src", "Salryn.App.Web", "package.json");
start.Environment["CAFE_QA_URL"] = url;
start.Environment["CAFE_QA_NONCE"] = nonce;
start.Environment["CAFE_QA_PASSWORD"] = password;
start.Environment["CAFE_QA_ROOT"] = root;
using var browser = Process.Start(start) ?? throw new InvalidOperationException("Browser runner did not start.");
await browser.WaitForExitAsync();
await host.StopAsync();
if (browser.ExitCode != 0) throw new InvalidOperationException($"Live browser acceptance failed: {browser.ExitCode}. Synthetic artifacts retained: {root}");
// Mount a new executable Host over the exact same disposable database and replay
// the original successful completion requests through real authenticated HTTP.
await using var restarted = await LocalApiApplicationFactory.CreateAsync([], new()
{
    Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
    EnableStaticWebAssets = true, WriteDevelopmentDatabasePath = false
});
await restarted.StartAsync();
start.Environment["CAFE_QA_URL"] = restarted.Urls.Single();
start.Environment["CAFE_QA_REPLAY"] = "1";
using var replayBrowser = Process.Start(start) ?? throw new InvalidOperationException("Restart browser did not start.");
await replayBrowser.WaitForExitAsync();
await restarted.StopAsync();
if (replayBrowser.ExitCode != 0) throw new InvalidOperationException("Restart replay failed; retained synthetic evidence: " + root);
int checks = 0;
void Equal(long expected, string sql) { long actual = db.ScalarLong(sql); if (actual != expected) throw new InvalidOperationException($"Ledger assertion failed: {sql}, expected {expected}, actual {actual}"); checks++; }
Equal(3, "SELECT COUNT(*) FROM sales;");
Equal(1500, "SELECT SUM(total_cents) FROM sales;");
Equal(750, "SELECT SUM(debit_cents) FROM accounts_receivable_entries;");
Equal(7000, "SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';");
Equal(3, "SELECT COUNT(*) FROM cafe_orders WHERE state='Completed';");
Equal(3, "SELECT COUNT(*) FROM sales WHERE receipt_snapshot IS NOT NULL AND length(trim(receipt_snapshot)) > 0;");
Equal(2, "SELECT COUNT(*) FROM cafe_print_jobs;");
Equal(2, "SELECT COUNT(*) FROM cafe_print_attempts;");
Equal(1, "SELECT COUNT(*) FROM store_settings WHERE store_name='Salryn Cafe Live QA';");
if (db.ScalarLong("SELECT COUNT(*) FROM menu_revisions;") < 2) throw new InvalidOperationException("Expected published menu revision after B3 live publish.");
checks++;
Console.WriteLine($"PASS: live React/Host cashier journeys and {checks} persisted SQLite assertions. Root: {root}. DEBUG_DEVELOPMENT_LICENSE; not Release license acceptance.");
#endif

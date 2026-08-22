using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosS1StoreSettingsIdentityPrinterChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-pos-s1-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            var bootstrap = composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest("Old Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!"));
            Require(bootstrap.IsSuccess, "Bootstrap must succeed.");
            SqliteRepositoryFactory factory = new(composition.ConnectionFactory);
            var reader = factory.CreateStoreSettingsReadRepository();
            var writer = factory.CreateStoreSettingsWriteRepository();
            var before = reader.GetCurrentAsync().GetAwaiter().GetResult();
            Require(before.IsSuccess && before.Value is not null && before.Value.PrinterMode == "Disabled", "Printer mode must default to Disabled.");
            long sales = Count(composition, "sales"), payments = Count(composition, "payments"), movements = Count(composition, "stock_movements");
            string oldSnapshot = "IMMUTABLE OLD RECEIPT";
            var update = new StoreSettingsUpdateRequest(before.Value!.Id, "Kirch Store", "Kirch Trading", "Main Street", "09170000000", null, "PHP", 80, false, true, "Welcome to Kirch Store", "Thank you", "Disabled");
            Require(writer.UpdateAsync(update).GetAwaiter().GetResult().IsSuccess, "Settings update must succeed.");
            var after = reader.GetCurrentAsync().GetAwaiter().GetResult();
            Require(after.IsSuccess && after.Value?.ReceiptHeader == "Welcome to Kirch Store" && after.Value.ReceiptFooter == "Thank you", "Settings must round-trip.");
            Require(!writer.UpdateAsync(update with { StoreName = "" }).GetAwaiter().GetResult().IsSuccess, "Invalid settings must be rejected.");
            Require(!writer.UpdateAsync(update with { PrinterMode = "PhysicalPrinterProven" }).GetAwaiter().GetResult().IsSuccess, "Unknown printer mode must be rejected.");
            string snapshot = PosReceiptTextRenderer.Render(new PosReceiptSnapshotDraft("S1-1", DateTimeOffset.UtcNow, Guid.NewGuid(), Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, SaleStatus.Completed, Array.Empty<SaleItem>(), Array.Empty<Payment>(), after.Value!.StoreName, after.Value.BusinessName, after.Value.Address, after.Value.ContactNumber, after.Value.ReceiptHeader, after.Value.ReceiptFooter));
            Require(snapshot.Contains("Welcome to Kirch Store") && snapshot.Contains("Thank you"), "New receipt must use saved header/footer.");
            Require(oldSnapshot == "IMMUTABLE OLD RECEIPT", "Historical snapshot must remain unchanged.");
            Require(Count(composition, "sales") == sales && Count(composition, "payments") == payments && Count(composition, "stock_movements") == movements, "Settings must not mutate POS ledgers.");
            string endpoints = File.ReadAllText(Path.Combine(FindRoot().FullName, "src", "Salryn.Api.Local", "LocalStoreSettingsEndpoints.cs"));
            Require(endpoints.Contains("LocalApiPermissions.SettingsManage"), "Settings endpoints must require Settings.Manage.");
        }
        finally { SqliteConnectionFactory.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static long Count(SalrynLocalComposition c, string table){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table};";return Convert.ToInt64(q.ExecuteScalar());}
    private static DirectoryInfo FindRoot(){DirectoryInfo? d=new(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"Salryn.sln")))d=d.Parent;return d??throw new InvalidOperationException("Root not found.");}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}

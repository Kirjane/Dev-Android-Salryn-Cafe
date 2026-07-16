using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosPrinter1WindowsPrinterWiringChecks
{
    public static void Run()
    {
        RunSettingsAndAdapterSelectionProof();
        RunSalePrintReprintNonMutationProof();
        RunFrontendSourceGuards();
    }

    private static void RunSettingsAndAdapterSelectionProof()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-printer1-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            AdminBootstrapSummary admin = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "PRINTER1 Store", null, null, null, null, 80, "admin", "PRINTER1 Admin", "StrongAdmin123!")), "bootstrap");
            _ = admin;

            SqliteRepositoryFactory factory = new(composition.ConnectionFactory);
            IStoreSettingsReadRepository reader = factory.CreateStoreSettingsReadRepository();
            IStoreSettingsWriteRepository writer = factory.CreateStoreSettingsWriteRepository();
            StoreSettingsReadModel before = RequireValue(reader.GetCurrentAsync().GetAwaiter().GetResult(), "read default")
                ?? throw new InvalidOperationException("Default store settings must exist.");

            Require(before.PrinterMode == ReceiptPrinterModes.Disabled, "Printer mode must default to Disabled.");
            Require(before.WindowsPrinterName is null, "Windows printer name must default to null.");

            StoreSettingsUpdateRequest disabledUpdate = new(
                before.Id,
                before.StoreName,
                before.BusinessName,
                before.Address,
                before.ContactNumber,
                before.Tin,
                before.CurrencyLabel,
                before.ReceiptWidthMm,
                before.TaxModeEnabled,
                before.ShiftModeEnabled,
                before.ReceiptHeader,
                before.ReceiptFooter,
                ReceiptPrinterModes.Disabled,
                null);

            RequireSuccess(writer.UpdateAsync(disabledUpdate).GetAwaiter().GetResult(), "disabled settings update");
            StoreSettingsReadModel disabled = RequireValue(reader.GetCurrentAsync().GetAwaiter().GetResult(), "read disabled")!;
            IReceiptPrinterAdapter disabledAdapter = ReceiptPrinterAdapterFactory.Create(disabled);
            PosReceiptPrintResult disabledTestPrint = new PosReceiptPrintService(new EmptySaleReadRepository(), disabledAdapter)
                .TestPrintAsync()
                .GetAwaiter()
                .GetResult();
            Require(!disabledTestPrint.Succeeded, "Disabled test print must not report success.");
            Require(disabledTestPrint.Status == PosReceiptPrintStatuses.DisabledUnavailable, "Disabled test print must return Disabled/Unavailable.");

            StoreSettingsUpdateRequest missingPrinter = disabledUpdate with
            {
                PrinterMode = ReceiptPrinterModes.WindowsPrinter,
                WindowsPrinterName = ""
            };
            Require(!writer.UpdateAsync(missingPrinter).GetAwaiter().GetResult().IsSuccess, "WindowsPrinter settings must reject missing printer name.");

            StoreSettingsUpdateRequest windowsUpdate = disabledUpdate with
            {
                PrinterMode = ReceiptPrinterModes.WindowsPrinter,
                WindowsPrinterName = "Kirch Office Printer"
            };
            RequireSuccess(writer.UpdateAsync(windowsUpdate).GetAwaiter().GetResult(), "windows settings update");
            StoreSettingsReadModel windows = RequireValue(reader.GetCurrentAsync().GetAwaiter().GetResult(), "read windows")!;
            Require(windows.PrinterMode == ReceiptPrinterModes.WindowsPrinter, "WindowsPrinter mode must round-trip.");
            Require(windows.WindowsPrinterName == "Kirch Office Printer", "Windows printer name must round-trip.");

            NoOpReceiptPrinterAdapter? fakeWindows = null;
            IReceiptPrinterAdapter selected = ReceiptPrinterAdapterFactory.Create(windows, printerName =>
            {
                fakeWindows = new NoOpReceiptPrinterAdapter("FakeWindowsPrinter:" + printerName);
                return fakeWindows;
            });
            Require(selected.IsEnabled, "Configured WindowsPrinter mode must select an enabled adapter boundary when factory succeeds.");
            Require(ReferenceEquals(selected, fakeWindows), "Configured WindowsPrinter mode must reach the Windows adapter factory boundary.");

            PosReceiptPrintResult fakeTestPrint = new PosReceiptPrintService(new EmptySaleReadRepository(), selected)
                .TestPrintAsync()
                .GetAwaiter()
                .GetResult();
            Require(fakeTestPrint.Succeeded, "Configured fake Windows printer test print must succeed at the adapter boundary.");
            Require(fakeWindows!.LastReceiptText?.Contains("SALRYN POS TEST PRINT", StringComparison.Ordinal) == true, "Test print must send sample receipt text to adapter boundary.");

            IReceiptPrinterAdapter missingNameAdapter = ReceiptPrinterAdapterFactory.Create(windows with { WindowsPrinterName = null });
            PosReceiptPrintResult missingNamePrint = new PosReceiptPrintService(new EmptySaleReadRepository(), missingNameAdapter)
                .TestPrintAsync()
                .GetAwaiter()
                .GetResult();
            Require(!missingNamePrint.Succeeded, "WindowsPrinter mode without a name must not report success.");
            Require(missingNamePrint.Message.Contains("no Windows printer name", StringComparison.OrdinalIgnoreCase), "Missing printer name must return clear unavailable copy.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void RunSalePrintReprintNonMutationProof()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-printer1-sale-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            AdminBootstrapSummary admin = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "PRINTER1 Sale Store", null, null, null, null, 80, "admin", "PRINTER1 Admin", "StrongAdmin123!")), "bootstrap");

            var categories = RequireValue(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var brands = RequireValue(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");
            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            DateTimeOffset saleTime = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
            Product product = new()
            {
                Id = Guid.NewGuid(),
                Sku = "PRINTER1-ITEM",
                Barcode = RequireValue(Barcode.Create("9890000000010"), "barcode"),
                Name = "PRINTER1 Proof Item",
                CategoryId = categories[0].Id,
                BrandId = brands[0].Id,
                UnitId = units[0].Id,
                CostPrice = Money.FromCents(9000),
                SellingPrice = Money.FromCents(15000),
                StockOnHand = RequireValue(Quantity.FromX1000(5000), "initial stock"),
                ReorderLevel = Quantity.Zero,
                TrackStock = true,
                IsActive = true,
                CreatedAt = saleTime.AddHours(-1)
            };
            RequireSuccess(composition.ProductWriteRepository.AddAsync(product).GetAwaiter().GetResult(), "product");

            Guid saleId = Guid.NewGuid();
            Quantity sold = RequireValue(Quantity.FromX1000(2000), "sold quantity");
            const string snapshot = "SALRYN RECEIPT SNAPSHOT\nReceipt: PRINTER1-000001\nTOTAL: PHP 300.00";
            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = "PRINTER1-000001",
                SaleDate = saleTime,
                CashierUserId = admin.AdminUserId,
                Subtotal = Money.FromCents(30000),
                Discount = Money.Zero,
                Tax = Money.Zero,
                Total = Money.FromCents(30000),
                AmountPaid = Money.FromCents(50000),
                Change = Money.FromCents(20000),
                Status = SaleStatus.Completed,
                ReceiptSnapshot = snapshot,
                CreatedAt = saleTime
            };
            SaleItem item = new()
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BarcodeSnapshot = product.Barcode?.Value,
                UnitNameSnapshot = units[0].Name,
                Quantity = sold,
                UnitPrice = Money.FromCents(15000),
                CostPrice = product.CostPrice,
                Discount = Money.Zero,
                LineTotal = Money.FromCents(30000)
            };
            Payment payment = new()
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                Method = PaymentMethod.Cash,
                Amount = Money.FromCents(50000),
                PaidAt = saleTime.AddSeconds(30)
            };
            StockMovement movement = new()
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                QuantityChange = sold,
                CostPrice = product.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = saleId,
                Reason = "PRINTER1 sale before print proof",
                UserId = admin.AdminUserId,
                CreatedAt = saleTime.AddSeconds(30)
            };
            RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
                sale, new[] { item }, new[] { payment }, new[] { movement }).GetAwaiter().GetResult(), "completed sale");

            LedgerSnapshot beforeFailure = CaptureLedger(composition, product.Id, saleId);
            PosReceiptPrintResult failedPrint = new PosReceiptPrintService(composition.SaleReadRepository, new FailingReceiptPrinterAdapter())
                .PrintSaleReceiptAsync(saleId)
                .GetAwaiter()
                .GetResult();
            Require(!failedPrint.Succeeded, "Failing printer must not report success.");
            Require(failedPrint.Status == PosReceiptPrintStatuses.Failed, "Failing printer must return Failed status.");
            RequireLedgerUnchanged(composition, beforeFailure, product.Id, saleId, "failed print");

            NoOpReceiptPrinterAdapter fakePrinter = new("FakeWindowsReprint");
            LedgerSnapshot beforeReprint = CaptureLedger(composition, product.Id, saleId);
            PosReceiptPrintResult reprint = new PosReceiptPrintService(composition.SaleReadRepository, fakePrinter)
                .PrintSaleReceiptAsync(saleId)
                .GetAwaiter()
                .GetResult();
            Require(reprint.Succeeded, "Reprint through fake adapter must succeed.");
            Require(reprint.Source == PosReceiptPrintSources.PersistedReceiptSnapshot, "Reprint must use persisted receipt snapshot source.");
            Require(fakePrinter.LastReceiptText == snapshot, "Reprint must send persisted snapshot text exactly.");
            RequireLedgerUnchanged(composition, beforeReprint, product.Id, saleId, "successful reprint");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void RunFrontendSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string web = Path.Combine(root.FullName, "src", "Salryn.App.Web", "src");
        string settingsPage = File.ReadAllText(Path.Combine(web, "pages", "StoreSettingsPage.tsx"));
        string settingsApi = File.ReadAllText(Path.Combine(web, "api", "storeSettings.ts"));
        string checkoutPage = File.ReadAllText(Path.Combine(web, "pages", "CheckoutPage.tsx"));
        string salesPage = File.ReadAllText(Path.Combine(web, "pages", "SalesHistoryPage.tsx"));
        string windowsAdapter = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Core", "Services", "WindowsQueueReceiptPrinterAdapter.cs"));
        string printerSource = settingsPage + "\n" + settingsApi + "\n" + checkoutPage + "\n" + salesPage;

        RequireContains(settingsPage, "Windows Printer", "Store Settings must expose Windows Printer mode.");
        RequireContains(settingsPage, "Windows printer name", "Store Settings must expose Windows printer name field.");
        RequireContains(settingsPage, "Exact Windows printer name", "Store Settings must request the exact configured Windows printer name.");
        RequireContains(settingsPage, "Leave printing disabled if it is not configured", "Store Settings must keep unconfigured printing safely disabled.");
        RequireContains(settingsPage, "Saved receipts remain available when printing is disabled", "Store Settings must preserve receipt access when printing is disabled.");
        RequireContains(settingsApi, "windowsPrinterName", "Store settings API client must carry Windows printer name.");
        RequireContains(windowsAdapter, "ArgumentList.Add", "Windows adapter must pass print arguments without manual command-line concatenation.");
        RequireNotContains(windowsAdapter, "Arguments =", "Windows adapter must not concatenate caller-influenced printer names into a command line.");
        RequireContains(checkoutPage, "Print Receipt", "Checkout completion print action must remain discoverable.");
        RequireContains(salesPage, "Receipt Snapshot Preview", "Sales detail must keep persisted snapshot preview.");
        RequireContains(salesPage, "Reprint Receipt", "Sales detail must keep reprint action discoverable.");

        foreach (string forbidden in new[]
        {
            "physical printer proven",
            "scanner hardware proven",
            "cash drawer supported",
            "cutter supported",
            "ESC/POS supported",
            "thermal profile supported",
            "hardware bundle supported",
            "hardware update later",
            "next hardware update",
            "backup UI complete",
            "licensing final",
            "installer ready",
            "beta ready",
            "store ready"
        })
        {
            RequireNotContains(printerSource, forbidden, "Printer-facing frontend source must not claim: " + forbidden);
        }
    }

    private sealed record LedgerSnapshot(long Sales, long Payments, long StockMovements, long StockOnHandX1000, string? ReceiptSnapshot);

    private static LedgerSnapshot CaptureLedger(SalrynLocalComposition composition, Guid productId, Guid saleId) =>
        new(
            CountTable(composition, "sales"),
            CountTable(composition, "payments"),
            CountTable(composition, "stock_movements"),
            ProductStock(composition, productId),
            ReceiptSnapshot(composition, saleId));

    private static void RequireLedgerUnchanged(SalrynLocalComposition composition, LedgerSnapshot before, Guid productId, Guid saleId, string area)
    {
        LedgerSnapshot after = CaptureLedger(composition, productId, saleId);
        Require(after.Sales == before.Sales, area + " must not duplicate or mutate sales.");
        Require(after.Payments == before.Payments, area + " must not duplicate or mutate payments.");
        Require(after.StockMovements == before.StockMovements, area + " must not duplicate or mutate stock movements.");
        Require(after.StockOnHandX1000 == before.StockOnHandX1000, area + " must not mutate stock quantity.");
        Require(after.ReceiptSnapshot == before.ReceiptSnapshot, area + " must not rewrite receipt snapshot.");
    }

    private static long CountTable(SalrynLocalComposition composition, string tableName)
    {
        if (tableName is not ("sales" or "payments" or "stock_movements")) throw new InvalidOperationException("Unexpected table count request.");
        using SqliteConnection connection = composition.ConnectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long ProductStock(SalrynLocalComposition composition, Guid productId)
    {
        using SqliteConnection connection = composition.ConnectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? ReceiptSnapshot(SalrynLocalComposition composition, Guid saleId)
    {
        using SqliteConnection connection = composition.ConnectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT receipt_snapshot FROM sales WHERE id = $id;";
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is DBNull or null ? null : Convert.ToString(value);
    }

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException(area + ": " + result.Error);
        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException(area + ": " + result.Error);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message);
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class EmptySaleReadRepository : ISaleReadRepository
    {
        public Task<Result<SaleDetailReadModel?>> FindSaleByIdAsync(Guid saleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<SaleDetailReadModel?>.Success(null));

        public Task<Result<SaleDetailReadModel?>> FindSaleByReceiptNoAsync(string receiptNo, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<SaleDetailReadModel?>.Success(null));

        public Task<Result<PagedResult<SaleHistoryItemReadModel>>> SearchSalesAsync(SaleHistoryFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PagedResult<SaleHistoryItemReadModel>>.Fail("Search is not used by PRINTER1 checks."));
    }

    private sealed class FailingReceiptPrinterAdapter : IReceiptPrinterAdapter
    {
        public string AdapterName => "FailingWindowsPrinterAdapter";
        public bool IsEnabled => true;

        public Task<ReceiptPrinterAdapterResult> PrintAsync(string jobName, string receiptText, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReceiptPrinterAdapterResult.Failed(
                "Fake Windows printer failure. Sale state is unchanged.",
                "FAKE_WINDOWS_PRINTER_OFFLINE"));
        }
    }
}

using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.Products;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Paths;

internal static class MvpBusinessFlowDatabaseSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== MVP BUSINESS FLOW DATABASE SMOKE CHECKS =====");

        string smokeRoot = Path.Combine(projectRoot, ".salryn-j20-mvp-business-flow-smoke");

        ResetSmokeRoot(smokeRoot);

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(smokeRoot);
            SalrynAppServices services = SalrynAppServices.Create(paths);

            RequireSuccess(
                services.DatabaseInitialization.InitializeDatabase(),
                "MvpBusinessFlow.InitializeDatabase");

            AuthenticatedUserReadModel admin = RequireSuccess(
                services.Authentication.CreateFirstAdminAsync(
                    new FirstRunAdminSetupRequest(
                        "admin",
                        "SALRYN Admin",
                        "StrongAdmin123!",
                        "StrongAdmin123!")).GetAwaiter().GetResult(),
                "MvpBusinessFlow.CreateFirstAdmin");

            CurrentAppSession appSession = new(
                admin.UserId,
                admin.RoleId,
                admin.Username,
                admin.DisplayName,
                admin.RoleName,
                admin.Status,
                admin.Permissions,
                DateTimeOffset.UtcNow);

            services.LocalHostSessionIdentityProvider.SetSession(appSession);

            ProductManagementPresenter productsPresenter = new(
                services.ProductReadRepository,
                services.ProductWriteRepository,
                services.CatalogReadRepository);

            ProductCreateFormViewModel createForm = productsPresenter
                .LoadCreateFormAsync()
                .GetAwaiter()
                .GetResult();

            if (createForm.HasError)
            {
                throw new InvalidOperationException(
                    $"MvpBusinessFlow.LoadCreateForm failed: {createForm.ErrorCode} / {createForm.ErrorMessage}");
            }

            ProductCatalogOptionViewModel unitOption = createForm.Units.FirstOrDefault(x => x.Id.HasValue)
                ?? throw new InvalidOperationException("MvpBusinessFlow expected at least one unit option.");

            string barcode = NewBarcode();

            ProductCreateOperationResult createProduct = productsPresenter
                .CreateProductAsync(new ProductCreateRequest(
                    Sku: "J20-MVP-SMOKE",
                    Barcode: barcode,
                    Name: "J20 MVP Smoke Product",
                    Description: "Created by MVP business flow database smoke.",
                    CategoryId: createForm.Categories.FirstOrDefault(x => x.Id.HasValue)?.Id,
                    BrandId: createForm.Brands.FirstOrDefault(x => x.Id.HasValue)?.Id,
                    UnitId: unitOption.Id!.Value,
                    SupplierId: null,
                    CostPrice: "10.00",
                    SellingPrice: "15.00",
                    WholesalePrice: null,
                    StockOnHand: "5",
                    ReorderLevel: "1",
                    TrackStock: true,
                    AllowDiscount: true,
                    Notes: "J20-4A smoke product."))
                .GetAwaiter()
                .GetResult();

            if (!createProduct.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"MvpBusinessFlow.CreateProduct failed: {createProduct.ErrorCode} / {createProduct.ErrorMessage}");
            }

            ProductLookupReadModel productBefore = RequireProductByBarcode(
                services,
                barcode,
                "MvpBusinessFlow.ProductBeforeCheckout");

            if (productBefore.StockOnHand.ValueX1000 != 5000)
            {
                throw new InvalidOperationException(
                    $"MvpBusinessFlow expected initial stock 5000, got {productBefore.StockOnHand.ValueX1000}.");
            }

            CashierSessionReadModel openSession = RequireSuccess(
                services.CashierSessions.OpenAsync(new OpenCashierSessionRequest(
                    admin.UserId,
                    Money.FromCents(10000),
                    "J20-4A MVP business flow smoke open")).GetAwaiter().GetResult(),
                "MvpBusinessFlow.OpenCashierSession");

            CheckoutSessionContext checkoutSession = CheckoutSessionContext.From(
                appSession,
                openSession);

            CheckoutPresenter checkoutPresenter = new(
                services.CheckoutProductSearch,
                checkoutSession,
                receiptNumberBoundary: null,
                completionBoundary: new CheckoutCompletionBoundary(services.PosCheckoutApplication));

            CheckoutPresenterOperationResult initialized = checkoutPresenter.Initialize();

            RequireCheckoutSuccess(initialized, "MvpBusinessFlow.InitializeCheckout");

            string receiptNumber = initialized.State.ReceiptNumber;

            if (string.IsNullOrWhiteSpace(receiptNumber))
            {
                throw new InvalidOperationException("MvpBusinessFlow expected checkout receipt number.");
            }

            CheckoutPresenterOperationResult searchResult = checkoutPresenter
                .SearchProductAsync(barcode)
                .GetAwaiter()
                .GetResult();

            RequireCheckoutSuccess(searchResult, "MvpBusinessFlow.SearchProduct");

            if (searchResult.State.PreparedProduct is null ||
                searchResult.State.PreparedProduct.ProductId != productBefore.Id ||
                !searchResult.State.CanAddPreparedProductLine)
            {
                throw new InvalidOperationException("MvpBusinessFlow product search did not prepare the created product.");
            }

            CheckoutPresenterOperationResult addLine = checkoutPresenter.AddPreparedProductLine();

            RequireCheckoutSuccess(addLine, "MvpBusinessFlow.AddPreparedProductLine");

            if (addLine.State.Lines.Count != 1 ||
                addLine.State.Totals.TotalCents != 1500)
            {
                throw new InvalidOperationException("MvpBusinessFlow draft line/totals verification failed.");
            }

            CheckoutPresenterOperationResult cashAmount = checkoutPresenter.SetCashAmountText("20.00");

            RequireCheckoutSuccess(cashAmount, "MvpBusinessFlow.SetCashAmount");

            CheckoutPresenterOperationResult addPayment = checkoutPresenter.AddCashPayment();

            RequireCheckoutSuccess(addPayment, "MvpBusinessFlow.AddCashPayment");

            if (addPayment.State.PaymentCount != 1 ||
                addPayment.State.Totals.AmountPaidCents != 2000 ||
                addPayment.State.Totals.ChangeCents != 500)
            {
                throw new InvalidOperationException("MvpBusinessFlow payment/change verification failed.");
            }

            CheckoutPresenterOperationResult dryRun = checkoutPresenter.DryRunCompletionRequest();

            RequireCheckoutSuccess(dryRun, "MvpBusinessFlow.DryRunCompletionRequest");

            if (!dryRun.State.CompletionDryRun.HasRun ||
                !dryRun.State.CompletionDryRun.IsSuccess ||
                !dryRun.State.CompletionDryRun.BuiltRequest)
            {
                throw new InvalidOperationException("MvpBusinessFlow completion dry run did not build request.");
            }

            CheckoutPresenterOperationResult guard = checkoutPresenter.PrepareSaleSubmitGuard();

            RequireCheckoutSuccess(guard, "MvpBusinessFlow.PrepareSaleSubmitGuard");

            if (!guard.State.SaleSubmitGuard.IsPrepared ||
                !guard.State.SaleSubmitGuard.CanExecuteSubmit)
            {
                throw new InvalidOperationException("MvpBusinessFlow submit guard was not ready.");
            }

            CheckoutPresenterOperationResult submit = checkoutPresenter
                .ExecutePreparedSaleSubmitAsync()
                .GetAwaiter()
                .GetResult();

            RequireCheckoutSuccess(submit, "MvpBusinessFlow.ExecutePreparedSaleSubmit");

            if (!submit.State.SaleSubmitResult.HasRun ||
                !submit.State.SaleSubmitResult.IsSuccess ||
                !submit.State.SaleSubmitResult.IsCompleted)
            {
                throw new InvalidOperationException("MvpBusinessFlow sale submit result was not completed.");
            }

            SaleDetailReadModel completedSale = RequireSaleByReceipt(
                services,
                receiptNumber,
                "MvpBusinessFlow.FindCompletedSale");

            VerifyCompletedSale(
                completedSale,
                receiptNumber,
                admin.DisplayName,
                productBefore.Id,
                barcode);

            VerifyStockMovementCount(
                paths,
                completedSale.Id,
                expectedCount: 1,
                "MvpBusinessFlow.StockMovementCount");

            ProductLookupReadModel productAfter = RequireProductByBarcode(
                services,
                barcode,
                "MvpBusinessFlow.ProductAfterCheckout");

            if (productAfter.StockOnHand.ValueX1000 != 4000)
            {
                throw new InvalidOperationException(
                    $"MvpBusinessFlow expected final stock 4000, got {productAfter.StockOnHand.ValueX1000}.");
            }

            CashierSessionReadModel closedSession = RequireSuccess(
                services.CashierSessions.CloseAsync(new CloseCashierSessionRequest(
                    openSession.Id,
                    admin.UserId,
                    Money.FromCents(10500),
                    "J20-4A MVP business flow smoke close")).GetAwaiter().GetResult(),
                "MvpBusinessFlow.CloseCashierSession");

            if (closedSession.Status != CashierSessionStatus.Closed ||
                closedSession.ClosedAt is null)
            {
                throw new InvalidOperationException("MvpBusinessFlow cashier shift did not close.");
            }

            CashierSessionReadModel? finalOpen = RequireSuccess(
                services.CashierSessions.FindOpenSessionByUserIdAsync(admin.UserId).GetAwaiter().GetResult(),
                "MvpBusinessFlow.FinalOpenSessionLookup");

            if (finalOpen is not null)
            {
                throw new InvalidOperationException("MvpBusinessFlow left an open cashier session.");
            }

            Console.WriteLine($"Admin user: {admin.UserId}");
            Console.WriteLine($"Product: {productBefore.Id}");
            Console.WriteLine($"Receipt: {receiptNumber}");
            Console.WriteLine($"Sale: {completedSale.Id}");
            Console.WriteLine("MVP business flow database smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            ResetSmokeRoot(smokeRoot);
        }
    }

    private static ProductLookupReadModel RequireProductByBarcode(
        SalrynAppServices services,
        string barcode,
        string label)
    {
        Barcode barcodeValue = RequireValue(
            Barcode.Create(barcode),
            label + ".Barcode");

        ProductLookupReadModel? product = RequireSuccess(
            services.ProductReadRepository.FindByBarcodeAsync(barcodeValue).GetAwaiter().GetResult(),
            label);

        if (product is null)
        {
            throw new InvalidOperationException($"{label} product was not found.");
        }

        return product;
    }

    private static SaleDetailReadModel RequireSaleByReceipt(
        SalrynAppServices services,
        string receiptNumber,
        string label)
    {
        SaleDetailReadModel? sale = RequireSuccess(
            services.SaleReadRepository.FindSaleByReceiptNoAsync(receiptNumber).GetAwaiter().GetResult(),
            label);

        if (sale is null)
        {
            throw new InvalidOperationException($"{label} sale was not found.");
        }

        return sale;
    }

    private static void VerifyCompletedSale(
        SaleDetailReadModel sale,
        string expectedReceiptNumber,
        string expectedCashierName,
        Guid expectedProductId,
        string expectedBarcode)
    {
        if (!string.Equals(sale.ReceiptNo, expectedReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(sale.CashierDisplayName, expectedCashierName, StringComparison.Ordinal) ||
            sale.Status != SaleStatus.Completed ||
            sale.Items.Count != 1 ||
            sale.Payments.Count != 1 ||
            sale.Subtotal.Cents != 1500 ||
            sale.Total.Cents != 1500 ||
            sale.AmountPaid.Cents != 2000 ||
            sale.Change.Cents != 500)
        {
            throw new InvalidOperationException("MvpBusinessFlow completed sale header verification failed.");
        }

        SaleItemDetailReadModel item = sale.Items[0];

        if (item.ProductId != expectedProductId ||
            !string.Equals(item.BarcodeSnapshot, expectedBarcode, StringComparison.Ordinal) ||
            item.Quantity.ValueX1000 != 1000 ||
            item.UnitPrice.Cents != 1500 ||
            item.LineTotal.Cents != 1500)
        {
            throw new InvalidOperationException("MvpBusinessFlow completed sale item verification failed.");
        }

        PaymentReadModel payment = sale.Payments[0];

        if (payment.Method != PaymentMethod.Cash ||
            payment.Amount.Cents != 2000)
        {
            throw new InvalidOperationException("MvpBusinessFlow payment verification failed.");
        }
    }

    private static void VerifyStockMovementCount(
        DbPaths paths,
        Guid saleId,
        int expectedCount,
        string label)
    {
        using SqliteConnection connection = new($"Data Source={paths.DatabasePath}");
        connection.Open();
        SqlitePragma.Apply(connection);

        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM stock_movements
            WHERE reference_type = 'SALE'
              AND reference_id = $saleId;
            """;

        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        int actualCount = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"{label} expected {expectedCount} stock movement(s), got {actualCount}.");
        }
    }

    private static void RequireCheckoutSuccess(
        CheckoutPresenterOperationResult result,
        string label)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"{label} failed: {result.ErrorCode} / {result.ErrorMessage}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }

    private static T RequireValue<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }

    private static string NewBarcode()
    {
        return "94" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()[^10..];
    }

    private static void ResetSmokeRoot(string smokeRoot)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (Directory.Exists(smokeRoot))
        {
            Directory.Delete(smokeRoot, recursive: true);
        }
    }
}
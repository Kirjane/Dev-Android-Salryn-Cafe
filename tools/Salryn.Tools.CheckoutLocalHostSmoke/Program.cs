using System.Globalization;
using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;

string command = args.Length == 0 ? "fake" : args[0].Trim().ToLowerInvariant();

Console.WriteLine("===== SALRYN CHECKOUT LOCAL HOST LOOKUP SMOKE =====");
Console.WriteLine($"Command: {command}");
Console.WriteLine();

switch (command)
{
    case "fake":
        await CheckoutLocalHostLookupSmokeChecks.RunAsync();
        await CheckoutOfficialModePresenterSmokeChecks.RunAsync();
        Console.WriteLine("SALRYN checkout Local Host lookup smoke passed.");
        break;

    case "database":
        await CheckoutLocalHostDatabaseLookupSmokeChecks.RunAsync();
        Console.WriteLine("SALRYN checkout Local Host database lookup smoke passed.");
        break;

    default:
        Console.WriteLine("Unknown command.");
        Console.WriteLine("Use: fake | database");
        Environment.ExitCode = 1;
        return;
}

internal static class CheckoutLocalHostLookupSmokeChecks
{
    public static async Task RunAsync()
    {
        const string barcode = "480000000001";
        const string sku = "E2E-SKU-001";
        const string productName = "E2E Local Host Cola";
        const string unitName = "piece";
        const long sellingPriceCents = 1250;
        const long costPriceCents = 700;
        const long stockOnHandX1000 = 12000;

        Guid productId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        SmokeGateway gateway = SmokeGateway.Found(
            productId,
            barcode,
            sku,
            productName,
            unitName,
            sellingPriceCents,
            costPriceCents,
            stockOnHandX1000);

        ICheckoutProductSearchBoundary boundary =
            new CheckoutLocalHostProductSearchBoundary(gateway);

        CheckoutPresenter presenter = new CheckoutPresenter(boundary);

        CheckoutPresenterOperationResult inputResult =
            presenter.SetProductInputText($"  {barcode}  ");

        Require(inputResult.IsSuccess, "Product input should be accepted before search.");
        Require(inputResult.State.CanSearchProduct, "Presenter must allow search when the Local Host-backed boundary is wired.");

        CheckoutPresenterOperationResult searchResult =
            await presenter.SearchProductAsync($"  {barcode}  ").ConfigureAwait(false);

        Require(searchResult.IsSuccess, "Local checkout lookup should succeed.");
        Require(gateway.CallCount == 1, "Local Host gateway should be called exactly once.");
        Require(gateway.LastBarcode == barcode, "Checkout boundary must trim scanned barcode before gateway lookup.");
        Require(searchResult.State.ProductInputText == barcode, "Presenter must keep trimmed product input.");
        Require(searchResult.State.ProductResultText == $"{productName} is ready to add.", "Presenter must show product prepared result text.");
        Require(searchResult.State.StatusMessage == "Product found and prepared.", "Presenter must report prepared status.");
        Require(searchResult.State.PreparedProduct is not null, "Presenter must expose prepared product after Local Host lookup.");
        Require(searchResult.State.CanAddPreparedProductLine, "Presenter must allow adding the prepared Local Host product line.");

        CheckoutPreparedProductViewModel prepared = searchResult.State.PreparedProduct!;

        Require(prepared.ProductId == productId, "Prepared product id must come from Local Host payload.");
        Require(prepared.ProductName == productName, "Prepared product name must come from Local Host payload.");
        Require(prepared.Sku == sku, "Prepared product SKU must come from Local Host payload.");
        Require(prepared.Barcode == barcode, "Prepared product barcode must come from Local Host payload.");
        Require(prepared.UnitName == unitName, "Prepared product unit must come from Local Host payload.");
        Require(prepared.UnitPriceCents == sellingPriceCents, "Prepared product selling price must come from Local Host payload.");
        Require(prepared.CostPriceCents == costPriceCents, "Prepared product cost price must come from Local Host payload.");
        Require(prepared.StockOnHandX1000 == stockOnHandX1000, "Prepared product stock must come from Local Host payload.");
        Require(prepared.TrackStock, "Prepared product track-stock flag must come from Local Host payload.");
        Require(prepared.IsActive, "Prepared product active flag must come from Local Host payload.");

        CheckoutPresenterOperationResult addResult = presenter.AddPreparedProductLine();

        Require(addResult.IsSuccess, "Prepared Local Host product should be addable to the checkout draft.");
        Require(addResult.State.Lines.Count == 1, "Checkout draft must contain exactly one line after adding prepared product.");
        Require(!addResult.State.CanAddPreparedProductLine, "Prepared product should be cleared after adding it to the draft.");

        CheckoutLineViewModel line = addResult.State.Lines[0];

        Require(line.ProductId == productId, "Checkout line product id must match Local Host payload.");
        Require(line.ProductName == productName, "Checkout line product name must match Local Host payload.");
        Require(line.Barcode == barcode, "Checkout line barcode must match Local Host payload.");
        Require(line.UnitName == unitName, "Checkout line unit must match Local Host payload.");
        Require(line.QuantityX1000 == 1000, "Checkout line must default to one unit.");
        Require(line.UnitPriceCents == sellingPriceCents, "Checkout line price must match Local Host payload.");
        Require(line.CostPriceCents == costPriceCents, "Checkout line cost must match Local Host payload.");
        Require(line.GrossCents == sellingPriceCents, "Checkout line gross must equal one unit of selling price.");
        Require(line.LineTotalCents == sellingPriceCents, "Checkout line total must equal one unit of selling price.");
        Require(addResult.State.Totals.SubtotalCents == sellingPriceCents, "Checkout subtotal must reflect the Local Host product line.");
        Require(addResult.State.Totals.TotalCents == sellingPriceCents, "Checkout total must reflect the Local Host product line.");

        await VerifyNotFoundPathAsync().ConfigureAwait(false);
        await VerifyUnavailableFailurePathAsync().ConfigureAwait(false);
    }

    private static async Task VerifyNotFoundPathAsync()
    {
        const string barcode = "000000000000";

        SmokeGateway gateway = SmokeGateway.NotFound();
        CheckoutPresenter presenter = new(new CheckoutLocalHostProductSearchBoundary(gateway));

        CheckoutPresenterOperationResult result =
            await presenter.SearchProductAsync(barcode).ConfigureAwait(false);

        Require(result.IsSuccess, "Not-found lookup should be a successful empty checkout search result.");
        Require(gateway.CallCount == 1, "Not-found lookup should call the gateway once.");
        Require(result.State.PreparedProduct is null, "Not-found lookup must not prepare a product.");
        Require(!result.State.CanAddPreparedProductLine, "Not-found lookup must not allow adding a product line.");
        Require(result.State.ProductResultText == $"No product found for '{barcode}'.", "Not-found lookup should update product result text.");
    }

    private static async Task VerifyUnavailableFailurePathAsync()
    {
        const string barcode = "480000000999";

        SmokeGateway gateway = SmokeGateway.Unavailable("Local Host smoke unavailable.");
        CheckoutPresenter presenter = new(new CheckoutLocalHostProductSearchBoundary(gateway));

        CheckoutPresenterOperationResult result =
            await presenter.SearchProductAsync(barcode).ConfigureAwait(false);

        Require(!result.IsSuccess, "Unavailable Local Host lookup should fail the checkout search operation.");
        Require(gateway.CallCount == 1, "Unavailable lookup should call the gateway once.");
        Require(result.ErrorCode == "CheckoutLocalHostProductSearch.LocalHostUnavailable", "Unavailable lookup should surface Local Host failure code.");
        Require(result.ErrorMessage == "Local Host smoke unavailable.", "Unavailable lookup should surface Local Host failure message.");
        Require(result.State.PreparedProduct is null, "Unavailable lookup must not prepare a product.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeGateway : IAppLocalHostProductLookupGateway
    {
        private readonly Func<string, AppLocalHostProductLookupResult> _handler;

        private SmokeGateway(Func<string, AppLocalHostProductLookupResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public string? LastBarcode { get; private set; }

        public static SmokeGateway Found(
            Guid productId,
            string barcode,
            string sku,
            string productName,
            string unitName,
            long sellingPriceCents,
            long costPriceCents,
            long stockOnHandX1000)
        {
            return new SmokeGateway(_ => AppLocalHostProductLookupResult.FoundProduct(
                productId,
                sku,
                barcode,
                productName,
                unitName,
                sellingPriceCents,
                costPriceCents,
                stockOnHandX1000,
                trackStock: true,
                allowDiscount: true,
                isActive: true));
        }

        public static SmokeGateway NotFound()
        {
            return new SmokeGateway(AppLocalHostProductLookupResult.NotFound);
        }

        public static SmokeGateway Unavailable(string message)
        {
            return new SmokeGateway(_ => AppLocalHostProductLookupResult.LocalHostUnavailable(message));
        }

        public Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync(
            AppLocalHostProductLookupRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastBarcode = request.Barcode;

            return Task.FromResult(_handler(request.Barcode));
        }
    }
}

internal static class CheckoutOfficialModePresenterSmokeChecks
{
    public static async Task RunAsync()
    {
        CheckoutPresenter lockedPresenter = new(
            productSearchBoundary: null,
            sessionContext: null,
            receiptNumberBoundary: null,
            completionBoundary: null,
            officialModeReadinessBoundary: new FakeOfficialModeReadinessBoundary(
                new CheckoutOfficialModeReadiness(
                    HasCheckedReadiness: true,
                    IsReady: false,
                    CanSelectOfficialMode: false,
                    SummaryText: "Official output locked",
                    DetailText: "No active BIR machine profile was found.",
                    ErrorCode: "OfficialCheckoutReadiness.ActiveMachineProfileMissing",
                    ErrorMessage: "No active BIR machine profile was found.")));

        RequireSuccess(lockedPresenter.Initialize(), "OfficialMode.Locked.Initialize");

        CheckoutPresenterOperationResult lockedRefresh =
            await lockedPresenter.RefreshOfficialModeReadinessAsync().ConfigureAwait(false);

        RequireSuccess(lockedRefresh, "OfficialMode.Locked.Refresh");
        Require(!lockedRefresh.State.OfficialMode.CanSelectOfficialMode, "Locked official mode must not be selectable.");
        Require(!lockedRefresh.State.OfficialMode.IsOfficialModeSelected, "Locked official mode must remain unselected.");

        CheckoutPresenterOperationResult lockedSelect = lockedPresenter.SelectOfficialMode();

        RequireFailureCode(
            lockedSelect,
            "OfficialCheckoutReadiness.ActiveMachineProfileMissing",
            "Locked official selection must fail with backend readiness code.");

        CheckoutPresenter readyPresenter = new(
            productSearchBoundary: null,
            sessionContext: null,
            receiptNumberBoundary: null,
            completionBoundary: null,
            officialModeReadinessBoundary: new FakeOfficialModeReadinessBoundary(
                new CheckoutOfficialModeReadiness(
                    HasCheckedReadiness: true,
                    IsReady: true,
                    CanSelectOfficialMode: true,
                    SummaryText: "Official output ready",
                    DetailText: "Active accredited BIR machine profile and official receipt sequence are ready.",
                    ErrorCode: null,
                    ErrorMessage: null)));

        RequireSuccess(readyPresenter.Initialize(), "OfficialMode.Ready.Initialize");

        CheckoutPresenterOperationResult readyRefresh =
            await readyPresenter.RefreshOfficialModeReadinessAsync().ConfigureAwait(false);

        RequireSuccess(readyRefresh, "OfficialMode.Ready.Refresh");
        Require(readyRefresh.State.OfficialMode.CanSelectOfficialMode, "Ready official mode must be selectable.");

        CheckoutPresenterOperationResult readySelect = readyPresenter.SelectOfficialMode();

        RequireSuccess(readySelect, "OfficialMode.Ready.SelectOfficial");
        Require(readySelect.State.OfficialMode.IsOfficialModeSelected, "Ready official mode must become selected.");

        CheckoutPresenterOperationResult officialDryRun = readyPresenter.DryRunCompletionRequest();

        RequireFailureCode(
            officialDryRun,
            "CheckoutOfficialMode.CompletionNotWired",
            "Official mode must block dry-run until official persistence is wired to UI completion.");

        CheckoutPresenterOperationResult standardSelect = readyPresenter.SelectStandardMode();

        RequireSuccess(standardSelect, "OfficialMode.Ready.SelectStandard");
        Require(!standardSelect.State.OfficialMode.IsOfficialModeSelected, "Standard mode must clear official selection.");
    }

    private sealed class FakeOfficialModeReadinessBoundary : ICheckoutOfficialModeReadinessBoundary
    {
        private readonly CheckoutOfficialModeReadiness _readiness;

        public FakeOfficialModeReadinessBoundary(CheckoutOfficialModeReadiness readiness)
        {
            _readiness = readiness;
        }

        public Task<Result<CheckoutOfficialModeReadiness>> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<CheckoutOfficialModeReadiness>.Success(_readiness));
        }
    }

    private static void RequireSuccess(CheckoutPresenterOperationResult result, string area)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorCode} {result.ErrorMessage}");
        }
    }

    private static void RequireFailureCode(
        CheckoutPresenterOperationResult result,
        string expectedCode,
        string message)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(message);
        }

        if (!string.Equals(result.ErrorCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {expectedCode}, found {result.ErrorCode}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal static class CheckoutLocalHostDatabaseLookupSmokeChecks
{
    public static async Task RunAsync()
    {
        string tempRoot = CreateTempRoot();
        SalrynAppServices? services = null;

        try
        {
            services = SalrynAppServices.CreateDevelopment(tempRoot);

            RequireSuccess(
                services.DatabaseInitialization.InitializeDatabase(),
                "Database.InitializeDatabase");

            UnitReadModel unit = RequireSuccess(
                    await services.CatalogReadRepository.GetUnitsAsync().ConfigureAwait(false),
                    "Database.GetUnits")
                .FirstOrDefault(item => string.Equals(item.Abbreviation, "pc", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Default pc unit was not found for database checkout lookup smoke.");

            string suffix = Guid.NewGuid().ToString("N")[..10];
            string barcode = CreateNumericBarcode();
            Guid productId = Guid.NewGuid();
            const long sellingPriceCents = 2199;
            const long costPriceCents = 1300;
            const long stockOnHandX1000 = 9000;

            Product product = new()
            {
                Id = productId,
                Sku = "P66G8-DB-" + suffix,
                Barcode = BarcodeValue(barcode),
                Name = "Phase66G8 Database Lookup Product " + suffix,
                Description = "Phase 66G8 checkout Local Host database smoke product.",
                UnitId = unit.Id,
                CostPrice = Money.FromCents(costPriceCents),
                SellingPrice = Money.FromCents(sellingPriceCents),
                WholesalePrice = null,
                StockOnHand = QuantityX1000(stockOnHandX1000),
                ReorderLevel = QuantityX1000(1000),
                TrackStock = true,
                AllowDiscount = true,
                IsActive = true,
                Notes = "Created inside temporary 66G8 smoke database.",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = null
            };

            RequireSuccess(
                await services.ProductWriteRepository.AddAsync(product).ConfigureAwait(false),
                "Database.AddProduct");

            services.LocalHostSessionIdentityProvider.SetSession(new CurrentAppSession(
                userId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000001"),
                roleId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000002"),
                username: "phase66g8-smoke",
                displayName: "Phase 66G8 Smoke",
                roleName: "SmokeRole",
                status: UserStatus.Active,
                permissions: Array.Empty<string>(),
                startedAt: DateTimeOffset.UtcNow));

            CheckoutPresenter presenter = new CheckoutPresenter(services.CheckoutProductSearch);

            CheckoutPresenterOperationResult searchResult =
                await presenter.SearchProductAsync($"  {barcode}  ").ConfigureAwait(false);

            Require(searchResult.IsSuccess, "Database-backed Local Host checkout lookup should succeed.");
            Require(searchResult.State.ProductInputText == barcode, "Database-backed lookup should trim barcode input.");
            Require(searchResult.State.PreparedProduct is not null, "Database-backed lookup should prepare a product.");
            Require(searchResult.State.CanAddPreparedProductLine, "Database-backed lookup should allow adding the prepared product.");

            CheckoutPreparedProductViewModel prepared = searchResult.State.PreparedProduct!;

            Require(prepared.ProductId == productId, "Prepared product id must come from the temporary database.");
            Require(prepared.ProductName == product.Name, "Prepared product name must come from the temporary database.");
            Require(prepared.Sku == product.Sku, "Prepared product SKU must come from the temporary database.");
            Require(prepared.Barcode == barcode, "Prepared product barcode must come from the temporary database.");
            Require(prepared.UnitName == unit.Name, "Prepared product unit name must come from the seeded unit.");
            Require(prepared.UnitPriceCents == sellingPriceCents, "Prepared product price must come from the temporary database.");
            Require(prepared.CostPriceCents == costPriceCents, "Prepared product cost must come from the temporary database.");
            Require(prepared.StockOnHandX1000 == stockOnHandX1000, "Prepared product stock must come from the temporary database.");
            Require(prepared.TrackStock, "Prepared product track-stock flag must come from the temporary database.");
            Require(prepared.IsActive, "Prepared product active flag must come from the temporary database.");

            CheckoutPresenterOperationResult addResult = presenter.AddPreparedProductLine();

            Require(addResult.IsSuccess, "Database-backed prepared product should be added to the checkout draft.");
            Require(addResult.State.Lines.Count == 1, "Database-backed checkout draft must have one line.");
            Require(addResult.State.Totals.SubtotalCents == sellingPriceCents, "Database-backed checkout subtotal must match product price.");
            Require(addResult.State.Totals.TotalCents == sellingPriceCents, "Database-backed checkout total must match product price.");

            CheckoutLineViewModel line = addResult.State.Lines[0];

            Require(line.ProductId == productId, "Database-backed checkout line product id must match product.");
            Require(line.Barcode == barcode, "Database-backed checkout line barcode must match product.");
            Require(line.QuantityX1000 == 1000, "Database-backed checkout line must default to one unit.");
            Require(line.UnitPriceCents == sellingPriceCents, "Database-backed checkout line unit price must match product.");

            CheckoutPresenterOperationResult notFoundResult =
                await presenter.SearchProductAsync("999999999999").ConfigureAwait(false);

            Require(notFoundResult.IsSuccess, "Database-backed not-found lookup should return successful empty search result.");
            Require(notFoundResult.State.PreparedProduct is null, "Database-backed not-found lookup must not prepare product.");
            Require(!notFoundResult.State.CanAddPreparedProductLine, "Database-backed not-found lookup must not allow add line.");
        }
        finally
        {
            TryReleaseSmokeResources(services);
            TryDeleteTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-66g8-checkout-localhost-db-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        return tempRoot;
    }

    private static string CreateNumericBarcode()
    {
        string millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        return "66" + millis[^10..];
    }

    private static Barcode BarcodeValue(string value)
    {
        Result<Barcode> result = Barcode.Create(value);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Barcode helper failed: {result.Error}");
        }

        return result.Value;
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Quantity helper failed: {result.Error}");
        }

        return result.Value;
    }

    private static void TryReleaseSmokeResources(SalrynAppServices? services)
    {
        Exception? releaseException = null;

        if (services is not null)
        {
            try
            {
                services.LocalHostSessionIdentityProvider.SetSession(null);

                SalrynLocalHostRuntimeLifecycleResult stopResult =
                    services.LocalHostRuntimeLifecycleHolder.TryStop();

                if (!stopResult.IsSuccess)
                {
                    releaseException = new InvalidOperationException(
                        $"Local Host runtime did not stop during 66G8 cleanup: {stopResult.Message}");
                }
            }
            catch (Exception ex)
            {
                releaseException = ex;
            }
        }

        SqliteConnectionFactory.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (releaseException is not null)
        {
            throw new InvalidOperationException(
                "66G8 database smoke resource release failed before cleanup.",
                releaseException);
        }
    }

    private static void TryDeleteTempRoot(string tempRoot)
    {
        DeleteTempRootOrThrow(tempRoot);
    }

    private static void DeleteTempRootOrThrow(string tempRoot)
    {
        if (string.IsNullOrWhiteSpace(tempRoot) || !Directory.Exists(tempRoot))
        {
            return;
        }

        Exception? lastException = null;

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                SqliteConnectionFactory.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Directory.Delete(tempRoot, recursive: true);

                if (Directory.Exists(tempRoot))
                {
                    throw new IOException($"Temporary 66G8 smoke folder still exists after delete attempt: {tempRoot}");
                }

                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                Thread.Sleep(250);
            }
        }

        throw new IOException(
            $"Temporary 66G8 smoke folder was not deleted: {tempRoot}",
            lastException);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }
}

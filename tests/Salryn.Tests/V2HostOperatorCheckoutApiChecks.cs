using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class V2HostOperatorCheckoutApiChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;
    private const string SmokeCheckoutProductName = "SALRYN-SMOKE-CHECKOUT-V2";
    private const string SmokeDepletedProductName = "SALRYN-SMOKE-CHECKOUT-DEPLETED";
    private const long CheckoutLineQuantityX1000 = 1000;
    private const long SmokeTargetStockX1000 = 50_000;

    public static async Task RunAsync()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        await RunRuntimeChecksAsync(projectRoot).ConfigureAwait(false);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string factory = ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string checkoutEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalHostCheckoutEndpoints.cs"));
        string checkoutService = ReadRequiredFile(Path.Combine(apiRoot, "Services", "HostOperatorCheckoutApiService.cs"));
        string hostOperatorServices = ReadRequiredFile(Path.Combine(apiRoot, "Services", "HostOperatorApiServices.cs"));
        string permissions = ReadRequiredFile(Path.Combine(apiRoot, "Auth", "LocalApiPermissionGuards.cs"));
        string bootstrap = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Host.Local",
            "Bootstrap",
            "LocalHostHandlerBootstrapModels.cs"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string seedFile = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Seeding",
            "DefaultSeedData.cs"));
        string navigation = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.App.Web",
            "src",
            "navigation.ts"));

        AssertContains(coreSmoke, "v2-host-checkout", "CoreSmoke must expose v2-host-checkout runner.");
        AssertContains(factory, "MapLocalHostCheckoutEndpoints", "Factory must map host checkout endpoints.");
        AssertContains(checkoutEndpoints, "/api/checkout", "Host checkout route must exist.");
        AssertContains(checkoutEndpoints, "LocalApiPermissions.SalesCreate", "Checkout must require Sales.Create.");
        AssertContains(checkoutService, "IPosCheckoutApplicationService", "Checkout must use authoritative sale completion service.");
        AssertContains(checkoutService, "ICashierSessionService", "Checkout must validate cashier shift state.");
        AssertContains(hostOperatorServices, "CreateSaleWriteRepository", "Checkout composition must wire sale write repository.");
        AssertNotContains(checkoutService, "LocalHostBootstrapNotImplementedHandler", "Checkout must not use placeholder Local Host handlers.");
        AssertContains(bootstrap, "LocalHostBootstrapNotImplementedHandler", "Placeholder handlers remain quarantined in Local Host bootstrap.");
        AssertContains(permissions, "SalesCreate = \"Sales.Create\"", "Api permission catalog must include Sales.Create.");
        AssertContains(seedFile, "Permissions.SalesCreate", "Sales.Create must remain seeded.");
        AssertContains(navigation, "POS Checkout", "Navigation must still list checkout.");
        AssertContains(navigation, "id: \"checkout\", label: \"POS Checkout\", enabled: true", "React checkout nav must remain enabled after backend verification.");
    }

    private static async Task RunRuntimeChecksAsync(string projectRoot)
    {
        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(projectRoot);
        await composition.InitializeDatabaseAsync().ConfigureAwait(false);

        AuthenticatedUserReadModel operatorUser = await EnsureCheckoutOperatorAsync(composition)
            .ConfigureAwait(false);
        ProductLookupReadModel product = await EnsureCheckoutProductAsync(composition, operatorUser.UserId)
            .ConfigureAwait(false);
        ProductLookupReadModel depletedProduct = await EnsureDepletedCheckoutProductAsync(
                composition,
                operatorUser.UserId)
            .ConfigureAwait(false);

        InMemoryLocalApiSessionStore sessionStore = new();

        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    InitializeDatabase = false,
                    SessionStore = sessionStore,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = ResolveStartedBaseAddress(app);
            HostOperatorCheckoutRequest emptyCartRequest = BuildCheckoutRequest(
                Array.Empty<HostOperatorCheckoutLineRequest>(),
                BuildCashPayment(1000));

            using HttpClient anonymous = CreateClient(baseAddress);
            await AssertCheckoutStatusAsync(
                anonymous,
                emptyCartRequest,
                HttpStatusCode.Unauthorized,
                "Checkout without session must return 401.").ConfigureAwait(false);

            using HttpClient noPermission = CreateClient(
                baseAddress,
                sessionStore.Create(new AuthenticatedUserReadModel(
                    Guid.NewGuid(),
                    "v2-checkout-denied",
                    "V2 Checkout Denied",
                    "Manager",
                    UserStatus.Active,
                    new[] { DefaultSeedData.Permissions.ProductsView })));
            await AssertCheckoutStatusAsync(
                noPermission,
                emptyCartRequest,
                HttpStatusCode.Forbidden,
                "Checkout without Sales.Create must return 403.").ConfigureAwait(false);

            using HttpClient operatorClient = CreateClient(baseAddress, sessionStore.Create(operatorUser));
            await EnsureNoOpenShiftAsync(operatorClient).ConfigureAwait(false);

            HostOperatorCheckoutRequest noShiftRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(product.Id, CheckoutLineQuantityX1000) },
                BuildCashPayment(product.SellingPrice.Cents + 100));

            await AssertCheckoutStatusAsync(
                operatorClient,
                noShiftRequest,
                HttpStatusCode.Conflict,
                "Checkout without open shift must return 409.").ConfigureAwait(false);

            await PostShiftOpenAsync(operatorClient, 0).ConfigureAwait(false);

            await AssertCheckoutStatusAsync(
                operatorClient,
                emptyCartRequest,
                HttpStatusCode.BadRequest,
                "Checkout with empty cart must return 400.").ConfigureAwait(false);

            HostOperatorCheckoutRequest missingProductRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(Guid.NewGuid(), 1000) },
                BuildCashPayment(5000));

            await AssertCheckoutStatusAsync(
                operatorClient,
                missingProductRequest,
                HttpStatusCode.NotFound,
                "Checkout with missing product must return 404.").ConfigureAwait(false);

            long lineTotal = product.SellingPrice.Cents;
            HostOperatorCheckoutRequest insufficientPaymentRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(product.Id, CheckoutLineQuantityX1000) },
                BuildCashPayment(Math.Max(1, lineTotal - 1)));

            await AssertCheckoutStatusAsync(
                operatorClient,
                insufficientPaymentRequest,
                HttpStatusCode.BadRequest,
                "Checkout with insufficient payment must return 400.").ConfigureAwait(false);

            HostOperatorCheckoutRequest negativeStockRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(depletedProduct.Id, CheckoutLineQuantityX1000) },
                BuildCashPayment(depletedProduct.SellingPrice.Cents + 100));

            await AssertCheckoutConflictAsync(
                operatorClient,
                negativeStockRequest,
                "Sales.NegativeStockNotAllowed",
                "StockChanged",
                "Checkout with insufficient stock must return 409 StockChanged.").ConfigureAwait(false);

            HostOperatorCheckoutRequest validRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(product.Id, CheckoutLineQuantityX1000) },
                BuildCashPayment(lineTotal + 500));

            JsonDocument completed = await PostCheckoutAsync(
                operatorClient,
                validRequest,
                product).ConfigureAwait(false);
            string? status = completed.RootElement.GetProperty("checkout").GetProperty("status").GetString();
            string? receiptNumber = completed.RootElement.GetProperty("checkout").GetProperty("receiptNumber").GetString();
            string? saleIdText = completed.RootElement.GetProperty("checkout").GetProperty("saleId").GetString();

            if (!string.Equals(status, "Completed", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(receiptNumber) ||
                string.IsNullOrWhiteSpace(saleIdText) ||
                !Guid.TryParse(saleIdText, out Guid saleId))
            {
                throw new InvalidOperationException("Valid checkout must return a completed sale with receipt and sale id.");
            }

            using HttpResponseMessage saleResponse = await operatorClient.GetAsync($"/api/sales/{saleId:D}")
                .ConfigureAwait(false);
            if (saleResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException(
                    $"Completed sale must be readable through sales history API. Got {(int)saleResponse.StatusCode}.");
            }

            JsonDocument saleDocument = JsonDocument.Parse(
                await saleResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            string? readReceipt = saleDocument.RootElement.GetProperty("receiptNo").GetString();
            if (!string.Equals(readReceipt, receiptNumber, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Sales readback receipt number must match checkout receipt.");
            }

            await PostShiftCloseAsync(operatorClient).ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task<AuthenticatedUserReadModel> EnsureCheckoutOperatorAsync(
        SalrynLocalComposition composition)
    {
        Result<IReadOnlyList<UserReadModel>> usersResult =
            await composition.UserReadRepository.GetAllAsync().ConfigureAwait(false);

        if (usersResult.IsFailure)
        {
            throw new InvalidOperationException(
                "V2 checkout runtime could not read users: " + usersResult.ErrorDetail.Message);
        }

        foreach (UserReadModel user in usersResult.Value.Where(user => user.Status == UserStatus.Active))
        {
            Result<UserPermissionReadModel?> permissionsResult =
                await composition.UserPermissionReadRepository.FindByUserIdAsync(user.Id).ConfigureAwait(false);

            if (permissionsResult.IsFailure)
            {
                throw new InvalidOperationException(
                    "V2 checkout runtime could not read permissions: " + permissionsResult.ErrorDetail.Message);
            }

            UserPermissionReadModel? permissions = permissionsResult.Value;
            if (permissions is not null &&
                permissions.Permissions.Contains(DefaultSeedData.Permissions.SalesCreate, StringComparer.Ordinal) &&
                permissions.Permissions.Contains(DefaultSeedData.Permissions.ShiftOpenClose, StringComparer.Ordinal) &&
                permissions.Permissions.Contains(DefaultSeedData.Permissions.SalesViewHistory, StringComparer.Ordinal))
            {
                return new AuthenticatedUserReadModel(
                    permissions.UserId,
                    permissions.Username,
                    permissions.DisplayName,
                    permissions.RoleName,
                    permissions.Status,
                    permissions.Permissions)
                {
                    RoleId = permissions.RoleId
                };
            }
        }

        if (usersResult.Value.Any(user => user.Status == UserStatus.Active))
        {
            throw new InvalidOperationException(
                "V2 checkout runtime requires an active operator with Sales.Create, Shift.OpenClose, and Sales.ViewHistory.");
        }

        Result<AuthenticatedUserReadModel> firstAdmin = await composition.Authentication
            .CreateFirstAdminAsync(new FirstRunAdminSetupRequest(
                "v2-checkout-admin",
                "V2 Checkout Admin",
                "V2CheckoutAdmin!123",
                "V2CheckoutAdmin!123"))
            .ConfigureAwait(false);

        if (firstAdmin.IsFailure)
        {
            throw new InvalidOperationException(
                "V2 checkout runtime could not create first admin: " + firstAdmin.ErrorDetail.Message);
        }

        return firstAdmin.Value;
    }

    private static async Task<ProductLookupReadModel> EnsureCheckoutProductAsync(
        SalrynLocalComposition composition,
        Guid operatorUserId)
    {
        ProductLookupReadModel? existing = await FindSmokeProductByNameAsync(
                composition,
                SmokeCheckoutProductName)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ReplenishSmokeProductStockAsync(
                    composition,
                    existing,
                    operatorUserId,
                    SmokeTargetStockX1000)
                .ConfigureAwait(false);
        }

        return await CreateSmokeCheckoutProductAsync(
                composition,
                operatorUserId,
                SmokeCheckoutProductName,
                SmokeTargetStockX1000,
                "Dedicated V2 host checkout smoke product with guaranteed stock.")
            .ConfigureAwait(false);
    }

    private static async Task<ProductLookupReadModel> EnsureDepletedCheckoutProductAsync(
        SalrynLocalComposition composition,
        Guid operatorUserId)
    {
        ProductLookupReadModel? existing = await FindSmokeProductByNameAsync(
                composition,
                SmokeDepletedProductName)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.StockOnHand.ValueX1000 >= CheckoutLineQuantityX1000)
            {
                await DrainProductStockAsync(composition, existing, operatorUserId).ConfigureAwait(false);
                existing = RequireSuccess(
                    await composition.ProductReadRepository
                        .FindByIdAsync(existing.Id, CancellationToken.None)
                        .ConfigureAwait(false),
                    "V2Checkout.ReadDepletedProduct");
            }

            if (existing is null)
            {
                throw new InvalidOperationException("V2 checkout runtime could not read depleted smoke product.");
            }

            return existing;
        }

        return await CreateSmokeCheckoutProductAsync(
                composition,
                operatorUserId,
                SmokeDepletedProductName,
                initialStockX1000: 0,
                "Dedicated depleted product for negative-stock checkout smoke.")
            .ConfigureAwait(false);
    }

    private static async Task<ProductLookupReadModel?> FindSmokeProductByNameAsync(
        SalrynLocalComposition composition,
        string productName)
    {
        Result<PagedResult<ProductListItemReadModel>> searchResult = await composition.ProductReadRepository
            .SearchAsync(
                new ProductSearchFilter(
                    productName,
                    null,
                    null,
                    true,
                    IncludeInactive: false,
                    new PageRequest(1, 20)),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (searchResult.IsFailure || searchResult.Value.Items.Count == 0)
        {
            return null;
        }

        foreach (ProductListItemReadModel item in searchResult.Value.Items)
        {
            if (!string.Equals(item.Name, productName, StringComparison.Ordinal))
            {
                continue;
            }

            Result<ProductLookupReadModel?> lookup = await composition.ProductReadRepository
                .FindByIdAsync(item.Id, CancellationToken.None)
                .ConfigureAwait(false);

            if (lookup.IsSuccess && lookup.Value is not null)
            {
                return lookup.Value;
            }
        }

        return null;
    }

    private static async Task<ProductLookupReadModel> ReplenishSmokeProductStockAsync(
        SalrynLocalComposition composition,
        ProductLookupReadModel product,
        Guid operatorUserId,
        long targetStockX1000)
    {
        long currentStockX1000 = product.StockOnHand.ValueX1000;
        if (currentStockX1000 >= targetStockX1000)
        {
            return product;
        }

        long replenishAmountX1000 = targetStockX1000 - currentStockX1000;
        await AddStockInMovementAsync(
                composition,
                product.Id,
                operatorUserId,
                replenishAmountX1000,
                "V2 host checkout smoke stock replenishment")
            .ConfigureAwait(false);

        ProductLookupReadModel? refreshed = RequireSuccess(
            await composition.ProductReadRepository
                .FindByIdAsync(product.Id, CancellationToken.None)
                .ConfigureAwait(false),
            "V2Checkout.ReadReplenishedProduct");

        if (refreshed is null)
        {
            throw new InvalidOperationException("V2 checkout runtime could not read replenished smoke product.");
        }

        if (refreshed.StockOnHand.ValueX1000 < CheckoutLineQuantityX1000)
        {
            throw new InvalidOperationException(
                $"V2 checkout smoke replenishment failed. Product {refreshed.Id} ({refreshed.Name}) stockX1000={refreshed.StockOnHand.ValueX1000}, required>={CheckoutLineQuantityX1000}.");
        }

        return refreshed;
    }

    private static async Task DrainProductStockAsync(
        SalrynLocalComposition composition,
        ProductLookupReadModel product,
        Guid operatorUserId)
    {
        long currentStockX1000 = product.StockOnHand.ValueX1000;
        if (currentStockX1000 <= 0)
        {
            return;
        }

        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        IInventoryWriteRepository inventoryWriteRepository = repositories.CreateInventoryWriteRepository();
        Result<Quantity> quantity = Quantity.FromX1000(currentStockX1000);
        if (quantity.IsFailure)
        {
            throw new InvalidOperationException("V2 checkout runtime could not create drain quantity.");
        }

        StockMovement adjustmentRemove = new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            MovementType = StockMovementType.AdjustmentRemove,
            QuantityChange = quantity.Value,
            CostPrice = product.CostPrice ?? Money.FromCents(1000),
            ReferenceType = "V2-HOST-CHECKOUT-SMOKE",
            ReferenceId = Guid.NewGuid(),
            Reason = "V2 host checkout depleted-product reset",
            UserId = operatorUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        RequireSuccess(
            await inventoryWriteRepository
                .AddStockMovementAsync(adjustmentRemove, CancellationToken.None)
                .ConfigureAwait(false),
            "V2Checkout.DrainDepletedProduct");
    }

    private static async Task AddStockInMovementAsync(
        SalrynLocalComposition composition,
        Guid productId,
        Guid operatorUserId,
        long quantityX1000,
        string reason)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        IInventoryWriteRepository inventoryWriteRepository = repositories.CreateInventoryWriteRepository();
        Result<Quantity> quantity = Quantity.FromX1000(quantityX1000);
        if (quantity.IsFailure)
        {
            throw new InvalidOperationException("V2 checkout runtime could not create stock-in quantity.");
        }

        StockMovement stockIn = new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MovementType = StockMovementType.StockIn,
            QuantityChange = quantity.Value,
            CostPrice = Money.FromCents(1000),
            ReferenceType = "V2-HOST-CHECKOUT-SMOKE",
            ReferenceId = Guid.NewGuid(),
            Reason = reason,
            UserId = operatorUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        RequireSuccess(
            await inventoryWriteRepository
                .AddStockMovementAsync(stockIn, CancellationToken.None)
                .ConfigureAwait(false),
            "V2Checkout.StockIn");
    }

    private static async Task<ProductLookupReadModel> CreateSmokeCheckoutProductAsync(
        SalrynLocalComposition composition,
        Guid operatorUserId,
        string productName,
        long initialStockX1000,
        string description)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        IProductWriteRepository productWriteRepository = repositories.CreateProductWriteRepository();
        ICatalogReadRepository catalogRepository = composition.CatalogReadRepository;

        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            await catalogRepository.GetCategoriesAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false),
            "V2Checkout.GetCategories");
        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            await catalogRepository.GetBrandsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false),
            "V2Checkout.GetBrands");
        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            await catalogRepository.GetUnitsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false),
            "V2Checkout.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("V2 checkout runtime requires seeded catalog data.");
        }

        Result<Barcode> barcode = Barcode.Create("99" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (barcode.IsFailure)
        {
            throw new InvalidOperationException("V2 checkout runtime could not create barcode.");
        }

        Result<Quantity> stock = Quantity.FromX1000(initialStockX1000);
        if (stock.IsFailure)
        {
            throw new InvalidOperationException("V2 checkout runtime could not create stock quantity.");
        }

        Product product = new()
        {
            Id = Guid.NewGuid(),
            Sku = "V2CHK-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Barcode = barcode.Value,
            Name = productName,
            Description = description,
            CategoryId = categories[0].Id,
            BrandId = brands[0].Id,
            UnitId = units[0].Id,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1500),
            WholesalePrice = null,
            StockOnHand = stock.Value,
            ReorderLevel = stock.Value,
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "V2 host checkout smoke",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };

        RequireSuccess(
            await productWriteRepository.AddAsync(product, CancellationToken.None).ConfigureAwait(false),
            "V2Checkout.AddProduct");

        if (initialStockX1000 == 0)
        {
            ProductLookupReadModel? created = RequireSuccess(
                await composition.ProductReadRepository
                    .FindByIdAsync(product.Id, CancellationToken.None)
                    .ConfigureAwait(false),
                "V2Checkout.ReadCreatedProduct");

            if (created is null)
            {
                throw new InvalidOperationException("V2 checkout runtime could not read created depleted product.");
            }

            return created;
        }

        ProductLookupReadModel? createdWithStock = RequireSuccess(
            await composition.ProductReadRepository
                .FindByIdAsync(product.Id, CancellationToken.None)
                .ConfigureAwait(false),
            "V2Checkout.ReadCreatedProduct");

        if (createdWithStock is null)
        {
            throw new InvalidOperationException("V2 checkout runtime could not read created smoke product.");
        }

        return await ReplenishSmokeProductStockAsync(
                composition,
                createdWithStock,
                operatorUserId,
                Math.Max(initialStockX1000, SmokeTargetStockX1000))
            .ConfigureAwait(false);
    }

    private static HostOperatorCheckoutRequest BuildCheckoutRequest(
        IReadOnlyList<HostOperatorCheckoutLineRequest> lines,
        IReadOnlyList<HostOperatorCheckoutPaymentRequest> payments) =>
        new(lines, payments);

    private static IReadOnlyList<HostOperatorCheckoutPaymentRequest> BuildCashPayment(long amountCents) =>
        new[] { new HostOperatorCheckoutPaymentRequest("Cash", amountCents) };

    private static async Task EnsureNoOpenShiftAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/shift").ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        string? state = document.RootElement.GetProperty("shift").GetProperty("shiftState").GetString();
        if (string.Equals(state, "Open", StringComparison.Ordinal))
        {
            await PostShiftCloseAsync(client).ConfigureAwait(false);
        }
    }

    private static async Task PostShiftOpenAsync(HttpClient client, long openingCashCents)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/shift/open", new HostOperatorShiftOpenRequest(openingCashCents))
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Expected POST /api/shift/open to return 200, got {(int)response.StatusCode}.");
        }
    }

    private static async Task PostShiftCloseAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsync("/api/shift/close", null).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Expected POST /api/shift/close to return 200, got {(int)response.StatusCode}.");
        }
    }

    private static async Task<JsonDocument> PostCheckoutAsync(
        HttpClient client,
        HostOperatorCheckoutRequest request,
        ProductLookupReadModel? diagnosticProduct = null)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/checkout", request)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string diagnostics = BuildCheckoutDiagnostics(diagnosticProduct, request, body);
            throw new InvalidOperationException(
                $"Expected POST /api/checkout to return 200, got {(int)response.StatusCode}. {diagnostics}");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static string BuildCheckoutDiagnostics(
        ProductLookupReadModel? product,
        HostOperatorCheckoutRequest request,
        string body)
    {
        string productPart = product is null
            ? "product=unknown"
            : $"productId={product.Id:D} productName={product.Name} stockX1000={product.StockOnHand.ValueX1000}";

        long requestedQtyX1000 = request.Lines.Count > 0 ? request.Lines[0].QuantityX1000 : 0;
        string code = "unknown";
        string checkoutStatus = "unknown";

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out JsonElement codeElement))
            {
                code = codeElement.GetString() ?? code;
            }

            if (document.RootElement.TryGetProperty("checkout", out JsonElement checkoutElement) &&
                checkoutElement.TryGetProperty("status", out JsonElement statusElement))
            {
                checkoutStatus = statusElement.GetString() ?? checkoutStatus;
            }
        }
        catch (JsonException)
        {
            // Keep default diagnostic values when the body is not JSON.
        }

        return
            $"Body: {body} Diagnostics: {productPart} requestedQtyX1000={requestedQtyX1000} backendCode={code} checkoutStatus={checkoutStatus}";
    }

    private static async Task AssertCheckoutConflictAsync(
        HttpClient client,
        HostOperatorCheckoutRequest request,
        string expectedCode,
        string expectedCheckoutStatus,
        string message)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/checkout", request)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{message} Expected {(int)HttpStatusCode.Conflict}, got {(int)response.StatusCode}. Body: {body}");
        }

        string conflictBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(conflictBody);
        string? code = document.RootElement.TryGetProperty("code", out JsonElement codeElement)
            ? codeElement.GetString()
            : null;
        string? checkoutStatus = document.RootElement.TryGetProperty("checkout", out JsonElement checkoutElement) &&
                                 checkoutElement.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString()
            : null;

        if (!string.Equals(code, expectedCode, StringComparison.Ordinal) ||
            !string.Equals(checkoutStatus, expectedCheckoutStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{message} Expected code={expectedCode} checkout.status={expectedCheckoutStatus}, got code={code} checkout.status={checkoutStatus}. Body: {conflictBody}");
        }
    }

    private static async Task AssertCheckoutStatusAsync(
        HttpClient client,
        HostOperatorCheckoutRequest request,
        HttpStatusCode expectedStatus,
        string message)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/checkout", request)
            .ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}. Body: {body}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Message}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Message}");
        }
    }

    private static HttpClient CreateClient(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new()
        {
            BaseAddress = baseAddress
        };

        if (session is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookieName}={session.SessionId}");
        }

        return client;
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (address is null)
        {
            throw new InvalidOperationException("Local API did not start on loopback.");
        }

        return new Uri(address);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
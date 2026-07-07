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
        AssertContains(navigation, "enabled: false", "React checkout nav must remain disabled.");
    }

    private static async Task RunRuntimeChecksAsync(string projectRoot)
    {
        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(projectRoot);
        await composition.InitializeDatabaseAsync().ConfigureAwait(false);

        AuthenticatedUserReadModel operatorUser = await EnsureCheckoutOperatorAsync(composition)
            .ConfigureAwait(false);
        ProductLookupReadModel product = await EnsureCheckoutProductAsync(composition).ConfigureAwait(false);

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
                new[] { new HostOperatorCheckoutLineRequest(product.Id, 1000) },
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
                new[] { new HostOperatorCheckoutLineRequest(product.Id, 1000) },
                BuildCashPayment(Math.Max(1, lineTotal - 1)));

            await AssertCheckoutStatusAsync(
                operatorClient,
                insufficientPaymentRequest,
                HttpStatusCode.BadRequest,
                "Checkout with insufficient payment must return 400.").ConfigureAwait(false);

            HostOperatorCheckoutRequest validRequest = BuildCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(product.Id, 1000) },
                BuildCashPayment(lineTotal + 500));

            JsonDocument completed = await PostCheckoutAsync(operatorClient, validRequest).ConfigureAwait(false);
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
        SalrynLocalComposition composition)
    {
        Result<PagedResult<ProductListItemReadModel>> searchResult = await composition.ProductReadRepository
            .SearchAsync(
                new ProductSearchFilter(
                    null,
                    null,
                    null,
                    true,
                    IncludeInactive: false,
                    new PageRequest(1, 1)),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (searchResult.IsSuccess && searchResult.Value.Items.Count > 0)
        {
            Guid productId = searchResult.Value.Items[0].Id;
            Result<ProductLookupReadModel?> existing = await composition.ProductReadRepository
                .FindByIdAsync(productId, CancellationToken.None)
                .ConfigureAwait(false);

            if (existing.IsSuccess && existing.Value is not null)
            {
                return existing.Value;
            }
        }

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

        Result<Quantity> stock = Quantity.FromX1000(5000);
        if (stock.IsFailure)
        {
            throw new InvalidOperationException("V2 checkout runtime could not create stock quantity.");
        }

        Product product = new()
        {
            Id = Guid.NewGuid(),
            Sku = "V2CHK-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Barcode = barcode.Value,
            Name = "V2 Host Checkout Smoke Product",
            Description = "Temporary V2 host checkout runtime product.",
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

        ProductLookupReadModel? created = RequireSuccess(
            await composition.ProductReadRepository.FindByIdAsync(product.Id, CancellationToken.None).ConfigureAwait(false),
            "V2Checkout.ReadCreatedProduct");

        if (created is null)
        {
            throw new InvalidOperationException("V2 checkout runtime could not read created product.");
        }

        return created;
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
        HostOperatorCheckoutRequest request)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/checkout", request)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Expected POST /api/checkout to return 200, got {(int)response.StatusCode}. Body: {body}");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
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
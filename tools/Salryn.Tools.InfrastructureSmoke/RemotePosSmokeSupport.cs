using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

// LAN-M16: shared seeding/build helpers for the host-owned remote POS runtime smokes. These
// exercise the REAL host-side services (composition, repositories, checkout runtime) against a
// real development SQLite database. The remote client never touches the database directly; these
// helpers stand in for the host process that owns the DB.
internal static class RemotePosSmokeSupport
{
    public static void EnsureDatabaseReady(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        bool needsBootstrap = !File.Exists(orchestrator.DatabasePath);

        if (!needsBootstrap)
        {
            DatabaseInitializationStatus status = RequireSuccess(orchestrator.CheckStatus(), "RemotePos.CheckStatus");
            needsBootstrap = !status.FirstRunStatus.IsDatabaseInitialized || status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    public static RemotePosApiServices CreateServices(
        string projectRoot,
        out SalrynLocalComposition composition,
        out RemotePosRuntimeGate runtimeGate)
    {
        composition = SalrynLocalComposition.CreateDevelopment(projectRoot);
        LanRemoteTransportApiServices lanServices = LanRemoteTransportApiComposition.Create(composition);
        runtimeGate = new RemotePosRuntimeGate();
        return RemotePosApiServices.Create(composition, lanServices, runtimeGate);
    }

    public static SqliteRepositoryFactory RepositoryFactory(SalrynLocalComposition composition) =>
        new(composition.ConnectionFactory);

    public static UserReadModel GetCashier(SalrynLocalComposition composition)
    {
        IReadOnlyList<UserReadModel> users = RequireSuccess(
            composition.UserReadRepository.GetAllAsync().GetAwaiter().GetResult(),
            "RemotePos.GetAllUsers");

        UserReadModel? active = users.FirstOrDefault(u => u.Status == UserStatus.Active) ?? users.FirstOrDefault();

        if (active is null)
        {
            throw new InvalidOperationException("RemotePos smoke expected at least one seeded user.");
        }

        return active;
    }

    public static SeedIds GetSeedIds(SalrynLocalComposition composition)
    {
        ICatalogReadRepository catalog = RepositoryFactory(composition).CreateCatalogReadRepository();

        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(catalog.GetCategoriesAsync().GetAwaiter().GetResult(), "RemotePos.Categories");
        IReadOnlyList<BrandReadModel> brands = RequireSuccess(catalog.GetBrandsAsync().GetAwaiter().GetResult(), "RemotePos.Brands");
        IReadOnlyList<UnitReadModel> units = RequireSuccess(catalog.GetUnitsAsync().GetAwaiter().GetResult(), "RemotePos.Units");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("RemotePos smoke seed lookup failed.");
        }

        return new SeedIds(categories[0].Id, brands[0].Id, units[0].Id, units[0].Name);
    }

    public static Product CreateTrackedProduct(SeedIds seedIds, string name, long initialStockX1000, long sellingPriceCents)
    {
        Barcode barcode = RequireValue(Barcode.Create(NewBarcode()), "RemotePos.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "RPOS-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = name,
            Description = "Temporary LAN-M16 remote POS smoke product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(sellingPriceCents),
            WholesalePrice = null,
            StockOnHand = RequireValue(Quantity.FromX1000(initialStockX1000), "RemotePos.Stock"),
            ReorderLevel = RequireValue(Quantity.FromX1000(1000), "RemotePos.Reorder"),
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "LAN-M16 remote POS smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static Product SeedTrackedProduct(SalrynLocalComposition composition, string name, long stockX1000, long sellingPriceCents)
    {
        SeedIds seedIds = GetSeedIds(composition);
        Product product = CreateTrackedProduct(seedIds, name, stockX1000, sellingPriceCents);

        RequireSuccess(
            RepositoryFactory(composition).CreateProductWriteRepository().AddAsync(product).GetAwaiter().GetResult(),
            "RemotePos.AddProduct");

        return product;
    }

    // Builds a host-issued-shaped operator session directly. The checkout runtime consumes the
    // session identity + permission snapshot; the login/token issuance path is exercised separately
    // by the operator-session smoke. StoreId/HostId/TerminalId are used for the idempotency key and
    // the audit trail (not as DB foreign keys), so synthetic ids are safe here.
    public static RemotePosOperatorSession CreateSession(Guid userId, string roleName, params string[] permissions)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new RemotePosOperatorSession
        {
            SessionId = Guid.NewGuid(),
            TokenHash = new byte[32],
            TerminalId = Guid.NewGuid(),
            HandshakeSessionId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            UserId = userId,
            Username = "remote-pos-smoke",
            DisplayName = "Remote POS Smoke Operator",
            RoleName = roleName,
            RoleId = Guid.NewGuid(),
            Permissions = permissions,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(RemotePosOperatorSessionService.SessionLifetime),
            RevokedAtUtc = null,
        };
    }

    public static void ForceCloseOpenShift(ICashierSessionService cashierSessions, Guid userId)
    {
        CashierSessionReadModel? open = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(userId).GetAwaiter().GetResult(),
            "RemotePos.PreExistingOpenLookup");

        if (open is null)
        {
            return;
        }

        RequireSuccess(
            cashierSessions.ForceCloseAsync(new ForceCloseCashierSessionRequest(open.Id, userId, null, "LAN-M16 remote POS smoke cleanup"))
                .GetAwaiter().GetResult(),
            "RemotePos.ForceCloseOpen");
    }

    public static ICashierSessionService CashierSessions(SalrynLocalComposition composition)
    {
        SqliteRepositoryFactory repositories = RepositoryFactory(composition);
        return new CashierSessionService(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());
    }

    public static int CountSalesByReceipt(string projectRoot, string receiptNo)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sales WHERE receipt_no = $receipt;";
        command.Parameters.AddWithValue("$receipt", receiptNo);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public static int CountStockMovementsForSale(string projectRoot, Guid saleId)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE reference_type = 'SALE' AND reference_id = $saleId;";
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public static long GetProductStockX1000(SalrynLocalComposition composition, Guid productId)
    {
        ProductLookupReadModel? product = RequireSuccess(
            composition.ProductReadRepository.FindByIdAsync(productId).GetAwaiter().GetResult(),
            "RemotePos.ProductStock");

        if (product is null)
        {
            throw new InvalidOperationException("RemotePos smoke could not read product stock.");
        }

        return product.StockOnHand.ValueX1000;
    }

    public static SqliteConnection OpenConnection(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        SqliteConnection connection = new($"Data Source={orchestrator.DatabasePath}");
        connection.Open();
        return connection;
    }

    private static string NewBarcode() =>
        "97" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(100, 999);

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    public static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    public static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    internal sealed record SeedIds(Guid CategoryId, Guid BrandId, Guid UnitId, string UnitName);
}

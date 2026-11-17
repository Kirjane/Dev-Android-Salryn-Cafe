using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories.Products;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// Executes only against a newly initialized disposable SQLite DB. Never point this at installed data.
/// Called by the existing POS-M4 CoreSmoke entrypoint.
/// </summary>
public static class PilotCashierFastAddAtomicityChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-pilot-fastadd-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            var admin = composition.DatabaseInitialization.CreateFirstAdmin(
                new AdminBootstrapRequest("Fast Add QA Store", null, null, null, null, 80,
                    "fastadd_admin", "Fast Add QA Admin", "StrongAdmin123!"));
            Require(admin.IsSuccess, "Disposable DB first-admin bootstrap failed.");

            Guid operatorId;
            using (SqliteConnection connection = Open(composition))
            {
                using SqliteCommand query = connection.CreateCommand();
                query.CommandText = "SELECT id FROM users WHERE username = 'fastadd_admin' LIMIT 1;";
                operatorId = Guid.Parse((string)query.ExecuteScalar()!);
            }

            Require(Scalar(composition, """
                SELECT COUNT(*) FROM role_permissions WHERE role_id = '00000000-0000-0000-0000-000000000003'
                AND permission = 'Products.FastAdd';
                """) == 1, "Fresh cashier seed must carry Products.FastAdd.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM role_permissions WHERE role_id = '00000000-0000-0000-0000-000000000003'
                AND permission IN ('Inventory.View', 'Inventory.Adjust', 'Products.Manage', 'OfficialDocument.Refund');
                """) == 0, "Cashier seed must not carry broad stock, product edit or refund access.");

            // Mimic an upgraded role DB that predates Products.FastAdd and preserves custom additions.
            using (SqliteConnection connection = Open(composition))
            {
                using SqliteCommand change = connection.CreateCommand();
                change.CommandText = """
                    DELETE FROM role_permissions
                    WHERE role_id = $cashier AND permission = 'Products.FastAdd';
                    INSERT INTO role_permissions (id, role_id, permission, created_at)
                    VALUES ($id, $cashier, 'Custom.LocalPolicy', $at);
                    """;
                change.Parameters.AddWithValue("$cashier", DefaultSeedData.CashierRoleId);
                change.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                change.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                change.ExecuteNonQuery();
            }
            new DatabaseSeeder(composition.ConnectionFactory).SeedDefaultData();
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM role_permissions WHERE role_id = '00000000-0000-0000-0000-000000000003'
                AND permission IN ('Products.FastAdd', 'Custom.LocalPolicy');
                """) == 2, "Upgrade seeding must restore bounded permission without dropping a custom permission.");

            var units = composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult();
            Require(units.IsSuccess && units.Value.Count > 0, "Fresh catalog must contain units.");
            Guid unitId = units.Value[0].Id;

            SqliteFastAddProductWriteRepository repository = new(composition.ConnectionFactory);
            Guid firstId = Guid.NewGuid();
            var qty = Quantity.FromX1000(3000);
            Require(qty.IsSuccess, "Positive stock fixture invalid.");
            var first = repository.CreateAsync(Product(firstId, "FASTADD-R1-UNIQUE", unitId), qty.Value, operatorId)
                .GetAwaiter().GetResult();
            Require(first.IsSuccess, "Positive Fast Add transaction must commit.");
            Require(Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;", firstId) == 3000,
                "Product stock must be exactly initial stock.");
            Require(Scalar(composition, """
                SELECT quantity_x1000 FROM product_stock_buckets WHERE product_id = $id AND bucket_status='Available';
                """, firstId) == 3000, "MAIN-STORE bucket must equal product stock.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM stock_movements WHERE product_id = $id AND reference_type='POS_M4_FAST_ADD';
                """, firstId) == 1, "Positive initial stock must create exactly one stock-in.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id AND action='PRODUCT_CREATED';
                """, firstId) == 1, "Positive-stock Fast Add must have exactly one operator audit.");

            var duplicate = repository.CreateAsync(Product(Guid.NewGuid(), "FASTADD-R1-UNIQUE", unitId), qty.Value, operatorId)
                .GetAwaiter().GetResult();
            Require(duplicate.IsFailure && duplicate.ErrorDetail.Code == "Products.DuplicateBarcode",
                "Retry with same barcode must conflict without extra stock.");

            using (SqliteConnection connection = Open(composition))
            {
                using SqliteCommand createTrigger = connection.CreateCommand();
                createTrigger.CommandText = """
                    CREATE TRIGGER qa_fastadd_fail_stock BEFORE INSERT ON stock_movements
                    WHEN NEW.reference_type='POS_M4_FAST_ADD'
                    BEGIN SELECT RAISE(ABORT, 'Injected stock stage failure'); END;
                    """;
                createTrigger.ExecuteNonQuery();
            }
            Guid failedId = Guid.NewGuid();
            var failed = repository.CreateAsync(Product(failedId, "FASTADD-R1-ROLLBACK", unitId), qty.Value, operatorId)
                .GetAwaiter().GetResult();
            Require(failed.IsFailure, "Injected stock stage failure must reject Fast Add.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM products WHERE id=$id;", failedId) == 0,
                "Injected stock failure must roll back product.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM product_stock_buckets WHERE product_id=$id;", failedId) == 0,
                "Injected stock failure must roll back bucket.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id;", failedId) == 0,
                "Injected stock failure must roll back creation audit.");
            using (SqliteConnection connection = Open(composition))
            {
                using SqliteCommand drop = connection.CreateCommand();
                drop.CommandText = "DROP TRIGGER qa_fastadd_fail_stock;";
                drop.ExecuteNonQuery();
            }
            Require(repository.CreateAsync(Product(failedId, "FASTADD-R1-ROLLBACK", unitId), qty.Value, operatorId)
                .GetAwaiter().GetResult().IsSuccess, "Retry after rollback must create exactly once.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM stock_movements WHERE product_id=$id;", failedId) == 1,
                "Retry after rollback must create only one stock movement.");

            Guid zeroId = Guid.NewGuid();
            Require(repository.CreateAsync(Product(zeroId, "FASTADD-R1-ZERO", unitId), Quantity.Zero, operatorId)
                .GetAwaiter().GetResult().IsSuccess, "Zero-stock Fast Add must succeed.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM stock_movements WHERE product_id=$id;", zeroId) == 0,
                "Zero stock must not create synthetic stock-in.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id;", zeroId) == 1,
                "Zero-stock product must still have operator-attributed audit.");
            Require(Scalar(composition, "SELECT quantity_x1000 FROM product_stock_buckets WHERE product_id=$id;", zeroId) == 0,
                "Zero-stock product must initialize zero MAIN-STORE bucket.");

            // Separate concurrent callers race on the same barcode: one active product at most.
            var race = Task.WhenAll(
                Task.Run(() => repository.CreateAsync(Product(Guid.NewGuid(), "FASTADD-R1-RACE", unitId), qty.Value, operatorId).GetAwaiter().GetResult()),
                Task.Run(() => repository.CreateAsync(Product(Guid.NewGuid(), "FASTADD-R1-RACE", unitId), qty.Value, operatorId).GetAwaiter().GetResult()))
                .GetAwaiter().GetResult();
            Require(race.Count(result => result.IsSuccess) == 1,
                "Concurrent same-barcode Fast Add must commit exactly one product.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM products WHERE barcode='FASTADD-R1-RACE';") == 1,
                "Concurrent barcode must remain unique.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM stock_movements sm INNER JOIN products p ON p.id=sm.product_id
                WHERE p.barcode='FASTADD-R1-RACE' AND sm.reference_type='POS_M4_FAST_ADD';
                """) == 1, "Concurrent barcode must create one initial stock movement.");

            RunHttpMatrixAsync(composition, operatorId, unitId).GetAwaiter().GetResult();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task RunHttpMatrixAsync(
        SalrynLocalComposition composition,
        Guid adminUserId,
        Guid unitId)
    {
        const string cashierUsername = "fastadd_http_cashier";
        const string cashierPassword = "FastAddCashier123!";
        const string readerUsername = "fastadd_http_reader";
        const string readerPassword = "FastAddReader123!";
        Guid cashierRoleId = Guid.Parse(DefaultSeedData.CashierRoleId);
        UserAdminApiService userAdmin = new(composition);
        Require((await userAdmin.CreateUserAsync(new CreateUserApiRequest(
            cashierUsername, "Fast Add HTTP Cashier", cashierPassword, cashierPassword,
            cashierRoleId, true), adminUserId).ConfigureAwait(false)).IsSuccess,
            "Persisted Fast Add Cashier creation failed.");
        Require((await userAdmin.CreateUserAsync(new CreateUserApiRequest(
            readerUsername, "Fast Add HTTP Reader", readerPassword, readerPassword,
            cashierRoleId, true), adminUserId).ConfigureAwait(false)).IsSuccess,
            "Persisted insufficient-permission user creation failed.");
        Guid cashierUserId = Guid.Parse(ScalarText(composition,
            "SELECT id FROM users WHERE username='fastadd_http_cashier';"));
        Guid readerUserId = Guid.Parse(ScalarText(composition,
            "SELECT id FROM users WHERE username='fastadd_http_reader';"));

        InMemoryLocalApiSessionStore sessionStore = new();
        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                SessionStore = sessionStore,
                InitializeDatabase = false,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }).ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);
        try
        {
            string? boundUrl = app.Urls.FirstOrDefault(url =>
                url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) &&
                !url.EndsWith(":0", StringComparison.Ordinal));
            Require(!string.IsNullOrWhiteSpace(boundUrl), "Fast Add HTTP host did not bind a loopback port.");
            Uri baseAddress = new(boundUrl!);

            using HttpClient anonymousClient = new() { BaseAddress = baseAddress };
            var cashierLogin = await LoginClientAsync(baseAddress, cashierUsername, cashierPassword).ConfigureAwait(false);
            using HttpClient cashierClient = cashierLogin.Client;
            Require(cashierLogin.UserId == cashierUserId && cashierLogin.RoleName == "Cashier",
                "Normal login must authenticate the persisted Cashier identity and role.");
            Require(cashierLogin.Permissions.Contains(LocalApiPermissions.ProductsFastAdd) &&
                    cashierLogin.Permissions.Contains(LocalApiPermissions.ProductsView) &&
                    !cashierLogin.Permissions.Contains(LocalApiPermissions.ProductsManage) &&
                    !cashierLogin.Permissions.Contains(LocalApiPermissions.InventoryAdjust),
                "Persisted Cashier login must carry bounded Fast Add without broad catalog/stock authority.");

            using (SqliteConnection connection = Open(composition))
            {
                using SqliteCommand removeFastAdd = connection.CreateCommand();
                removeFastAdd.CommandText = "DELETE FROM role_permissions WHERE role_id=$role AND permission='Products.FastAdd';";
                removeFastAdd.Parameters.AddWithValue("$role", DefaultSeedData.CashierRoleId);
                removeFastAdd.ExecuteNonQuery();
            }
            var readerLogin = await LoginClientAsync(baseAddress, readerUsername, readerPassword).ConfigureAwait(false);
            using HttpClient unauthorizedClient = readerLogin.Client;
            Require(readerLogin.UserId == readerUserId && readerLogin.RoleName == "Cashier",
                "Normal login must authenticate the persisted insufficient-permission identity.");
            Require(readerLogin.Permissions.Contains(LocalApiPermissions.ProductsView) &&
                    !readerLogin.Permissions.Contains(LocalApiPermissions.ProductsFastAdd) &&
                    !readerLogin.Permissions.Contains(LocalApiPermissions.ProductsManage) &&
                    !readerLogin.Permissions.Contains(LocalApiPermissions.InventoryAdjust),
                "Insufficient-permission login must retain read access but lack Fast Add and broad stock authority.");
            new DatabaseSeeder(composition.ConnectionFactory).SeedDefaultData();

            object validPayload = new
            {
                productName = "HTTP Fast Add Product",
                barcode = "FASTADD-R2-HTTP",
                unitId,
                sellingPrice = 12.50m,
                initialStockQuantity = 4m
            };

            long productsBefore = Scalar(composition, "SELECT COUNT(*) FROM products WHERE barcode='FASTADD-R2-HTTP';");
            FastAddStateSnapshot beforeAnonymous = Snapshot(composition);
            HttpResponseMessage unauthenticated = await anonymousClient.PostAsJsonAsync("/api/products/fast-add", validPayload).ConfigureAwait(false);
            Require(unauthenticated.StatusCode == HttpStatusCode.Unauthorized,
                $"Unauthenticated Fast Add must be 401, got {(int)unauthenticated.StatusCode}.");
            Require(Snapshot(composition) == beforeAnonymous, "401 Fast Add must not mutate product, stock or audit state.");
            FastAddStateSnapshot beforeForbidden = Snapshot(composition);
            HttpResponseMessage forbidden = await unauthorizedClient.PostAsJsonAsync("/api/products/fast-add", validPayload).ConfigureAwait(false);
            Require(forbidden.StatusCode == HttpStatusCode.Forbidden,
                $"Products.View-only Fast Add must be 403, got {(int)forbidden.StatusCode}.");
            Require(Snapshot(composition) == beforeForbidden, "403 Fast Add must not mutate product, stock or audit state.");

            object invalidPayload = new
            {
                productName = "",
                barcode = "FASTADD-R2-INVALID",
                unitId,
                sellingPrice = 0m,
                initialStockQuantity = -1m
            };
            FastAddStateSnapshot beforeInvalid = Snapshot(composition);
            HttpResponseMessage invalid = await cashierClient.PostAsJsonAsync("/api/products/fast-add", invalidPayload).ConfigureAwait(false);
            Require(invalid.StatusCode == HttpStatusCode.BadRequest,
                $"Invalid Fast Add must be 400, got {(int)invalid.StatusCode}.");
            Require(Snapshot(composition) == beforeInvalid, "400 Fast Add must not mutate product, stock or audit state.");

            HttpResponseMessage created = await cashierClient.PostAsJsonAsync("/api/products/fast-add", validPayload).ConfigureAwait(false);
            Require(created.StatusCode == HttpStatusCode.OK,
                $"Valid Fast Add must be 200, got {(int)created.StatusCode}.");
            FastAddStateSnapshot beforeDuplicate = Snapshot(composition);
            HttpResponseMessage duplicate = await cashierClient.PostAsJsonAsync("/api/products/fast-add", validPayload).ConfigureAwait(false);
            Require(duplicate.StatusCode == HttpStatusCode.Conflict,
                $"Duplicate Fast Add must be 409, got {(int)duplicate.StatusCode}.");
            Require(Snapshot(composition) == beforeDuplicate, "409 Fast Add must not mutate product, stock or audit state.");

            Require(Scalar(composition, "SELECT COUNT(*) FROM products WHERE barcode='FASTADD-R2-HTTP';") == productsBefore + 1,
                "HTTP matrix must persist exactly one product.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM product_stock_buckets b
                INNER JOIN products p ON p.id=b.product_id
                INNER JOIN inventory_locations l ON l.id=b.location_id
                WHERE p.barcode='FASTADD-R2-HTTP' AND l.code='MAIN-STORE' AND b.quantity_x1000=4000;
                """) == 1, "HTTP Fast Add must persist one MAIN-STORE bucket at quantity 4.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM stock_movements sm INNER JOIN products p ON p.id=sm.product_id
                WHERE p.barcode='FASTADD-R2-HTTP' AND sm.reference_type='POS_M4_FAST_ADD';
                """) == 1, "HTTP Fast Add must persist one initial-stock movement.");
            Require(Scalar(composition, """
                SELECT COUNT(*) FROM audit_logs a INNER JOIN products p ON p.id=a.entity_id
                WHERE p.barcode='FASTADD-R2-HTTP' AND a.action='PRODUCT_CREATED';
                """) == 1, "HTTP Fast Add must persist one creation audit.");
            Require(ScalarText(composition, """
                SELECT sm.user_id FROM stock_movements sm INNER JOIN products p ON p.id=sm.product_id
                WHERE p.barcode='FASTADD-R2-HTTP' AND sm.reference_type='POS_M4_FAST_ADD';
                """) == cashierUserId.ToString("D"), "HTTP stock movement must identify the authenticated persisted Cashier.");
            Require(ScalarText(composition, """
                SELECT a.user_id FROM audit_logs a INNER JOIN products p ON p.id=a.entity_id
                WHERE p.barcode='FASTADD-R2-HTTP' AND a.action='PRODUCT_CREATED';
                """) == cashierUserId.ToString("D"), "HTTP creation audit must identify the authenticated persisted Cashier.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM products WHERE barcode='FASTADD-R2-INVALID';") == 0,
                "Invalid HTTP request must leave no product.");
            Require(Scalar(composition, "SELECT COUNT(*) FROM products WHERE barcode='FASTADD-R2-HTTP';") == 1,
                "401/403/409 requests must not add partial or duplicate products.");

            Console.WriteLine("[FASTADD HTTP] 200 valid / 400 invalid / 401 anonymous / 403 unauthorized / 409 duplicate; product=1 bucket=1 movement=1 audit=1.");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task<LoginEvidence> LoginClientAsync(Uri baseAddress, string username, string password)
    {
        HttpClient client = new(new HttpClientHandler { UseCookies = true }) { BaseAddress = baseAddress };
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { username, password }).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Require(response.StatusCode == HttpStatusCode.OK,
            $"Normal login for {username} failed: {(int)response.StatusCode} {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Require(root.GetProperty("isAuthenticated").GetBoolean(), $"Login response for {username} must be authenticated.");
        JsonElement user = root.GetProperty("user");
        return new LoginEvidence(
            client,
            user.GetProperty("userId").GetGuid(),
            user.GetProperty("roleName").GetString() ?? string.Empty,
            user.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    private static FastAddStateSnapshot Snapshot(SalrynLocalComposition composition) => new(
        Scalar(composition, "SELECT COUNT(*) FROM products;"),
        Scalar(composition, "SELECT COUNT(*) FROM product_stock_buckets;"),
        Scalar(composition, "SELECT COUNT(*) FROM stock_movements WHERE reference_type='POS_M4_FAST_ADD';"),
        Scalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE action='PRODUCT_CREATED';"));

    private sealed record LoginEvidence(HttpClient Client, Guid UserId, string RoleName, string[] Permissions);
    private sealed record FastAddStateSnapshot(long Products, long Buckets, long Movements, long Audits);

    private static Product Product(Guid id, string barcode, Guid unitId)
    {
        var normalized = Barcode.Create(barcode);
        Require(normalized.IsSuccess, "QA barcode fixture invalid.");
        return new Product
        {
            Id = id,
            Barcode = normalized.Value,
            Name = "Fast Add QA " + barcode,
            UnitId = unitId,
            SellingPrice = Money.FromCents(1250),
            StockOnHand = Quantity.Zero,
            ReorderLevel = Quantity.Zero,
            TrackStock = true,
            AllowDiscount = true,
            TaxClassification = ProductTaxClassification.Vatable,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SqliteConnection Open(SalrynLocalComposition composition)
    {
        SqliteConnection connection = composition.ConnectionFactory.CreateConnection();
        connection.Open();
        SqlitePragma.Apply(connection);
        return connection;
    }

    private static long Scalar(SalrynLocalComposition composition, string sql, Guid? id = null)
    {
        using SqliteConnection connection = Open(composition);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (id.HasValue) command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarText(SalrynLocalComposition composition, string sql)
    {
        using SqliteConnection connection = Open(composition);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Pilot Cashier Fast Add: " + message);
    }
}

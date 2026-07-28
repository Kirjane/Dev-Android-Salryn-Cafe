using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class U1AccountsReceivableRuntimeChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-u1-runtime-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            Guid userId = SeedUser(paths.DatabasePath);
            Guid productId = SeedProduct(paths.DatabasePath);
            SqliteAccountsReceivableService receivables = new(composition.ConnectionFactory);
            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            Require((await host.ShiftService.OpenShiftAsync(userId, 0)).Ok, "U1 runtime shift must open.");

            ReceivableCustomer paidCustomer = await CreateCustomerAsync(receivables, userId, "Authoritative Paid", "09170000001");
            ReceivableCustomer partialCustomer = await CreateCustomerAsync(receivables, userId, "Authoritative Partial", "09170000002");
            ReceivableCustomer reversedCustomer = await CreateCustomerAsync(receivables, userId, "Authoritative Reversed", "09170000003");
            ReceivableCustomer unpaidCustomer = await CreateCustomerAsync(receivables, userId, "Authoritative Unpaid", "09170000004");
            DateOnly overdueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));

            string fullRequestId = Guid.NewGuid().ToString("N");
            HostOperatorCheckoutResponse full = await CheckoutAsync(
                host, userId, productId, fullRequestId,
                new[] { new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 10_000) },
                paidCustomer.Id, overdueDate, "Spoofed Client Name", "09999999999");
            HostOperatorCheckoutResponse fullDuplicate = await CheckoutAsync(
                host, userId, productId, fullRequestId,
                new[] { new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 10_000) },
                paidCustomer.Id, overdueDate, "Spoofed Client Name", "09999999999");
            Require(fullDuplicate.Duplicate && fullDuplicate.SaleId == full.SaleId,
                "Duplicate full Utang checkout must reconcile to the original sale.");
            Require(Count(paths.DatabasePath, "sales", "id", full.SaleId!) == 1,
                "Duplicate full Utang checkout must create one sale.");
            Require(Count(paths.DatabasePath, "accounts_receivable_entries", "sale_id", full.SaleId!) == 1,
                "Duplicate full Utang checkout must create one AR debit.");
            Require(Count(paths.DatabasePath, "stock_movements", "reference_id", full.SaleId!) == 1,
                "Duplicate full Utang checkout must decrement stock once.");

            string receipt = ReadText(paths.DatabasePath, "SELECT receipt_snapshot FROM sales WHERE id=$value;", full.SaleId!);
            Require(receipt.Contains(paidCustomer.FullName, StringComparison.Ordinal) &&
                    receipt.Contains(paidCustomer.Phone!, StringComparison.Ordinal) &&
                    !receipt.Contains("Spoofed Client Name", StringComparison.Ordinal) &&
                    !receipt.Contains("09999999999", StringComparison.Ordinal),
                "Receipt identity must come from the authoritative customer record.");
            Require(ReadLong(paths.DatabasePath,
                    "SELECT COALESCE(SUM(amount_cents),0) FROM payments WHERE sale_id=$value AND method=$method;",
                    full.SaleId!, Convert.ToInt32(PaymentMethod.Cash)) == 0,
                "Full Utang checkout must collect zero cash.");

            HostOperatorCheckoutResponse partial = await CheckoutAsync(
                host, userId, productId, Guid.NewGuid().ToString("N"),
                new[]
                {
                    new HostOperatorCheckoutPaymentRequest("Cash", 4_000),
                    new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 6_000)
                },
                partialCustomer.Id, overdueDate, null, null);
            Require(partial.ChangeCents == 0, "Partial Cash + Utang checkout must return zero change.");

            HostOperatorCheckoutResponse reversed = await CheckoutAsync(
                host, userId, productId, Guid.NewGuid().ToString("N"),
                new[] { new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 10_000) },
                reversedCustomer.Id, overdueDate, null, null);
            Result reversedResult = await composition.SaleWriteRepository.MarkVoidedAsync(
                Guid.Parse(reversed.SaleId!), userId, "U1 aging reversal smoke", Array.Empty<StockMovement>());
            Require(reversedResult.IsSuccess, "Uncollected Utang sale reversal must succeed.");

            _ = await CheckoutAsync(
                host, userId, productId, Guid.NewGuid().ToString("N"),
                new[] { new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 10_000) },
                unpaidCustomer.Id, overdueDate, null, null);

            HostOperatorCheckoutResponse cash = await CheckoutAsync(
                host, userId, productId, Guid.NewGuid().ToString("N"),
                new[] { new HostOperatorCheckoutPaymentRequest("Cash", 20_000) },
                null, null, null, null);
            Require(cash.TotalCents == 10_000 && cash.ChangeCents == 10_000,
                "Overpaid cash smoke must persist sale total and returned change.");

            await AssertCollectionIdempotencyAsync(receivables, paidCustomer.Id, 10_000, userId, paths.DatabasePath);
            await AssertCollectionIdempotencyAsync(receivables, partialCustomer.Id, 3_000, userId, paths.DatabasePath);

            AccountsReceivableSummary summary = RequireSuccess(await receivables.GetSummaryAsync(), "U1 summary");
            Require(summary.TotalReceivablesCents == 13_000 && summary.OverdueCents == 13_000,
                "AR summary must report only remaining unpaid overdue exposure.");
            IReadOnlyList<ReceivableCustomerBalance> balances =
                RequireSuccess(await receivables.SearchCustomersAsync(null), "U1 customer balances");
            Require(Balance(balances, paidCustomer.Id).BalanceCents == 0 &&
                    Balance(balances, paidCustomer.Id).OldestDueDate is null,
                "Fully paid customer must not remain overdue.");
            Require(Balance(balances, partialCustomer.Id).BalanceCents == 3_000 &&
                    Balance(balances, partialCustomer.Id).OldestDueDate == overdueDate,
                "Partially paid customer must retain only remaining overdue exposure.");
            Require(Balance(balances, reversedCustomer.Id).BalanceCents == 0 &&
                    Balance(balances, reversedCustomer.Id).OldestDueDate is null,
                "Sale reversal must remove overdue exposure.");

            OwnerReportsResponse reports = RequireSuccess(
                await new OwnerReportsApiService(composition).GetSummaryAsync(),
                "U1 owner reports");
            Require(reports.Receivables.CashCollectedOnSalesCents == 14_000,
                "Cash collected on sales must exclude returned change and include partial retained cash.");
            Require(reports.Receivables.CollectionsCents == 13_000,
                "AR collections must be reported separately from sales.");
            Require(reports.Receivables.OutstandingCents == 13_000 &&
                    reports.Receivables.OverdueCents == 13_000,
                "Owner report outstanding and overdue must agree with AR summary.");
            await AssertAuthorizationMatrixAsync(composition, userId, unpaidCustomer.Id).ConfigureAwait(false);

            SqliteBackupRecoveryService backup = new(paths);
            string backupPath = RequireSuccess(backup.CreateBackup(), "U1 backup");
            BackupVerificationSummary verified =
                RequireSuccess(backup.VerifyBackup(backupPath), "U1 backup verification");
            Require(verified.IsRestorable, "U1 backup must pass restore verification.");
            RequireSuccess(
                await receivables.AdjustAsync(new(
                    unpaidCustomer.Id, 777, true, "Post-backup mutation", userId)),
                "U1 post-backup mutation");
            RequireSuccess(
                backup.RestoreVerifiedBackup(
                    verified,
                    SqliteBackupRecoveryService.RestoreConfirmationText),
                "U1 restore");
            SqliteAccountsReceivableService restoredReceivables =
                new(new SqliteConnectionFactory(paths));
            AccountsReceivableSummary restoredSummary =
                RequireSuccess(await restoredReceivables.GetSummaryAsync(), "U1 restored summary");
            Require(restoredSummary.TotalReceivablesCents == 13_000 &&
                    (await restoredReceivables.GetLedgerAsync(unpaidCustomer.Id)).Value.All(
                        entry => !string.Equals(entry.Note, "Post-backup mutation", StringComparison.Ordinal)),
                "Backup/restore must preserve U1 balances/history and discard post-backup mutation.");

            Console.WriteLine(
                "U1 runtime: full/partial Utang, idempotency, receipt identity, aging, net-cash reports, and backup/restore PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<HostOperatorCheckoutResponse> CheckoutAsync(
        HostOperatorApiServices host,
        Guid userId,
        Guid productId,
        string requestId,
        IReadOnlyList<HostOperatorCheckoutPaymentRequest> payments,
        Guid? customerId,
        DateOnly? dueDate,
        string? clientCustomerName,
        string? clientCustomerPhone)
    {
        HostOperatorCheckoutOperationResult result = await host.CheckoutService.SubmitAsync(
            userId,
            "U1 Runtime Cashier",
            new HostOperatorCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(productId, 1000) },
                payments,
                customerId,
                clientCustomerName,
                clientCustomerPhone,
                dueDate,
                "U1 runtime checkout",
                requestId));
        Require(result.Ok && result.Checkout?.Status == HostOperatorCheckoutStatus.Completed,
            "U1 runtime checkout must complete: " + result.Message);
        return result.Checkout!;
    }

    private static async Task AssertAuthorizationMatrixAsync(
        SalrynLocalComposition composition,
        Guid userId,
        Guid customerId)
    {
        InMemoryLocalApiSessionStore sessions = new();
        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                InitializeDatabase = false,
                SessionStore = sessions,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            });
        await app.StartAsync();
        try
        {
            Uri address = new(app.Urls.Single(url =>
                url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)));
            using HttpClient anonymous = new() { BaseAddress = address };
            Require((await anonymous.GetAsync("/api/receivables/summary")).StatusCode == HttpStatusCode.Unauthorized,
                "Unauthenticated receivables request must be denied.");

            LocalApiSession cashierSession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-cashier", "U1 Cashier", "Cashier", UserStatus.Active,
                new[] { DefaultSeedData.Permissions.SalesCreate }));
            using HttpClient cashier = Client(address, cashierSession);
            using HttpResponseMessage cashierCredit = await cashier.PostAsJsonAsync(
                "/api/checkout",
                new
                {
                    clientRequestId = Guid.NewGuid().ToString("N"),
                    lines = Array.Empty<object>(),
                    payments = new[] { new { method = "AccountsReceivable", amountCents = 1 } }
                });
            Require(cashierCredit.StatusCode == HttpStatusCode.Forbidden,
                "Cashier without Receivables.CreateCreditSale must be denied credit checkout.");

            LocalApiSession managerSession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-manager", "U1 Manager", "Manager", UserStatus.Active,
                new[]
                {
                    DefaultSeedData.Permissions.ReceivablesView,
                    DefaultSeedData.Permissions.ReceivablesCollect
                }));
            using HttpClient manager = Client(address, managerSession);
            Require((await manager.GetAsync("/api/receivables/summary")).StatusCode == HttpStatusCode.OK,
                "Manager with Receivables.View must be allowed to read summary.");
            using HttpResponseMessage managerCollection = await manager.PostAsJsonAsync(
                "/api/receivables/collections",
                new
                {
                    customerId,
                    amountCents = 0,
                    method = "Cash",
                    note = "Authorization passage probe",
                    clientRequestId = Guid.NewGuid().ToString("N")
                });
            Require(managerCollection.StatusCode == HttpStatusCode.BadRequest,
                "Manager with Receivables.Collect must pass authorization into domain validation.");

            using HttpRequestMessage terminalRequest = new(HttpMethod.Get, "/api/receivables/summary");
            terminalRequest.Headers.Add("X-Salryn-Terminal-Id", Guid.NewGuid().ToString("D"));
            using HttpResponseMessage terminalResponse = await manager.SendAsync(terminalRequest);
            Require(terminalResponse.StatusCode == HttpStatusCode.Forbidden,
                "LAN terminal request context must be denied local receivable endpoints.");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static HttpClient Client(Uri address, LocalApiSession session)
    {
        HttpClient client = new() { BaseAddress = address };
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{LocalApiSessionService.SessionCookieName}={session.SessionId}");
        return client;
    }

    private static async Task AssertCollectionIdempotencyAsync(
        SqliteAccountsReceivableService receivables,
        Guid customerId,
        long amountCents,
        Guid userId,
        string databasePath)
    {
        string requestId = Guid.NewGuid().ToString("N");
        RecordReceivableCollectionRequest request =
            new(customerId, amountCents, "Cash", "U1 runtime collection", userId, requestId);
        AccountsReceivableCollection first = RequireSuccess(await receivables.CollectAsync(request), "U1 collection");
        AccountsReceivableCollection duplicate =
            RequireSuccess(await receivables.CollectAsync(request), "U1 duplicate collection");
        Require(first.Id == duplicate.Id, "Duplicate collection must return the original collection.");
        Require(Count(databasePath, "accounts_receivable_collections", "id", first.Id.ToString("D")) == 1 &&
                Count(databasePath, "accounts_receivable_entries", "collection_id", first.Id.ToString("D")) == 1,
            "Duplicate collection must create one collection and one AR credit.");
    }

    private static async Task<ReceivableCustomer> CreateCustomerAsync(
        SqliteAccountsReceivableService service,
        Guid userId,
        string name,
        string phone) =>
        RequireSuccess(
            await service.CreateCustomerAsync(new(name, phone, null, null, null, userId)),
            "U1 create customer");

    private static ReceivableCustomerBalance Balance(
        IReadOnlyList<ReceivableCustomerBalance> balances,
        Guid customerId) =>
        balances.Single(row => row.Customer.Id == customerId);

    private static Guid SeedUser(string databasePath)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (id,username,display_name,password_hash,role_id,status,created_at)
            VALUES ($id,'u1-runtime','U1 Runtime','not-used',
                    '00000000-0000-0000-0000-000000000001',1,$at);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static Guid SeedProduct(string databasePath)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO products
                (id,sku,barcode,name,unit_id,cost_price_cents,selling_price_cents,
                 stock_on_hand_x1000,reorder_level_x1000,track_stock,allow_discount,
                 is_active,created_at)
            VALUES
                ($id,'U1-RUNTIME','9900000000001','U1 Runtime Product',
                 (SELECT id FROM units WHERE is_active=1 ORDER BY name LIMIT 1),
                 5000,10000,100000,1000,1,1,1,$at);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static long Count(string databasePath, string table, string column, string value) =>
        ReadLong(databasePath, $"SELECT COUNT(*) FROM {table} WHERE {column}=$value;", value);

    private static long ReadLong(string databasePath, string sql, string value, int? method = null)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);
        if (method.HasValue) command.Parameters.AddWithValue("$method", method.Value);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadText(string databasePath, string sql, string value)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Message}");
        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

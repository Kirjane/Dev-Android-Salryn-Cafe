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
using Salryn.Core.Services.Licensing;
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
            AccountsReceivableEntry partialEntry =
                (await receivables.GetLedgerAsync(partialCustomer.Id)).Value.Single(
                    entry => entry.EntryType == AccountsReceivableEntryType.CreditSale);
            Require(partialEntry.PaymentStatus == SalePaymentStatus.Partial,
                "Partial credit ledger projection must retain payment status for its operator label.");

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

            AccountsReceivableCollection paidCollection =
                await AssertCollectionIdempotencyAsync(
                    receivables, paidCustomer.Id, 10_000, userId, paths.DatabasePath);
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
            await AssertAuthorizationMatrixAsync(
                    composition, userId, unpaidCustomer.Id, paidCollection.Id, paths.DatabasePath)
                .ConfigureAwait(false);
            SeedQueryCapData(paths.DatabasePath, userId, unpaidCustomer.Id);
            Require((await restoredReceivables.SearchCustomersAsync(null)).Value.Count ==
                    SqliteAccountsReceivableService.CustomerQueryLimit,
                "Customer search must enforce its backend result cap.");
            Require((await restoredReceivables.GetLedgerAsync(unpaidCustomer.Id)).Value.Count ==
                    SqliteAccountsReceivableService.LedgerQueryLimit,
                "Customer ledger must enforce its backend result cap.");

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
        Guid customerId,
        Guid collectionId,
        string databasePath)
    {
        Require(DefaultSeedData.AdminPermissions.Contains(DefaultSeedData.Permissions.ReceivablesAdjust) &&
                !DefaultSeedData.ManagerPermissions.Contains(DefaultSeedData.Permissions.ReceivablesAdjust) &&
                !DefaultSeedData.CashierPermissions.Contains(DefaultSeedData.Permissions.ReceivablesAdjust),
            "Collection reversal permission must default to Admin only.");
        ToggleU1LicenseService licenseService = new();
        InMemoryLocalApiSessionStore sessions = new();
        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                InitializeDatabase = false,
                SessionStore = sessions,
                LicenseService = licenseService,
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

            LocalApiSession managerSession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-manager", "U1 Manager", "Manager", UserStatus.Active,
                new[]
                {
                    DefaultSeedData.Permissions.ReceivablesView,
                    DefaultSeedData.Permissions.ReceivablesCollect
                }));
            using HttpClient manager = Client(address, managerSession);
            using HttpResponseMessage baseLicenseSummary = await manager.GetAsync("/api/receivables/summary");
            Require(baseLicenseSummary.StatusCode == HttpStatusCode.Forbidden &&
                    (await baseLicenseSummary.Content.ReadAsStringAsync())
                        .Contains("Receivables.FeatureNotLicensed", StringComparison.Ordinal),
                "Base Host state must deny U1 with the feature-not-licensed code.");

            licenseService.U1Enabled = true;

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
                "Licensed Cashier without Receivables.CreateCreditSale must be denied credit checkout.");
            Require((await cashier.PostAsJsonAsync(
                    $"/api/receivables/collections/{collectionId:D}/void",
                    new { reason = "Cashier must be denied" })).StatusCode == HttpStatusCode.Forbidden,
                "Cashier must be denied collection reversal.");

            Require((await manager.GetAsync("/api/receivables/summary")).StatusCode == HttpStatusCode.OK,
                "Licensed Manager with Receivables.View must be allowed to read summary.");
            Require((await manager.PostAsJsonAsync(
                    $"/api/receivables/collections/{collectionId:D}/void",
                    new { reason = "Manager must be denied by default" })).StatusCode == HttpStatusCode.Forbidden,
                "Manager without Receivables.Adjust must be denied collection reversal.");
            LocalApiSession reportOnlySession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-report-only", "U1 Report Only", "Custom", UserStatus.Active,
                new[] { DefaultSeedData.Permissions.ReportsView }));
            using HttpClient reportOnly = Client(address, reportOnlySession);
            OwnerReportsResponse? reportOnlySummary =
                await reportOnly.GetFromJsonAsync<OwnerReportsResponse>("/api/owner-reports/summary");
            Require(reportOnlySummary?.Receivables is null,
                "Reports.View without Receivables.View must not expose AR totals.");
            LocalApiSession reportAndReceivablesSession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-report-ar", "U1 Report AR", "Custom", UserStatus.Active,
                new[]
                {
                    DefaultSeedData.Permissions.ReportsView,
                    DefaultSeedData.Permissions.ReceivablesView
                }));
            using HttpClient reportAndReceivables = Client(address, reportAndReceivablesSession);
            OwnerReportsResponse? authorizedReport =
                await reportAndReceivables.GetFromJsonAsync<OwnerReportsResponse>("/api/owner-reports/summary");
            Require(authorizedReport?.Receivables is not null,
                "Reports.View plus Receivables.View with U1 must expose AR totals.");
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

            using HttpRequestMessage terminalRequest = new(
                HttpMethod.Post,
                $"/api/receivables/collections/{collectionId:D}/void");
            terminalRequest.Headers.Add("X-Salryn-Terminal-Id", Guid.NewGuid().ToString("D"));
            terminalRequest.Content = JsonContent.Create(new { reason = "Terminal must be denied" });
            using HttpResponseMessage terminalResponse = await manager.SendAsync(terminalRequest);
            Require(terminalResponse.StatusCode == HttpStatusCode.Forbidden,
                "LAN terminal request context must be denied collection reversal.");

            LocalApiSession adminSession = sessions.Create(new AuthenticatedUserReadModel(
                userId, "u1-admin", "U1 Admin", "Admin", UserStatus.Active,
                new[]
                {
                    DefaultSeedData.Permissions.ReceivablesView,
                    DefaultSeedData.Permissions.ReceivablesCollect,
                    DefaultSeedData.Permissions.ReceivablesAdjust
                }));
            using HttpClient admin = Client(address, adminSession);
            using HttpResponseMessage collectionResponse = await admin.PostAsJsonAsync(
                "/api/receivables/collections",
                new
                {
                    customerId,
                    amountCents = 100,
                    method = "Cash",
                    note = "ACK-100",
                    clientRequestId = Guid.NewGuid().ToString("N")
                });
            Require(collectionResponse.StatusCode == HttpStatusCode.Created,
                "Admin with Receivables.Collect must create a collection acknowledgment.");
            string acknowledgmentJson = await collectionResponse.Content.ReadAsStringAsync();
            Require(acknowledgmentJson.Contains("COLLECTION RECEIPT", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("Authoritative Unpaid", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("ACK-100", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("U1 Runtime", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("\"amountPaidCents\":100", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("\"previousBalanceCents\":10000", StringComparison.Ordinal) &&
                    acknowledgmentJson.Contains("\"newBalanceCents\":9900", StringComparison.Ordinal) &&
                    !acknowledgmentJson.Contains("BIR", StringComparison.OrdinalIgnoreCase) &&
                    !acknowledgmentJson.Contains("accounting", StringComparison.OrdinalIgnoreCase) &&
                    !acknowledgmentJson.Contains("official tax", StringComparison.OrdinalIgnoreCase),
                "Collection acknowledgment must contain operational payment fields without compliance claims.");

            Require((await admin.PostAsJsonAsync(
                    $"/api/receivables/collections/{collectionId:D}/void",
                    new { reason = "Admin correction proof" })).StatusCode == HttpStatusCode.OK,
                "Admin with Receivables.Adjust must reverse a collection.");
            using HttpResponseMessage duplicateReversal = await admin.PostAsJsonAsync(
                $"/api/receivables/collections/{collectionId:D}/void",
                new { reason = "Duplicate reversal proof" });
            Require(duplicateReversal.StatusCode == HttpStatusCode.Conflict &&
                    (await duplicateReversal.Content.ReadAsStringAsync())
                        .Contains("This collection was already reversed.", StringComparison.Ordinal) &&
                    Count(databasePath, "accounts_receivable_entries", "collection_id", collectionId.ToString("D")) == 2,
                "Duplicate reversal must return the standard message and append only one reversal.");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class ToggleU1LicenseService : ILocalLicenseService
    {
        public bool U1Enabled { get; set; }

        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> features = U1Enabled
                ? new[] { LicenseFeatures.Pos, LicenseFeatures.Reports, LicenseFeatures.ReceivablesUtangLedger }
                : new[] { LicenseFeatures.Pos, LicenseFeatures.Reports };
            return Task.FromResult(new LocalLicenseValidationResult(
                LicenseStatus.Licensed,
                LicenseEdition.Local,
                features,
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "U1 runtime test Host license.",
                SourcePath: "test",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        }

        public Task<LocalLicenseInstallResponse> InstallAsync(
            LocalLicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                string.Equals(feature, LicenseFeatures.Pos, StringComparison.Ordinal) ||
                string.Equals(feature, LicenseFeatures.Reports, StringComparison.Ordinal) ||
                (U1Enabled && string.Equals(
                    feature,
                    LicenseFeatures.ReceivablesUtangLedger,
                    StringComparison.Ordinal)));
    }

    private static HttpClient Client(Uri address, LocalApiSession session)
    {
        HttpClient client = new() { BaseAddress = address };
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{LocalApiSessionService.SessionCookieName}={session.SessionId}");
        return client;
    }

    private static async Task<AccountsReceivableCollection> AssertCollectionIdempotencyAsync(
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
        return first;
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

    private static void SeedQueryCapData(string databasePath, Guid userId, Guid customerId)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 105; index++)
        {
            using SqliteCommand customer = connection.CreateCommand();
            customer.Transaction = transaction;
            customer.CommandText =
                """
                INSERT INTO customers
                    (id,full_name,phone,address,note,credit_limit_cents,is_active,
                     created_by_user_id,created_at,updated_at)
                VALUES ($id,$name,NULL,NULL,NULL,NULL,1,$user,$at,$at);
                """;
            customer.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            customer.Parameters.AddWithValue("$name", $"Query Cap Customer {index:D3}");
            customer.Parameters.AddWithValue("$user", userId.ToString("D"));
            customer.Parameters.AddWithValue("$at", now.AddMilliseconds(index).ToString("O"));
            customer.ExecuteNonQuery();
        }
        for (int index = 0; index < 505; index++)
        {
            using SqliteCommand ledger = connection.CreateCommand();
            ledger.Transaction = transaction;
            ledger.CommandText =
                """
                INSERT INTO accounts_receivable_entries
                    (id,customer_id,sale_id,collection_id,entry_type,debit_cents,credit_cents,
                     balance_after_cents,due_date,note,created_by_user_id,created_at)
                VALUES ($id,$customer,NULL,NULL,'AdjustmentDebit',1,0,$balance,NULL,
                        'Query cap proof',$user,$at);
                """;
            ledger.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            ledger.Parameters.AddWithValue("$customer", customerId.ToString("D"));
            ledger.Parameters.AddWithValue("$balance", 9_901 + index);
            ledger.Parameters.AddWithValue("$user", userId.ToString("D"));
            ledger.Parameters.AddWithValue("$at", now.AddSeconds(index + 1).ToString("O"));
            ledger.ExecuteNonQuery();
        }
        transaction.Commit();
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

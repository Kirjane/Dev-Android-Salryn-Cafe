using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosM7VoidSaleStockReversalChecks
{
    public static void Run()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pos-m7-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            _ = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "POS-M7 Store", null, null, null, null, 80, "admin", "POS-M7 Admin", "StrongAdmin123!")), "bootstrap");

            InMemoryLocalApiSessionStore store = new();
            LocalApiSessionService sessions = new(composition.Authentication, store);
            LocalApiSession session = RequireValue(sessions.LoginAsync(new LoginRequest("admin", "StrongAdmin123!"))
                .GetAwaiter().GetResult(), "login");
            Require(session.Permissions.Contains(SalrynPermissions.SalesVoid), "Admin session must carry Sales.Void.");

            // Engineer 2026-11-17: Cashier can void any Completed sale without Manager
            // pre-approval. The existing backend still requires a reason and audit.
            Require(DefaultSeedData.CashierPermissions.Contains(SalrynPermissions.SalesVoid),
                "Cashier seed must grant Sales.Void.");
            Require(!DefaultSeedData.CashierPermissions.Contains(SalrynPermissions.OfficialDocumentVoid) &&
                    !DefaultSeedData.CashierPermissions.Contains(SalrynPermissions.OfficialDocumentRefund) &&
                    !DefaultSeedData.CashierPermissions.Contains(SalrynPermissions.InventoryAdjust),
                "Cashier void must not imply official-document refund or inventory adjustment authority.");
            Guid cashierRoleId = Guid.Parse(DefaultSeedData.CashierRoleId);
            Require(GetScalar(composition,
                "SELECT COUNT(*) FROM role_permissions WHERE role_id=$id AND permission='Sales.Void'", cashierRoleId) == 1,
                "Fresh installation must persist cashier Sales.Void once.");

            // Recreate an old installed-role state using ONLY the disposable test DB.
            // Standard startup seeding should restore the new bounded permission
            // without removing a historically customized permission.
            using (SqliteConnection upgrade = new($"Data Source={composition.DbPaths.DatabasePath}"))
            {
                upgrade.Open();
                using SqliteCommand change = upgrade.CreateCommand();
                change.CommandText = """
                    DELETE FROM role_permissions WHERE role_id=$cashier AND permission='Sales.Void';
                    INSERT OR IGNORE INTO role_permissions (id, role_id, permission, created_at)
                    VALUES ($newId, $cashier, 'Custom.LocalPolicy', $at);
                    """;
                change.Parameters.AddWithValue("$cashier", DefaultSeedData.CashierRoleId);
                change.Parameters.AddWithValue("$newId", Guid.NewGuid().ToString("D"));
                change.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                change.ExecuteNonQuery();
            }
            new DatabaseSeeder(composition.ConnectionFactory).SeedDefaultData();
            Require(GetScalar(composition,
                "SELECT COUNT(*) FROM role_permissions WHERE role_id=$id AND permission='Sales.Void'", cashierRoleId) == 1,
                "Existing-role reseed must restore exactly one cashier Sales.Void grant.");
            Require(GetScalar(composition,
                "SELECT COUNT(*) FROM role_permissions WHERE role_id=$id AND permission='Custom.LocalPolicy'", cashierRoleId) == 1,
                "Existing-role reseed must retain local custom permissions.");
            Require(GetScalar(composition,
                """
                SELECT COUNT(*) FROM role_permissions WHERE role_id=$id
                  AND permission IN ('Products.Manage', 'Inventory.Adjust',
                                     'OfficialDocument.Void', 'OfficialDocument.Refund')
                """, cashierRoleId) == 0,
                "Cashier Sales.Void must not grant privileged catalog/stock/fiscal permissions.");

            // Real Cashier login after normal reseeding must carry Sales.Void.
            // The sale below is recorded by Admin to exercise cross-operator void.
            const string cashierPassword = "CashierStrong123!";
            _ = RequireValue(new UserAdminApiService(composition).CreateUserAsync(
                new CreateUserApiRequest("void_cashier", "POS-M7 Cashier",
                    cashierPassword, cashierPassword, cashierRoleId, true), session.UserId)
                .GetAwaiter().GetResult(), "cashier user creation");
            LocalApiSession cashierSession = RequireValue(
                sessions.LoginAsync(new LoginRequest("void_cashier", cashierPassword))
                    .GetAwaiter().GetResult(), "cashier login");
            Require(cashierSession.Permissions.Contains(SalrynPermissions.SalesVoid),
                "Cashier login must carry persisted Sales.Void without Manager approval.");
            Require(!cashierSession.Permissions.Contains(SalrynPermissions.OfficialDocumentRefund) &&
                    !cashierSession.Permissions.Contains(SalrynPermissions.OfficialDocumentVoid),
                "Cashier void session must not carry fiscal refund or official-document void.");

            var categories = RequireValue(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var brands = RequireValue(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");
            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            Product product = new()
            {
                Id = Guid.NewGuid(),
                Sku = "POS-M7-VOID",
                Barcode = RequireValue(Barcode.Create("9790000000007"), "barcode"),
                Name = "POS-M7 Void Product",
                CategoryId = categories[0].Id,
                BrandId = brands[0].Id,
                UnitId = units[0].Id,
                CostPrice = Money.FromCents(1000),
                SellingPrice = Money.FromCents(1500),
                StockOnHand = RequireValue(Quantity.FromX1000(5000), "initial quantity"),
                ReorderLevel = Quantity.Zero,
                TrackStock = true,
                IsActive = true
            };
            RequireSuccess(composition.ProductWriteRepository.AddAsync(product).GetAwaiter().GetResult(), "product");

            Guid saleId = Guid.NewGuid();
            Quantity sold = RequireValue(Quantity.FromX1000(2000), "sold quantity");
            const string snapshot = "POS-M7 immutable receipt snapshot";
            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = "POS-M7-000001",
                CashierUserId = session.UserId,
                Subtotal = Money.FromCents(3000),
                Total = Money.FromCents(3000),
                AmountPaid = Money.FromCents(3000),
                Change = Money.Zero,
                Status = SaleStatus.Completed,
                ReceiptSnapshot = snapshot
            };
            SaleItem item = new()
            {
                SaleId = saleId,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BarcodeSnapshot = product.Barcode?.Value,
                UnitNameSnapshot = units[0].Name,
                Quantity = sold,
                UnitPrice = Money.FromCents(1500),
                CostPrice = product.CostPrice,
                LineTotal = Money.FromCents(3000)
            };
            Payment payment = new() { SaleId = saleId, Amount = Money.FromCents(3000) };
            StockMovement saleMovement = new()
            {
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                QuantityChange = sold,
                CostPrice = product.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = saleId,
                Reason = "POS-M7 sale",
                UserId = session.UserId
            };
            RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
                sale, new[] { item }, new[] { payment }, new[] { saleMovement }).GetAwaiter().GetResult(), "completed sale");

            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 3000, "Sale must reduce stock after commit.");
            long paymentCount = GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", saleId);
            long saleCount = GetScalar(composition, "SELECT COUNT(*) FROM sales WHERE id=$id", saleId);

            SaleVoidApiService service = new(composition);
            Result<SaleVoidResponse> missingReason = service.VoidAsync(saleId, new SaleVoidRequest(" "), cashierSession).GetAwaiter().GetResult();
            Require(missingReason.IsFailure && missingReason.ErrorDetail.Code == "Sales.VoidReasonRequired", "Void without reason must fail.");

            SaleVoidResponse response = RequireValue(service.VoidAsync(
                saleId, new SaleVoidRequest("Customer cancelled completed sale"), cashierSession).GetAwaiter().GetResult(), "void");
            Require(response.Sale.Status == "Voided", "Void must mark sale voided.");
            Require(response.ReversalMovementCount == 1, "Tracked sale movement must create one reversal.");
            Require(response.Sale.ReceiptSnapshot == snapshot, "Void must preserve receipt snapshot.");
            Require(GetText(composition, "SELECT voided_by_user_id FROM sales WHERE id=$id", saleId) ==
                    cashierSession.UserId.ToString("D"),
                "Cross-operator sale void must persist the actual Cashier identity.");
            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 5000, "Void must restore stock.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM stock_movements WHERE reference_id=$id AND movement_type=2", saleId) == 1, "Void must persist one VoidSale movement.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", saleId) == paymentCount, "Void must not duplicate payment.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM sales WHERE id=$id", saleId) == saleCount, "Void must not duplicate sale.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId) == 1, "Successful void must append one searchable SALE_VOIDED audit record.");
            Require(GetText(composition, "SELECT reason FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId) == "Customer cancelled completed sale", "Void audit must preserve the operator reason.");
            Require(GetText(composition, "SELECT user_id FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId) ==
                    cashierSession.UserId.ToString("D"),
                "Cashier void audit must identify the cashier, not the original sale operator.");

            Execute(composition, "DELETE FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId);
            using (SqliteConnection migrationConnection = new($"Data Source={composition.DbPaths.DatabasePath}"))
            {
                migrationConnection.Open();
                using SqliteTransaction transaction = migrationConnection.BeginTransaction();
                new SaleVoidAuditReconciliationMigration().Up(migrationConnection, transaction);
                transaction.Commit();
            }
            Require(GetScalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId) == 1, "Migration must reconcile a missing historical void audit row exactly once.");
            Require(GetText(composition, "SELECT new_value_json FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", saleId).Contains("reconciledFromSaleRecord", StringComparison.Ordinal), "Reconciled historical audit must be explicitly marked.");

            Result<SaleVoidResponse> duplicate = service.VoidAsync(
                saleId, new SaleVoidRequest("Second void"), cashierSession).GetAwaiter().GetResult();
            Require(duplicate.IsFailure && duplicate.ErrorDetail.Code == "Sales.NotVoidable", "Double void must fail.");
            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 5000, "Failed double void must not change stock.");

            RunHistoricalAndCreditVoidScenarios(
                composition, service, cashierSession, session.UserId, product, units[0].Name);

            RunSourceGuards();
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static void RunHistoricalAndCreditVoidScenarios(
        SalrynLocalComposition composition,
        SaleVoidApiService service,
        LocalApiSession cashierSession,
        Guid originalOperatorId,
        Product product,
        string unitName)
    {
        DateTimeOffset historicalAt = DateTimeOffset.UtcNow.AddDays(-30);
        Guid closedSessionId = Guid.NewGuid();
        using (SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cashier_sessions
                (id,user_id,opened_at,closed_at,opening_cash_cents,expected_cash_cents,
                 counted_cash_cents,variance_cents,status,notes)
                VALUES ($id,$user,$opened,$closed,0,1500,1500,0,2,'R2 closed historical shift');
                """;
            command.Parameters.AddWithValue("$id", closedSessionId.ToString("D"));
            command.Parameters.AddWithValue("$user", originalOperatorId.ToString("D"));
            command.Parameters.AddWithValue("$opened", historicalAt.AddHours(-8).ToString("O"));
            command.Parameters.AddWithValue("$closed", historicalAt.ToString("O"));
            command.ExecuteNonQuery();
        }

        Guid historicalSaleId = Guid.NewGuid();
        Quantity one = RequireValue(Quantity.FromX1000(1000), "historical quantity");
        Sale historicalSale = new()
        {
            Id = historicalSaleId,
            ReceiptNumber = "POS-M7-HIST-0001",
            SaleDate = historicalAt.AddHours(-1),
            CreatedAt = historicalAt.AddHours(-1),
            CashierUserId = originalOperatorId,
            CashierSessionId = closedSessionId,
            Subtotal = Money.FromCents(1500),
            Total = Money.FromCents(1500),
            AmountPaid = Money.FromCents(1500),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "R2 closed-shift receipt"
        };
        AddSale(composition, historicalSale, product, unitName, one,
            new Payment { SaleId = historicalSaleId, Method = PaymentMethod.Cash, Amount = Money.FromCents(1500) });
        long historicalPayments = GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", historicalSaleId);
        SaleVoidResponse historicalVoid = RequireValue(service.VoidAsync(
            historicalSaleId, new SaleVoidRequest("Historical closed-shift correction"), cashierSession)
            .GetAwaiter().GetResult(), "historical closed-shift void");
        Require(historicalVoid.Sale.Status == "Voided", "Closed historical shift sale must be voidable by Cashier.");
        Require(GetText(composition, "SELECT voided_by_user_id FROM sales WHERE id=$id", historicalSaleId) == cashierSession.UserId.ToString("D"),
            "Historical void must identify current Cashier.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", historicalSaleId) == historicalPayments,
            "Historical void must preserve payment history.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id AND event_type='CashRefund'", historicalSaleId) == 0,
            "Historical void must not fabricate a cash refund.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id AND event_type='CashSaleNet'", historicalSaleId) == 1,
            "Historical cash-sale drawer event must remain preserved.");

        Guid customerId = Guid.NewGuid();
        using (SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO customers
                (id,full_name,is_active,created_by_user_id,created_at,updated_at)
                VALUES ($id,'R2 Credit Customer',1,$user,$at,$at);
                """;
            command.Parameters.AddWithValue("$id", customerId.ToString("D"));
            command.Parameters.AddWithValue("$user", originalOperatorId.ToString("D"));
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        Guid creditSaleId = Guid.NewGuid();
        Sale creditSale = new()
        {
            Id = creditSaleId,
            ReceiptNumber = "POS-M7-AR-0001",
            CashierUserId = originalOperatorId,
            CustomerId = customerId,
            Subtotal = Money.FromCents(1500),
            Total = Money.FromCents(1500),
            // The repository's tender invariant counts the AR tender as covering the total;
            // BalanceDue separately drives the receivable debit.
            AmountPaid = Money.FromCents(1500),
            Change = Money.Zero,
            PaymentStatus = SalePaymentStatus.Credit,
            BalanceDue = Money.FromCents(1500),
            ReceivableDueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            ReceivableNote = "R2 disposable AR fixture",
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "R2 credit receipt"
        };
        AddSale(composition, creditSale, product, unitName, one,
            new Payment { SaleId = creditSaleId, Method = PaymentMethod.AccountsReceivable, Amount = Money.FromCents(1500) });
        Require(GetScalar(composition, "SELECT COUNT(*) FROM accounts_receivable_entries WHERE sale_id=$id AND entry_type='CreditSale'", creditSaleId) == 1,
            "Credit fixture must create one receivable debit.");
        long creditPayments = GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", creditSaleId);
        SaleVoidResponse creditVoid = RequireValue(service.VoidAsync(
            creditSaleId, new SaleVoidRequest("Uncollected credit sale cancelled"), cashierSession)
            .GetAwaiter().GetResult(), "credit sale void");
        Require(creditVoid.Sale.Status == "Voided", "Uncollected AR sale must be voidable by Cashier.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM accounts_receivable_entries WHERE sale_id=$id AND entry_type='SaleReversal'", creditSaleId) == 1,
            "AR void must create exactly one receivable reversal.");
        Require(GetScalar(composition, "SELECT balance_after_cents FROM accounts_receivable_entries WHERE sale_id=$id AND entry_type='SaleReversal'", creditSaleId) == 0,
            "AR void must return customer balance to zero.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", creditSaleId) == creditPayments,
            "AR void must preserve original payment history.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id AND event_type='CashRefund'", creditSaleId) == 0,
            "AR void must not fabricate a cash refund.");
        Require(GetScalar(composition, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", creditSaleId) == 1,
            "AR void must create one Cashier-attributed audit.");
        Require(GetText(composition, "SELECT user_id FROM audit_logs WHERE entity_id=$id AND action='SALE_VOIDED'", creditSaleId) == cashierSession.UserId.ToString("D"),
            "AR void audit must identify the acting Cashier.");

        Console.WriteLine("[VOID R2] cross-operator + closed historical shift + uncollected AR sale PASS; payments/receipts preserved, no CashRefund, stock and AR reversed exactly once.");
    }

    private static void AddSale(
        SalrynLocalComposition composition,
        Sale sale,
        Product product,
        string unitName,
        Quantity quantity,
        Payment payment)
    {
        SaleItem item = new()
        {
            SaleId = sale.Id,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            BarcodeSnapshot = product.Barcode?.Value,
            UnitNameSnapshot = unitName,
            Quantity = quantity,
            UnitPrice = Money.FromCents(1500),
            CostPrice = product.CostPrice,
            LineTotal = Money.FromCents(1500)
        };
        StockMovement movement = new()
        {
            ProductId = product.Id,
            MovementType = StockMovementType.Sale,
            QuantityChange = quantity,
            CostPrice = product.CostPrice,
            ReferenceType = "SALE",
            ReferenceId = sale.Id,
            Reason = "R2 disposable void fixture",
            UserId = sale.CashierUserId
        };
        RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
            sale, new[] { item }, new[] { payment }, new[] { movement }).GetAwaiter().GetResult(),
            "R2 completed sale fixture");
    }

    private static long GetScalar(SalrynLocalComposition composition, string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void RunSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string endpoints = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Api.Local", "LocalSaleVoidEndpoints.cs"));
        string page = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "pages", "SalesHistoryPage.tsx"));
        Require(endpoints.Contains("LocalApiPermissions.SalesVoid", StringComparison.Ordinal), "Void endpoint must require Sales.Void.");
        Require(!endpoints.Contains("RequireManager", StringComparison.OrdinalIgnoreCase),
            "Completed-sale Cashier void must not require Manager pre-approval.");
        Require(page.Contains("Reprint blocked: VOIDED", StringComparison.Ordinal), "Voided sale reprint behavior must be explicit.");
        Require(page.Contains("Void reason", StringComparison.Ordinal), "Void UI must require a reason.");
        string service = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Api.Local", "Services", "SaleVoidApiService.cs"));
        Require(service.Contains("OperatorAuditActions.SaleVoided", StringComparison.Ordinal), "Successful sale void must append an operator audit record.");
    }

    private static string GetText(SalrynLocalComposition composition, string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void Execute(SalrynLocalComposition composition, string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}");
        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

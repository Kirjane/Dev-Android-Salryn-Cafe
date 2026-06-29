using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using System.Globalization;

internal static class PosAdminOperationsRegressionSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS ADMIN OPERATIONS REGRESSION SMOKE CHECKS =====");

        RunSourceGuard(projectRoot);
        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductReadRepository productReadRepository =
            repositoryFactory.CreateProductReadRepository();
        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();
        ICatalogReadRepository catalogReadRepository =
            repositoryFactory.CreateCatalogReadRepository();
        IInventoryWriteRepository inventoryWriteRepository =
            repositoryFactory.CreateInventoryWriteRepository();
        IUserReadRepository userReadRepository =
            repositoryFactory.CreateUserReadRepository();
        IUserWriteRepository userWriteRepository =
            repositoryFactory.CreateUserWriteRepository();
        IStoreSettingsReadRepository storeSettingsReadRepository =
            repositoryFactory.CreateStoreSettingsReadRepository();
        IStoreSettingsWriteRepository storeSettingsWriteRepository =
            repositoryFactory.CreateStoreSettingsWriteRepository();
        ISaleReadRepository saleReadRepository =
            repositoryFactory.CreateSaleReadRepository();
        ISaleWriteRepository saleWriteRepository =
            repositoryFactory.CreateSaleWriteRepository();
        IAuditReadRepository auditReadRepository =
            repositoryFactory.CreateAuditReadRepository();
        IErrorLogReadRepository errorLogReadRepository =
            repositoryFactory.CreateErrorLogReadRepository();
        ICashierSessionReadRepository cashierSessionReadRepository =
            repositoryFactory.CreateCashierSessionReadRepository();
        ICashierSessionWriteRepository cashierSessionWriteRepository =
            repositoryFactory.CreateCashierSessionWriteRepository();

        CashierSessionService cashierSessions = new(
            cashierSessionWriteRepository,
            cashierSessionReadRepository);

        PosCheckoutService checkoutCore = new(
            saleWriteRepository,
            cashierSessions);

        PosCheckoutApplicationService checkoutApplication = new(
            checkoutCore,
            productReadRepository);

        UserReadModel operatorUser = GetSmokeOperator(userReadRepository);
        ForceCloseExistingOpenSession(cashierSessions, operatorUser.Id);

        SeedIds seedIds = GetSeedIds(catalogReadRepository);

        Product maintainedProduct = RunProductMaintenanceRegression(
            productWriteRepository,
            productReadRepository,
            seedIds);

        RunStoreSettingsRegression(
            storeSettingsReadRepository,
            storeSettingsWriteRepository);

        RunUsersAdminRegression(
            userReadRepository,
            userWriteRepository,
            operatorUser);

        SaleProof saleProof = RunSalesHistoryRegression(
            productReadRepository,
            inventoryWriteRepository,
            saleReadRepository,
            checkoutApplication,
            cashierSessions,
            maintainedProduct,
            operatorUser);

        RunProductDeactivationRegression(
            productWriteRepository,
            productReadRepository,
            maintainedProduct.Id,
            operatorUser.Id);

        RunBackupRecoveryGuardrailsRegression(
            paths,
            storeSettingsReadRepository,
            new SqliteBackupRecoveryService(paths));

        RunAuditErrorOperationsRegression(
            auditReadRepository,
            errorLogReadRepository,
            saleProof.ReceiptNo);

        Console.WriteLine("POS admin operations regression smoke checks succeeded.");
        Console.WriteLine();
    }

    private static Product RunProductMaintenanceRegression(
        IProductWriteRepository productWriteRepository,
        IProductReadRepository productReadRepository,
        SeedIds seedIds)
    {
        Product product = CreateTrackedProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "PosAdminOps.ProductCreate");

        ProductDetailReadModel created = RequireProductDetail(
            productReadRepository.FindDetailByIdAsync(product.Id).GetAwaiter().GetResult(),
            "PosAdminOps.ProductReadCreated");

        long stockBeforeEdit = created.StockOnHand.ValueX1000;

        Product updatedProduct = new()
        {
            Id = product.Id,
            Sku = product.Sku,
            Barcode = product.Barcode,
            Name = product.Name + " Updated",
            Description = "Temporary POS admin operations regression product update.",
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            UnitId = product.UnitId,
            SupplierId = product.SupplierId,
            CostPrice = product.CostPrice,
            SellingPrice = Money.FromCents(product.SellingPrice.Cents + 100),
            WholesalePrice = product.WholesalePrice,
            StockOnHand = created.StockOnHand,
            ReorderLevel = product.ReorderLevel,
            TrackStock = product.TrackStock,
            AllowDiscount = product.AllowDiscount,
            IsActive = true,
            Notes = "POS admin operations regression product update",
            CreatedAt = product.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        RequireSuccess(
            productWriteRepository.UpdateAsync(updatedProduct).GetAwaiter().GetResult(),
            "PosAdminOps.ProductUpdate");

        ProductDetailReadModel updated = RequireProductDetail(
            productReadRepository.FindDetailByIdAsync(product.Id).GetAwaiter().GetResult(),
            "PosAdminOps.ProductReadUpdated");

        if (!string.Equals(updated.Name, updatedProduct.Name, StringComparison.Ordinal) ||
            updated.SellingPrice.Cents != updatedProduct.SellingPrice.Cents ||
            updated.StockOnHand.ValueX1000 != stockBeforeEdit)
        {
            throw new InvalidOperationException("Product maintenance regression readback failed.");
        }

        Console.WriteLine("Product maintenance regression: create/update/readback OK.");
        return updatedProduct;
    }

    private static void RunProductDeactivationRegression(
        IProductWriteRepository productWriteRepository,
        IProductReadRepository productReadRepository,
        Guid productId,
        Guid operatorUserId)
    {
        RequireSuccess(
            productWriteRepository.DeactivateAsync(
                productId,
                "POS admin operations regression product deactivate",
                operatorUserId).GetAwaiter().GetResult(),
            "PosAdminOps.ProductDeactivate");

        ProductDetailReadModel deactivated = RequireProductDetail(
            productReadRepository.FindDetailByIdAsync(productId).GetAwaiter().GetResult(),
            "PosAdminOps.ProductReadDeactivated");

        if (deactivated.IsActive)
        {
            throw new InvalidOperationException("Product maintenance regression expected inactive product state.");
        }

        ProductLookupReadModel? lookup = RequireSuccess(
            productReadRepository.FindByIdAsync(productId).GetAwaiter().GetResult(),
            "PosAdminOps.ProductInactiveLookup");

        if (lookup is not null)
        {
            throw new InvalidOperationException("Inactive product should not be returned by active lookup.");
        }

        Console.WriteLine("Product maintenance regression: deactivate/readback OK.");
    }

    private static void RunStoreSettingsRegression(
        IStoreSettingsReadRepository storeSettingsReadRepository,
        IStoreSettingsWriteRepository storeSettingsWriteRepository)
    {
        StoreSettingsReadModel original = RequireStoreSettings(
            storeSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "PosAdminOps.StoreSettingsReadOriginal");

        string replacementBusinessName =
            "POS Admin Ops Regression " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        RequireSuccess(
            storeSettingsWriteRepository.UpdateAsync(
                BuildStoreSettingsUpdate(original, replacementBusinessName)).GetAwaiter().GetResult(),
            "PosAdminOps.StoreSettingsUpdate");

        StoreSettingsReadModel updated = RequireStoreSettings(
            storeSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "PosAdminOps.StoreSettingsReadUpdated");

        if (!string.Equals(updated.BusinessName, replacementBusinessName, StringComparison.Ordinal))
        {
            RestoreStoreSettings(storeSettingsWriteRepository, original);
            throw new InvalidOperationException("Store settings regression update readback failed.");
        }

        RestoreStoreSettings(storeSettingsWriteRepository, original);

        StoreSettingsReadModel restored = RequireStoreSettings(
            storeSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "PosAdminOps.StoreSettingsReadRestored");

        if (!string.Equals(restored.BusinessName, original.BusinessName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Store settings regression restore readback failed.");
        }

        Console.WriteLine("Store settings regression: update/readback/restore OK.");
    }

    private static void RestoreStoreSettings(
        IStoreSettingsWriteRepository storeSettingsWriteRepository,
        StoreSettingsReadModel original)
    {
        RequireSuccess(
            storeSettingsWriteRepository.UpdateAsync(
                BuildStoreSettingsUpdate(original, original.BusinessName)).GetAwaiter().GetResult(),
            "PosAdminOps.StoreSettingsRestore");
    }

    private static StoreSettingsUpdateRequest BuildStoreSettingsUpdate(
        StoreSettingsReadModel source,
        string? businessName)
    {
        return new StoreSettingsUpdateRequest(
            source.Id,
            source.StoreName,
            businessName,
            source.Address,
            source.ContactNumber,
            source.Tin,
            source.CurrencyLabel,
            source.ReceiptWidthMm,
            source.TaxModeEnabled,
            source.ShiftModeEnabled);
    }

    private static void RunUsersAdminRegression(
        IUserReadRepository userReadRepository,
        IUserWriteRepository userWriteRepository,
        UserReadModel operatorUser)
    {
        IReadOnlyList<UserRoleReadModel> roles = RequireSuccess(
            userReadRepository.GetRolesAsync().GetAwaiter().GetResult(),
            "PosAdminOps.UserRoles");

        UserRoleReadModel createRole = roles.FirstOrDefault(x => x.IsActive)
            ?? throw new InvalidOperationException("Users admin regression expected at least one active role.");
        UserRoleReadModel updateRole = roles.LastOrDefault(x => x.IsActive) ?? createRole;

        string unique = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string username = "adminops-" + unique;

        string passwordHash = RequireSuccess(
            SalrynPasswordHasher.HashPassword("AdminOpsSmoke123!"),
            "PosAdminOps.UserPasswordHash");

        RequireSuccess(
            userWriteRepository.CreateAsync(new UserAdminCreateRequest(
                username,
                "Admin Ops Regression User",
                createRole.RoleId,
                passwordHash,
                operatorUser.Id)).GetAwaiter().GetResult(),
            "PosAdminOps.UserCreate");

        UserReadModel created = RequireUser(
            userReadRepository.FindByUsernameAsync(username).GetAwaiter().GetResult(),
            "PosAdminOps.UserReadCreated");

        string updatedDisplayName = "Admin Ops Regression User Updated";

        RequireSuccess(
            userWriteRepository.UpdateAsync(new UserAdminUpdateRequest(
                created.Id,
                username,
                updatedDisplayName,
                updateRole.RoleId,
                operatorUser.Id)).GetAwaiter().GetResult(),
            "PosAdminOps.UserUpdate");

        UserReadModel updated = RequireUser(
            userReadRepository.FindByIdAsync(created.Id).GetAwaiter().GetResult(),
            "PosAdminOps.UserReadUpdated");

        if (!string.Equals(updated.DisplayName, updatedDisplayName, StringComparison.Ordinal) ||
            !string.Equals(updated.RoleName, updateRole.RoleName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Users admin regression update readback failed.");
        }

        string resetHash = RequireSuccess(
            SalrynPasswordHasher.HashPassword("AdminOpsSmoke456!"),
            "PosAdminOps.UserResetPasswordHash");

        RequireSuccess(
            userWriteRepository.ResetPasswordAsync(new UserAdminPasswordResetRequest(
                created.Id,
                resetHash,
                operatorUser.Id)).GetAwaiter().GetResult(),
            "PosAdminOps.UserResetPassword");

        RequireSuccess(
            userWriteRepository.SetStatusAsync(new UserAdminStatusChangeRequest(
                created.Id,
                UserStatus.Inactive,
                "POS admin operations regression user deactivate",
                operatorUser.Id)).GetAwaiter().GetResult(),
            "PosAdminOps.UserDeactivate");

        UserReadModel inactive = RequireUser(
            userReadRepository.FindByIdAsync(created.Id).GetAwaiter().GetResult(),
            "PosAdminOps.UserReadInactive");

        if (inactive.Status != UserStatus.Inactive)
        {
            throw new InvalidOperationException("Users admin regression expected inactive user state.");
        }

        RequireSuccess(
            userWriteRepository.SetStatusAsync(new UserAdminStatusChangeRequest(
                created.Id,
                UserStatus.Active,
                "POS admin operations regression user reactivate",
                operatorUser.Id)).GetAwaiter().GetResult(),
            "PosAdminOps.UserReactivate");

        UserReadModel reactivated = RequireUser(
            userReadRepository.FindByIdAsync(created.Id).GetAwaiter().GetResult(),
            "PosAdminOps.UserReadReactivated");

        if (reactivated.Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Users admin regression expected reactivated user state.");
        }

        Result selfDeactivate = userWriteRepository.SetStatusAsync(new UserAdminStatusChangeRequest(
            operatorUser.Id,
            UserStatus.Inactive,
            "POS admin operations regression self-deactivate guard",
            operatorUser.Id)).GetAwaiter().GetResult();

        if (selfDeactivate.IsSuccess)
        {
            throw new InvalidOperationException("Users admin regression self-deactivation guard failed.");
        }

        UserReadModel operatorAfterGuard = RequireUser(
            userReadRepository.FindByIdAsync(operatorUser.Id).GetAwaiter().GetResult(),
            "PosAdminOps.UserReadOperatorAfterSelfGuard");

        if (operatorAfterGuard.Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Users admin regression changed the current operator status.");
        }

        Console.WriteLine("Users admin regression: create/update/password/status/self-guard OK.");
    }

    private static SaleProof RunSalesHistoryRegression(
        IProductReadRepository productReadRepository,
        IInventoryWriteRepository inventoryWriteRepository,
        ISaleReadRepository saleReadRepository,
        PosCheckoutApplicationService checkoutApplication,
        ICashierSessionService cashierSessions,
        Product product,
        UserReadModel operatorUser)
    {
        StockMovement stockIn = new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            MovementType = StockMovementType.StockIn,
            QuantityChange = QuantityX1000(3000),
            CostPrice = product.CostPrice,
            ReferenceType = "POS-ADMIN-OPS",
            ReferenceId = Guid.NewGuid(),
            Reason = "POS admin operations regression sales stock",
            UserId = operatorUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(stockIn).GetAwaiter().GetResult(),
            "PosAdminOps.SalesStockIn");

        ProductLookupReadModel activeProduct = RequireProductLookup(
            productReadRepository.FindByIdAsync(product.Id).GetAwaiter().GetResult(),
            "PosAdminOps.SalesProductLookup");

        CashierSessionReadModel openSession = RequireSuccess(
            cashierSessions.OpenAsync(new OpenCashierSessionRequest(
                operatorUser.Id,
                Money.FromCents(10000),
                "POS admin operations regression sales-history open")).GetAwaiter().GetResult(),
            "PosAdminOps.SalesOpenSession");

        DateTimeOffset saleDate = DateTimeOffset.UtcNow;
        string receiptNo = "ADM-" + saleDate.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
            "-" + Random.Shared.Next(100, 999).ToString(CultureInfo.InvariantCulture);

        PosCheckoutApplicationRequest request = new(
            operatorUser.Id,
            openSession.Id,
            receiptNo,
            new[]
            {
                new PosCheckoutLineInput(
                    activeProduct.Id,
                    activeProduct.Name,
                    activeProduct.Barcode,
                    activeProduct.UnitName,
                    QuantityX1000: 1000,
                    UnitPriceCents: activeProduct.SellingPrice.Cents,
                    CostPriceCents: activeProduct.CostPrice?.Cents,
                    DiscountCents: 0,
                    TrackStock: true)
            },
            new[]
            {
                new PosCheckoutPaymentInput(
                    PaymentMethod.Cash,
                    AmountCents: activeProduct.SellingPrice.Cents)
            },
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: "POS admin operations regression receipt snapshot.",
            SaleDate: saleDate);

        RequireSuccess(
            checkoutApplication.CompleteSaleAsync(request).GetAwaiter().GetResult(),
            "PosAdminOps.SalesComplete");

        SaleDetailReadModel saleByReceipt = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(receiptNo).GetAwaiter().GetResult(),
            "PosAdminOps.SalesFindReceipt");

        if (saleByReceipt.Status != SaleStatus.Completed ||
            !string.Equals(saleByReceipt.ReceiptNo, receiptNo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Sales history regression receipt lookup failed.");
        }

        VerifySalesHistoryFilter(
            saleReadRepository,
            saleByReceipt.Id,
            receiptNo,
            operatorUser.Id,
            SaleStatus.Completed,
            saleDate);

        ProductLookupReadModel productAfterSale = RequireProductLookup(
            productReadRepository.FindByIdAsync(product.Id).GetAwaiter().GetResult(),
            "PosAdminOps.SalesProductAfterSale");

        if (productAfterSale.StockOnHand.ValueX1000 != 2000)
        {
            throw new InvalidOperationException("Sales history regression stock decrease verification failed.");
        }

        CashierSessionReadModel closedSession = RequireSuccess(
            cashierSessions.CloseAsync(new CloseCashierSessionRequest(
                openSession.Id,
                operatorUser.Id,
                Money.FromCents(10000 + activeProduct.SellingPrice.Cents),
                "POS admin operations regression sales-history close")).GetAwaiter().GetResult(),
            "PosAdminOps.SalesCloseSession");

        if (closedSession.Status != CashierSessionStatus.Closed)
        {
            throw new InvalidOperationException("Sales history regression session close failed.");
        }

        CashierSessionReadModel? remainingOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(operatorUser.Id).GetAwaiter().GetResult(),
            "PosAdminOps.SalesOpenSessionAfterClose");

        if (remainingOpen is not null)
        {
            throw new InvalidOperationException("Sales history regression left an open cashier session.");
        }

        Console.WriteLine("Sales history regression: receipt/status/date/page filters OK.");
        return new SaleProof(saleByReceipt.Id, receiptNo, saleDate);
    }

    private static void VerifySalesHistoryFilter(
        ISaleReadRepository saleReadRepository,
        Guid saleId,
        string receiptNo,
        Guid cashierUserId,
        SaleStatus status,
        DateTimeOffset saleDate)
    {
        SaleHistoryFilter filter = new(
            From: saleDate.AddMinutes(-2),
            To: saleDate.AddMinutes(2),
            CashierUserId: cashierUserId,
            Status: status,
            ReceiptNo: receiptNo,
            Page: PageRequest.FirstPage(1));

        PagedResult<SaleHistoryItemReadModel> page = RequireSuccess(
            saleReadRepository.SearchSalesAsync(filter).GetAwaiter().GetResult(),
            "PosAdminOps.SalesHistoryFilteredSearch");

        if (page.PageNumber != 1 ||
            page.PageSize != 1 ||
            page.TotalCount < 1 ||
            page.Items.Count != 1)
        {
            throw new InvalidOperationException("Sales history regression paging filter failed.");
        }

        SaleHistoryItemReadModel item = page.Items[0];

        if (item.Id != saleId ||
            item.Status != status ||
            !string.Equals(item.ReceiptNo, receiptNo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Sales history regression filtered item mismatch.");
        }
    }

    private static void RunBackupRecoveryGuardrailsRegression(
        DbPaths paths,
        IStoreSettingsReadRepository storeSettingsReadRepository,
        IBackupRecoveryService backupRecoveryService)
    {
        StoreSettingsReadModel beforeBackup = RequireStoreSettings(
            storeSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "PosAdminOps.BackupLiveReadBefore");

        if (!File.Exists(paths.DatabasePath))
        {
            throw new InvalidOperationException("Backup guardrails regression expected live database file.");
        }

        Directory.CreateDirectory(paths.BackupPath);
        string backupPath = Path.Combine(
            paths.BackupPath,
            "salryn-admin-ops-" +
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) +
            ".db");

        SqliteConnectionFactory.ClearAllPools();
        File.Copy(paths.DatabasePath, backupPath, overwrite: false);

        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException("Backup guardrails regression did not create a backup file.");
        }

        IReadOnlyList<BackupFileSummary> backups = RequireSuccess(
            backupRecoveryService.ListBackups(),
            "PosAdminOps.BackupList");

        if (!backups.Any(x => string.Equals(x.FilePath, backupPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Backup guardrails regression list did not include the created backup.");
        }

        BackupVerificationSummary verification = RequireSuccess(
            backupRecoveryService.VerifyBackup(backupPath),
            "PosAdminOps.BackupVerify");

        if (!verification.CoreTablesExist ||
            verification.SizeBytes <= 0 ||
            verification.ProductCount is null ||
            verification.UserCount is null ||
            verification.SaleCount is null)
        {
            throw new InvalidOperationException("Backup guardrails regression verification summary failed.");
        }

        StoreSettingsReadModel afterBackup = RequireStoreSettings(
            storeSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "PosAdminOps.BackupLiveReadAfter");

        if (afterBackup.Id != beforeBackup.Id ||
            !string.Equals(afterBackup.StoreName, beforeBackup.StoreName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backup guardrails regression changed the live database.");
        }

        Console.WriteLine("Backup guardrails regression: create/list/verify/live-intact OK.");
    }

    private static void RunAuditErrorOperationsRegression(
        IAuditReadRepository auditReadRepository,
        IErrorLogReadRepository errorLogReadRepository,
        string saleReceiptNo)
    {
        DateTimeOffset from = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset to = DateTimeOffset.UtcNow.AddMinutes(30);

        PagedResult<AuditLogReadModel> userAuditPage = RequireSuccess(
            auditReadRepository.SearchAuditLogsAsync(new AuditLogSearchFilter(
                Page: PageRequest.FirstPage(10),
                From: from,
                To: to,
                Search: new SearchText("USER_"))).GetAwaiter().GetResult(),
            "PosAdminOps.AuditUserSearch");

        if (userAuditPage.TotalCount < 1 ||
            userAuditPage.Items.Count < 1 ||
            userAuditPage.Items.Count > 10)
        {
            throw new InvalidOperationException("Audit operations regression user filter/paging failed.");
        }

        PagedResult<AuditLogReadModel> productAuditPage = RequireSuccess(
            auditReadRepository.SearchAuditLogsAsync(new AuditLogSearchFilter(
                Page: PageRequest.FirstPage(5),
                From: from,
                To: to,
                Search: new SearchText("PRODUCT_DEACTIVATED"))).GetAwaiter().GetResult(),
            "PosAdminOps.AuditProductSearch");

        if (productAuditPage.TotalCount < 1 || productAuditPage.Items.Count < 1)
        {
            throw new InvalidOperationException("Audit operations regression product filter failed.");
        }

        PagedResult<ErrorLogReadModel> errorPage = RequireSuccess(
            errorLogReadRepository.SearchErrorLogsAsync(new ErrorLogSearchFilter(
                Page: PageRequest.FirstPage(5),
                From: from,
                To: to,
                Search: new SearchText(saleReceiptNo))).GetAwaiter().GetResult(),
            "PosAdminOps.ErrorLogSearch");

        if (errorPage.Items.Count > 5 ||
            errorPage.PageNumber != 1 ||
            errorPage.PageSize != 5)
        {
            throw new InvalidOperationException("Error operations regression paging failed.");
        }

        Console.WriteLine("Audit/error operations regression: filtering and paging OK.");
    }

    private static void RunSourceGuard(string projectRoot)
    {
        string smokePath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "PosAdminOperationsRegressionSmokeChecks.cs");

        string smokeText = File.ReadAllText(smokePath);
        string normalizedSmokeText = smokeText.ToLowerInvariant();

        string[] blockedCommandMarkers =
        {
            string.Concat("Salryn.App", ".WinForms"),
            string.Concat("System.Windows", ".Forms"),
            string.Concat("Application", ".Run"),
            string.Concat("Checkout", "Presenter"),
            string.Concat("ProductManagement", "Form"),
            string.Concat("StoreSettings", "Form"),
            string.Concat("UsersAdmin", "Form"),
            string.Concat("SalesHistory", "Form"),
            string.Concat("R", "MO"),
            string.Concat("official", "-", "output"),
            string.Concat("official", " output"),
            string.Concat("P", "DF"),
            string.Concat("crys", "tal"),
            string.Concat(".", "rpt"),
            string.Concat("ESC", "/", "POS"),
            string.Concat("ESC", "POS"),
            string.Concat("scan", "ner"),
            string.Concat("pr", "inter"),
            string.Concat("hard", "ware"),
            string.Concat("local", " area ", "network"),
            string.Concat("mob", "ile"),
            string.Concat("front", "end")
        };

        foreach (string marker in blockedCommandMarkers)
        {
            if (normalizedSmokeText.Contains(marker.ToLowerInvariant(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Command source contains a blocked boundary marker.");
            }
        }

        string programPath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "Program.cs");

        string programText = File.ReadAllText(programPath);

        if (!programText.Contains("pos-admin-operations-regression", StringComparison.Ordinal) ||
            !programText.Contains("PosAdminOperationsRegressionSmokeChecks.Run(projectRoot);", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Infrastructure smoke command registration is missing.");
        }

        string uiRoot = Path.Combine(projectRoot, "src", string.Concat("Salryn.App", ".WinForms"), "Features");

        if (!Directory.Exists(uiRoot))
        {
            throw new InvalidOperationException("WinForms feature source root was not found for boundary guard.");
        }

        foreach (string path in Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            string relativePath = Path.GetRelativePath(projectRoot, path);

            RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"WinForms feature source must not reference SQLite directly: {relativePath}");
            RequireDoesNotContain(text, "SqliteConnection", $"WinForms feature source must not open SQLite connections directly: {relativePath}");
            RequireDoesNotContain(text, "SqliteCommand", $"WinForms feature source must not create SQLite commands directly: {relativePath}");
            RequireDoesNotContain(text, "ExecuteNonQuery(", $"WinForms feature source must not execute SQL writes directly: {relativePath}");
            RequireDoesNotContain(text, "ExecuteReader(", $"WinForms feature source must not execute SQL reads directly: {relativePath}");
        }
    }

    private static bool IsBuildOutputPath(string path)
    {
        string normalized = path.Replace(Path.DirectorySeparatorChar, '/');

        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDatabaseReady(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        bool needsRepositoryBootstrap = !File.Exists(orchestrator.DatabasePath);

        if (!needsRepositoryBootstrap)
        {
            DatabaseInitializationStatus status = RequireSuccess(
                orchestrator.CheckStatus(),
                "PosAdminOps.CheckStatus");

            needsRepositoryBootstrap =
                !status.FirstRunStatus.IsDatabaseInitialized ||
                status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsRepositoryBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogReadRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "PosAdminOps.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "PosAdminOps.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "PosAdminOps.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("POS admin operations regression seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id,
            units[0].Name);
    }

    private static UserReadModel GetSmokeOperator(IUserReadRepository userReadRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userReadRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "PosAdminOps.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userReadRepository.GetAllAsync().GetAwaiter().GetResult(),
            "PosAdminOps.GetAllUsers");

        UserReadModel? activeUser = users.FirstOrDefault(x => x.Status == UserStatus.Active);

        return activeUser ?? throw new InvalidOperationException("POS admin operations regression expected at least one active user.");
    }

    private static void ForceCloseExistingOpenSession(
        ICashierSessionService cashierSessions,
        Guid cashierId)
    {
        CashierSessionReadModel? existingOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashierId).GetAwaiter().GetResult(),
            "PosAdminOps.PreExistingOpenLookup");

        if (existingOpen is null)
        {
            return;
        }

        RequireSuccess(
            cashierSessions.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                existingOpen.Id,
                cashierId,
                null,
                "POS admin operations regression cleanup pre-existing open session")).GetAwaiter().GetResult(),
            "PosAdminOps.ForceClosePreExistingOpen");
    }

    private static Product CreateTrackedProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireSuccess(
            Barcode.Create(NewBarcode()),
            "PosAdminOps.Barcode");

        string unique = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
            "-" + Random.Shared.Next(100, 999).ToString(CultureInfo.InvariantCulture);

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "POS-ADM-" + unique,
            Barcode = barcode,
            Name = "POS Admin Operations Regression Product",
            Description = "Temporary POS admin operations regression product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1800),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(0),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "POS admin operations regression smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static string NewBarcode()
    {
        return "92" +
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
            Random.Shared.Next(100, 999).ToString(CultureInfo.InvariantCulture);
    }

    private static ProductDetailReadModel RequireProductDetail(
        Result<ProductDetailReadModel?> result,
        string area)
    {
        ProductDetailReadModel? product = RequireSuccess(result, area);

        return product ?? throw new InvalidOperationException($"{area} returned no product.");
    }

    private static ProductLookupReadModel RequireProductLookup(
        Result<ProductLookupReadModel?> result,
        string area)
    {
        ProductLookupReadModel? product = RequireSuccess(result, area);

        return product ?? throw new InvalidOperationException($"{area} returned no active product.");
    }

    private static StoreSettingsReadModel RequireStoreSettings(
        Result<StoreSettingsReadModel?> result,
        string area)
    {
        StoreSettingsReadModel? settings = RequireSuccess(result, area);

        return settings ?? throw new InvalidOperationException($"{area} returned no store settings.");
    }

    private static UserReadModel RequireUser(
        Result<UserReadModel?> result,
        string area)
    {
        UserReadModel? user = RequireSuccess(result, area);

        return user ?? throw new InvalidOperationException($"{area} returned no user.");
    }

    private static SaleDetailReadModel RequireSale(
        Result<SaleDetailReadModel?> result,
        string area)
    {
        SaleDetailReadModel? sale = RequireSuccess(result, area);

        return sale ?? throw new InvalidOperationException($"{area} returned no sale.");
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireSuccess(
            Quantity.FromX1000(valueX1000),
            $"PosAdminOps.Quantity.{valueX1000}");
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record SeedIds(
        Guid CategoryId,
        Guid BrandId,
        Guid UnitId,
        string UnitName);

    private sealed record SaleProof(
        Guid SaleId,
        string ReceiptNo,
        DateTimeOffset SaleDate);
}

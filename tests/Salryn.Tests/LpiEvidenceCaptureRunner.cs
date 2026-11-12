using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class LpiEvidenceCaptureRunner
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lpi-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);

            // Seed users
            Guid requesterUserId = LpiProcureToPayRuntimeChecks.SeedUser(paths.DatabasePath, "lpi-requester", "LPI Requester");
            Guid approverUserId = LpiProcureToPayRuntimeChecks.SeedUser(paths.DatabasePath, "lpi-approver", "LPI Approver");
            Guid receiverUserId = LpiProcureToPayRuntimeChecks.SeedUser(paths.DatabasePath, "lpi-receiver", "LPI Receiver");
            Guid apClerkUserId = LpiProcureToPayRuntimeChecks.SeedUser(paths.DatabasePath, "lpi-apclerk", "LPI AP Clerk");
            Guid financeManagerUserId = LpiProcureToPayRuntimeChecks.SeedUser(paths.DatabasePath, "lpi-finance", "LPI Finance Manager");

            // Seed products
            Guid productAlphaId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "LPI-ALPHA-001", "9900001000001", "Alpha Coffee Beans 1kg", 45_000, 75_000, 20_000, 10_000);
            Guid productBetaId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "LPI-BETA-002", "9900001000002", "Beta Oat Milk 1L", 12_000, 22_000, 50_000, 15_000);
            Guid productGammaId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "LPI-GAMMA-003", "9900001000003", "Gamma Takeout Cup Pack", 8_000, 15_000, 30_000, 5_000);

            // Seed store settings so bootstrap does not require first-run
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO store_settings
                        (id, store_name, business_name, address, contact_number, tin, currency_label,
                         receipt_width_mm, receipt_header, receipt_footer, tax_mode_enabled,
                         shift_mode_enabled, allow_negative_stock, created_at, updated_at)
                    VALUES
                        ($id, 'Salryn Flagship Store', 'Salryn Retail Ph', 'Makati Ave, Metro Manila',
                         '0917-100-0000', '123-456-789-000', 'PHP', 80, NULL, NULL, 1, 1, 0, $now, NULL);

                    INSERT OR REPLACE INTO app_metadata (key, value, updated_at) VALUES ('first_run_required', 'false', $now);
                    """;
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            // Foundation & Locations
            LpiFoundationCatalog foundation = (await service.GetFoundationCatalogAsync()).Value!;
            Guid mainWarehouseId = foundation.Locations.First(loc => loc.Code == "MAIN-STORE").Id;

            // Suppliers
            LpiSupplier supplierOne = (await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-APEX-01", "Apex Roasters & Dairy Supply Inc.", "Vendor Contact A", "09171234501", "orders@apexroasters.ph",
                "Makati Industrial Park, Metro Manila", "123-456-789-000", 30, 5, "Primary coffee and dairy vendor", requesterUserId))).Value!;

            LpiSupplier supplierTwo = (await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-PACK-02", "Metro Packaging Solutions Corp.", "Vendor Contact B", "09171234502", "sales@metropack.ph",
                "Pasig Logistics Hub, Metro Manila", "987-654-321-000", 15, 3, "Packaging and consumables vendor", requesterUserId))).Value!;

            // PR #1: Split Vendor Restock
            PurchaseRequisitionModel pr = (await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                "Store Operations", new DateOnly(2026, 11, 20), "Normal", "Monthly restock for coffee bar and packaging", "Split vendor restock request", true,
                new[]
                {
                    new CreatePurchaseRequisitionLineInput(productAlphaId, "LPI-ALPHA-001", "Alpha Coffee Beans 1kg", "Beverage", "Resale", "bag", 10_000, 45_000, supplierOne.Id, "Arabica medium roast", "Urgent coffee stock"),
                    new CreatePurchaseRequisitionLineInput(productGammaId, "LPI-GAMMA-003", "Gamma Takeout Cup Pack", "Packaging", "Consumable", "pack", 20_000, 8_000, supplierTwo.Id, "12oz double-wall cups", "Cup restock")
                }, requesterUserId))).Value!;

            // Approve PR
            pr = (await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(pr.Id, "Approve", "Approved for split supplier sourcing", null, approverUserId))).Value!;

            // Quotations
            await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                pr.Id, pr.Lines[0].Id, supplierOne.Id, productAlphaId, "Alpha Coffee Beans 1kg", 45_000, 5_000, 5, 30, new DateOnly(2026, 12, 31), true, "Best lead time and terms", requesterUserId));

            // PO #1 (Alpha)
            var prLineAlpha = pr.Lines.Single(l => l.ProductId == productAlphaId);
            var prLineGamma = pr.Lines.Single(l => l.ProductId == productGammaId);

            PurchaseOrderModel poAlpha = (await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplierOne.Id, pr.Id, new DateOnly(2026, 11, 18), mainWarehouseId, 30, 0, "Deliver to rear receiving bay", "PO #1 from PR split conversion", false,
                new[]
                {
                    new CreatePurchaseOrderLineInput(prLineAlpha.Id, productAlphaId, prLineAlpha.ItemCode, prLineAlpha.ItemDescription, prLineAlpha.PurchaseClassification, prLineAlpha.UnitName, 10_000, 45_000, 0, 120, "1.2% freight tax")
                }, requesterUserId))).Value!;

            // Transition PO Alpha: Submit -> Approve -> MarkSent
            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poAlpha.Id, "Submit", "Submitted for approval", requesterUserId));
            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poAlpha.Id, "Approve", "Approved by manager", approverUserId));
            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poAlpha.Id, "MarkSent", "Dispatched to vendor", requesterUserId));

            // PO #2 (Gamma)
            PurchaseOrderModel poGamma = (await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplierTwo.Id, pr.Id, new DateOnly(2026, 11, 16), mainWarehouseId, 15, 0, "Gate 2 delivery", "PO #2 for packaging", false,
                new[]
                {
                    new CreatePurchaseOrderLineInput(prLineGamma.Id, productGammaId, prLineGamma.ItemCode, prLineGamma.ItemDescription, prLineGamma.PurchaseClassification, prLineGamma.UnitName, 20_000, 8_000, 0, 0, null)
                }, requesterUserId))).Value!;

            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poGamma.Id, "Submit", "Submitted for approval", requesterUserId));
            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poGamma.Id, "Approve", "Approved by manager", approverUserId));
            await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(poGamma.Id, "MarkSent", "Dispatched to vendor", requesterUserId));

            // Goods Receiving RR #1 (Partial delivery: 7 received, 6 accepted, 1 quarantine)
            Guid poAlphaLineId = poAlpha.Lines.Single().Id;
            ReceivingReportModel rrOne = (await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poAlpha.Id, mainWarehouseId, "DR-APEX-2026-001", "SI-APEX-2026-001", "Apex Logistics Van #4", new DateOnly(2026, 11, 10), true, null, null, "rr-req-1", "First partial delivery; 1 bag punctured in transit",
                new[]
                {
                    new CreateReceivingReportLineInput(poAlphaLineId, 7_000, 6_000, 1_000, "Bag punctured during courier handling", "Quarantine", "BATCH-ALPHA-11A", new DateOnly(2027, 5, 30))
                }, receiverUserId))).Value!;

            // Goods Receiving for PO Gamma (Full delivery: 20 received, 20 accepted)
            Guid poGammaLineId = poGamma.Lines.Single().Id;
            ReceivingReportModel rrGamma = (await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poGamma.Id, mainWarehouseId, "DR-PACK-2026-001", "SI-PACK-2026-001", "Eco Logistics Truck", new DateOnly(2026, 11, 10), true, null, null, "rr-gamma-req", "Full packaging delivery received in good order",
                new[]
                {
                    new CreateReceivingReportLineInput(poGammaLineId, 20_000, 20_000, 0, null, null, "BATCH-CUP-2026A", null)
                }, receiverUserId))).Value!;

            // Supplier Invoice with 3-Way Match & Price Variance
            SupplierInvoiceModel invoiceWithVariance = (await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplierTwo.Id, "INV-PACK-2026-001", poGamma.Id, rrGamma.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 25), 0, 0, true,
                new[]
                {
                    new CreateSupplierInvoiceLineInput(poGammaLineId, rrGamma.Lines.Single().Id, productGammaId, "Gamma Takeout Cup Pack", 20_000, 8_500, 0)
                }, apClerkUserId))).Value!;

            await service.ApproveInvoiceVarianceAsync(new ApproveInvoiceVarianceRequest(
                invoiceWithVariance.Id, "Approved 5.00/pack freight surcharge per addendum", true, financeManagerUserId));

            // Exact 3-Way Match Invoice for Alpha
            SupplierInvoiceModel invoiceAlpha = (await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplierOne.Id, "SI-APEX-2026-001", poAlpha.Id, rrOne.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 10), 0, 0, true,
                new[]
                {
                    new CreateSupplierInvoiceLineInput(poAlphaLineId, rrOne.Lines.Single().Id, productAlphaId, "Alpha Coffee Beans 1kg", 6_000, 45_000, 3_240)
                }, apClerkUserId))).Value!;

            // Post-acceptance supplier return of 1 bag
            SupplierReturnModel supplierReturn = (await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                rrOne.Id,
                invoiceAlpha.Id,
                "Hidden moisture defect discovered upon opening 1 bag",
                new[]
                {
                    new CreateSupplierReturnLineInput(rrOne.Lines.Single().Id, 1_000, "Moisture defect")
                },
                receiverUserId,
                approverUserId))).Value!;

            // Partial AP Payment
            SupplierPaymentModel paymentOne = (await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                invoiceAlpha.Id, new DateOnly(2026, 11, 12), 100_000, "BankTransfer", "OperatingBank", "BPI-TRF-9001", null, "BPI Corporate", "Partial settlement #1", "lpi-pay-req-1", apClerkUserId, null))).Value!;
            paymentOne = (await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                paymentOne.Id, financeManagerUserId, "YES", "Actual BPI transfer confirmed"))).Value!;

            // Manager session for browser UI
            InMemoryLocalApiSessionStore sessionStore = new();
            AuthenticatedUserReadModel managerUser = new(
                requesterUserId,
                "argon-manager",
                "Argon LPI Manager",
                "Manager",
                UserStatus.Active,
                DefaultSeedData.AdminPermissions)
            {
                RoleId = Guid.Parse(DefaultSeedData.ManagerRoleId)
            };
            var session = sessionStore.Create(managerUser);

            await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    SessionStore = sessionStore,
                    LicenseService = new FakeEvidenceLicenseService(),
                    EnableStaticWebAssets = true,
                    WriteDevelopmentDatabasePath = false
                }).ConfigureAwait(false);

            await app.StartAsync().ConfigureAwait(false);

            string? boundUrl = app.Urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(boundUrl) || boundUrl.EndsWith(":0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Failed to resolve dynamic loopback port.");
            }

            Console.WriteLine($"[LPI Evidence] Host started on {boundUrl} with session {session.SessionId}");

            // Execute capture script
            string repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
            string scriptPath = Path.Combine(repoRoot, "scripts", "capture-lpi-screenshots.mjs");
            ProcessStartInfo psi = new("node", $"\"{scriptPath}\" {boundUrl} {session.SessionId}")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using Process? proc = Process.Start(psi);
            if (proc is null) throw new InvalidOperationException("Failed to launch node screenshot process.");

            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Console.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine("[STDERR]: " + stderr);
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"Screenshot capture failed with exit code {proc.ExitCode}");
            }

            await app.StopAsync().ConfigureAwait(false);
            Console.WriteLine("[LPI Evidence] Screenshots successfully captured!");
        }
        finally
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private static string? FindRepoRoot()
    {
        string[] candidates = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (string candidate in candidates)
        {
            DirectoryInfo? current = new(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }
        return null;
    }

    private sealed class FakeEvidenceLicenseService : ILocalLicenseService
    {
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalLicenseValidationResult(
                LicenseStatus.Licensed,
                LicenseEdition.Local,
                LicenseFeatures.AllList,
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "LPI evidence license state.",
                SourcePath: "lpi-evidence",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        }

        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalLicenseInstallResponse(
                "Installed", LicenseStatus.Licensed, "lpi-evidence", null, null, "Installed", DateTimeOffset.UtcNow));

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}

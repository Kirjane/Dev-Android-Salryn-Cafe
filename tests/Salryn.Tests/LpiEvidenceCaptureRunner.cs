using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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

            // Partial AP payment: the declared transfer date is a synthetic fixture fact, never a service default.
            DateOnly requestedPaymentDate = new(2026, 11, 12);
            DateOnly manilaBusinessToday = DateOnly.FromDateTime(
                TimeProvider.System.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
            if (requestedPaymentDate > manilaBusinessToday)
            {
                throw new InvalidOperationException(
                    $"LPI evidence fixture cannot confirm a future transfer dated {requestedPaymentDate:yyyy-MM-dd}; Manila today is {manilaBusinessToday:yyyy-MM-dd}.");
            }

            DateOnly actualTransferDate = requestedPaymentDate;
            string actualTransferDateText = actualTransferDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            SupplierInvoiceModel invoiceBeforePayment = RequireSuccess(
                await service.GetSupplierInvoiceByIdAsync(invoiceAlpha.Id),
                "Load invoice before synthetic payment");
            Require(invoiceBeforePayment.RemainingBalanceCents > 100_000,
                "LPI evidence fixture must have more than 100,000 cents outstanding for a partial payment.");

            SupplierPaymentModel paymentOne = RequireSuccess(
                await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                    invoiceAlpha.Id, requestedPaymentDate, 100_000, "BankTransfer", "OperatingBank",
                    "BPI-TRF-9001", null, "BPI Corporate", "Partial settlement #1",
                    "lpi-pay-req-1", apClerkUserId, null)),
                "Record synthetic pending supplier payment");
            Require(paymentOne.Status == SupplierPaymentStatuses.PendingApproval &&
                    paymentOne.PaymentDate == requestedPaymentDate &&
                    paymentOne.ActualDisbursementDate is null &&
                    paymentOne.ApprovedByUserId is null && paymentOne.ApprovedAt is null,
                "Synthetic payment must remain pending with only its original requested date.");

            Result<SupplierPaymentModel> settlementResult = await service.ApproveSupplierPaymentAsync(
                new ApproveSupplierPaymentRequest(
                    paymentOne.Id, financeManagerUserId, "YES", "Actual BPI transfer confirmed",
                    ActualDisbursementDate: actualTransferDateText));
            paymentOne = RequireSuccess(settlementResult, "Confirm synthetic supplier transfer");
            Require(paymentOne.Status == SupplierPaymentStatuses.Settled &&
                    paymentOne.PaymentDate == requestedPaymentDate &&
                    paymentOne.ActualDisbursementDate == actualTransferDate &&
                    paymentOne.ApprovedByUserId == financeManagerUserId &&
                    paymentOne.ApprovedAt.HasValue,
                "Settlement must retain requested date, exact actual transfer date and authenticated confirmation actor/time.");

            SupplierInvoiceModel invoiceAfterPayment = RequireSuccess(
                await service.GetSupplierInvoiceByIdAsync(invoiceAlpha.Id),
                "Reload supplier invoice after settlement");
            Require(invoiceAfterPayment.PaidAmountCents == invoiceBeforePayment.PaidAmountCents + paymentOne.AmountCents &&
                    invoiceAfterPayment.RemainingBalanceCents == invoiceBeforePayment.RemainingBalanceCents - paymentOne.AmountCents &&
                    invoiceAfterPayment.Status == "PartiallyPaid",
                "Partial settlement must post exactly 100,000 cents and preserve the remaining AP liability.");

            // Independently verify disposable SQLite before attempting any browser capture.
            using (SqliteConnection verification = new($"Data Source={paths.DatabasePath}"))
            {
                verification.Open();
                using SqliteCommand check = verification.CreateCommand();

                check.CommandText = """
                    SELECT payment_date, actual_disbursement_date, approved_at, approved_by_user_id, status
                    FROM supplier_payments WHERE id=$id;
                    """;
                check.Parameters.AddWithValue("$id", paymentOne.Id.ToString("D"));
                using (SqliteDataReader persistedPayment = check.ExecuteReader())
                {
                    Require(persistedPayment.Read(), "Settled supplier payment must persist.");
                    Require(persistedPayment.GetString(0) == requestedPaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
                            persistedPayment.GetString(1) == actualTransferDateText &&
                            !persistedPayment.IsDBNull(2) &&
                            persistedPayment.GetString(3) == financeManagerUserId.ToString("D") &&
                            persistedPayment.GetString(4) == SupplierPaymentStatuses.Settled,
                        "SQLite payment must persist three-date custody, actual approver and Settled status.");
                    Require(!persistedPayment.Read(), "Settlement must persist as one payment record.");
                }

                check.Parameters.Clear();
                check.CommandText = """
                    SELECT paid_amount_cents, remaining_balance_cents, status
                    FROM supplier_invoices WHERE id=$id;
                    """;
                check.Parameters.AddWithValue("$id", invoiceAlpha.Id.ToString("D"));
                using (SqliteDataReader persistedInvoice = check.ExecuteReader())
                {
                    Require(persistedInvoice.Read() &&
                            persistedInvoice.GetInt64(0) == invoiceAfterPayment.PaidAmountCents &&
                            persistedInvoice.GetInt64(1) == invoiceAfterPayment.RemainingBalanceCents &&
                            persistedInvoice.GetString(2) == "PartiallyPaid",
                        "SQLite supplier invoice accounting must agree with the service readback.");
                    Require(!persistedInvoice.Read(), "Invoice accounting must persist in one row.");
                }

                check.Parameters.Clear();
                check.CommandText = """
                    SELECT COUNT(*), COALESCE(SUM(debit_cents), 0), COALESCE(SUM(credit_cents), 0)
                    FROM supplier_payable_entries
                    WHERE supplier_invoice_id=$invoice AND entry_type='PaymentApplied';
                    """;
                check.Parameters.AddWithValue("$invoice", invoiceAlpha.Id.ToString("D"));
                using (SqliteDataReader ap = check.ExecuteReader())
                {
                    Require(ap.Read() && ap.GetInt64(0) == 1 &&
                            ap.GetInt64(1) == paymentOne.AmountCents && ap.GetInt64(2) == 0,
                        "Exactly one AP PaymentApplied debit must be posted for the settled partial payment.");
                }

                check.Parameters.Clear();
                check.CommandText = """
                    SELECT user_id, new_value_json FROM audit_logs
                    WHERE entity_type='SupplierPayment' AND entity_id=$id
                      AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED';
                    """;
                check.Parameters.AddWithValue("$id", paymentOne.Id.ToString("D"));
                using (SqliteDataReader audit = check.ExecuteReader())
                {
                    Require(audit.Read() && audit.GetString(0) == financeManagerUserId.ToString("D"),
                        "Settlement audit must identify the actual authenticated finance manager.");
                    using JsonDocument data = JsonDocument.Parse(audit.GetString(1));
                    JsonElement facts = data.RootElement;
                    Require(facts.GetProperty("RequestedPaymentDate").GetString() == requestedPaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
                            facts.GetProperty("ActualDisbursementDate").GetString() == actualTransferDateText &&
                            facts.GetProperty("SettlementTimestamp").GetDateTimeOffset() == paymentOne.ApprovedAt!.Value &&
                            facts.GetProperty("SettlementActorUserId").GetString() == financeManagerUserId.ToString("D") &&
                            facts.GetProperty("AppliedApLedgerAmountCents").GetInt64() == paymentOne.AmountCents,
                        "Structured audit must preserve requested, actual and confirmation dates, actor and AP debit.");
                    Require(!audit.Read(), "Exactly one successful settlement audit event is expected.");
                }

                check.Parameters.Clear();
                check.CommandText = "SELECT COUNT(*) FROM cash_drawer_events;";
                Require(Convert.ToInt64(check.ExecuteScalar()) == 0,
                    "Supplier AP payment must not create any POS cashier drawer events.");
            }
            Console.WriteLine($"[LPI Evidence] Synthetic settlement verified: requested={requestedPaymentDate:yyyy-MM-dd}, actual={actualTransferDateText}, confirmed={paymentOne.ApprovedAt:O}, AP debit={paymentOne.AmountCents} cents.");

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
            string screenshotRoot = Path.Combine(Path.GetTempPath(), "salryn-lpi-evidence-screenshots-" + Guid.NewGuid().ToString("N"));
            ProcessStartInfo psi = new("node")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(boundUrl);
            psi.ArgumentList.Add(session.SessionId);
            psi.ArgumentList.Add(screenshotRoot);
            Console.WriteLine($"[LPI Evidence] Screenshot custody root: {screenshotRoot}");

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
                throw new InvalidOperationException($"Screenshot capture failed with exit code {proc.ExitCode}; output root: {screenshotRoot}");
            }

            string[] screenshots = Directory.GetFiles(screenshotRoot, "*.png", SearchOption.TopDirectoryOnly);
            Require(screenshots.Length == 8, $"Expected 8 screenshots, found {screenshots.Length} in {screenshotRoot}.");
            foreach (string screenshotPath in screenshots)
            {
                using FileStream screenshot = File.OpenRead(screenshotPath);
                byte[] pngSignature = new byte[8];
                Require(screenshot.Length > 24 && screenshot.Read(pngSignature, 0, pngSignature.Length) == pngSignature.Length &&
                        pngSignature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                    $"Screenshot must contain a nonempty PNG header: {screenshotPath}");
            }
            Console.WriteLine($"[LPI Evidence] Screenshot custody: {screenshots.Length} PNG files in {screenshotRoot}; rendered-content inspection remains an independent QA gate.");

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

    private static T RequireSuccess<T>(Result<T> result, string operation)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
        }
        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"LPI evidence assertion failed: {message}");
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

using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

/// <summary>
/// TRAD-M2: activation default-off host facade + tax-mode fail-closed on atomic path contracts.
/// Not accreditation. Not checkout wire-up.
/// </summary>
public static class RmoBirTradM2AtomicOfficialPostingChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        AssertDocs(projectRoot);
        AssertSourceInvariants(projectRoot);
        AssertActivationDefaultOff();
        AssertTaxModeRequiredOnFacade();
        Console.WriteLine("[TRAD-M2] Atomic official posting checks passed.");
    }

    private static void AssertDocs(string projectRoot)
    {
        string study = Path.Combine(projectRoot, "00-HANDOFF", "RMO_BIR_TRAD_M2_ATOMIC_OFFICIAL_POSTING_STUDY.md");
        RequireTrue(File.Exists(study), "TRAD-M2 study required.");
    }

    private static void AssertSourceInvariants(string projectRoot)
    {
        string persistence = File.ReadAllText(Path.Combine(
            projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutPersistenceService.cs"));
        string facade = File.ReadAllText(Path.Combine(
            projectRoot, "src", "Salryn.Core", "Services", "HostAtomicOfficialPostingService.cs"));
        string activation = File.ReadAllText(Path.Combine(
            projectRoot, "src", "Salryn.Core", "Services", "ProductionOfficialPostingActivation.cs"));
        string coreSmoke = File.ReadAllText(Path.Combine(
            projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        RequireContains(persistence, "OfficialCheckout.TaxpayerModeRequired", "Persistence must require taxpayer mode.");
        RequireContains(persistence, "LineTaxClassificationNotAllowed", "Persistence must fail closed on bad classifications.");
        RequireContains(persistence, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "Must allocate in shared tx.");
        RequireContains(persistence, "SqliteElectronicJournalAppendParticipant", "Must append EJ in shared tx.");
        RequireContains(facade, "ProductionOfficialPostingActivation", "Facade must consult activation.");
        RequireContains(facade, "ProductionOfficialPostingActivation.DisabledCode", "Facade must use stable disabled code path.");
        RequireContains(activation, "AtomicOfficialPosting.ActivationDisabled", "Activation disabled code constant required.");
        RequireContains(activation, "IsEnabled", "Activation switch required.");
        RequireContains(activation, "false", "Default off must be explicit in source.");
        RequireContains(coreSmoke, "rmo-bir-trad-atomic-posting", "CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM2AtomicOfficialPostingChecks.Run()", "CoreSmoke must run TRAD-M2.");
        RequireFalse(facade.Contains("is accredited", StringComparison.OrdinalIgnoreCase), "No accreditation claim.");
        RequireFalse(activation.Contains("is compliant", StringComparison.OrdinalIgnoreCase), "No compliance claim.");
    }

    private static void AssertActivationDefaultOff()
    {
        ProductionOfficialPostingActivation.ResetForTests();
        HostAtomicOfficialPostingService service = new();

        Result<HostAtomicOfficialPostingStatus> status = service.GetStatus();
        RequireTrue(status.IsSuccess, "Status must succeed.");
        RequireFalse(status.Value.ProductionActivationEnabled, "Activation must default OFF.");
        RequireFalse(status.Value.LiveOfficialPostingAllowed, "Live posting allowed must remain false on status.");

        Result<HostAtomicOfficialPostingResult> blocked = service.PostAtomicOfficialSaleAsync(
            BuildRequest(TaxpayerRegistrationMode.VatRegistered, OfficialDocumentLineTaxClassification.Vatable))
            .GetAwaiter().GetResult();

        RequireTrue(blocked.IsFailure, "Post must fail when activation OFF.");
        RequireTrue(
            blocked.ErrorDetail.Code == ProductionOfficialPostingActivation.DisabledCode,
            "Must use ActivationDisabled code, was " + blocked.ErrorDetail.Code);

        // Even if activation ON without executor, still fail closed.
        ProductionOfficialPostingActivation.SetEnabledForTests(true);
        try
        {
            HostAtomicOfficialPostingService noExecutor = new(executor: null);
            Result<HostAtomicOfficialPostingResult> noExec = noExecutor.PostAtomicOfficialSaleAsync(
                BuildRequest(TaxpayerRegistrationMode.VatRegistered, OfficialDocumentLineTaxClassification.Vatable))
                .GetAwaiter().GetResult();
            RequireTrue(noExec.IsFailure, "Missing executor must fail closed.");
            RequireTrue(
                noExec.ErrorDetail.Code == "AtomicOfficialPosting.ExecutorNotComposed",
                "Missing executor code expected.");
        }
        finally
        {
            ProductionOfficialPostingActivation.ResetForTests();
        }
    }

    private static void AssertTaxModeRequiredOnFacade()
    {
        ProductionOfficialPostingActivation.SetEnabledForTests(true);
        try
        {
            bool executorCalled = false;
            HostAtomicOfficialPostingService service = new(async (request, freeze, engineMode, ct) =>
            {
                executorCalled = true;
                return Result<HostAtomicOfficialPostingResult>.Success(new HostAtomicOfficialPostingResult(
                    request.CheckoutSale.Sale.Id,
                    "INV-2026-000001",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    freeze,
                    ActivationWasRequired: true,
                    Summary: "Test executor only."));
            });

            Result<HostAtomicOfficialPostingResult> reject = service.PostAtomicOfficialSaleAsync(
                BuildRequest(TaxpayerRegistrationMode.NonVatPercentageTax, OfficialDocumentLineTaxClassification.Vatable))
                .GetAwaiter().GetResult();
            RequireTrue(reject.IsFailure, "Non-VAT mode must reject VATable lines.");
            RequireFalse(executorCalled, "Executor must not run on classification failure.");

            Result<HostAtomicOfficialPostingResult> ok = service.PostAtomicOfficialSaleAsync(
                BuildRequest(TaxpayerRegistrationMode.VatRegistered, OfficialDocumentLineTaxClassification.Vatable))
                .GetAwaiter().GetResult();
            RequireTrue(ok.IsSuccess, "Valid VAT path should reach executor: " + (ok.IsFailure ? ok.ErrorDetail.Message : ""));
            RequireTrue(executorCalled, "Executor should run for valid request.");
            RequireTrue(
                ok.Value.TaxPolicySnapshot.VatRegistrationModeCode == TaxpayerRegistrationModeCodes.VatRegistered,
                "Freeze must carry VAT_REGISTERED.");
            RequireTrue(
                ok.Value.TaxPolicySnapshot.PrimarySalesDocumentKind == SalrynInvoiceDocumentPolicy.PrimarySalesDocumentKind,
                "Freeze must be Invoice-first.");
        }
        finally
        {
            ProductionOfficialPostingActivation.ResetForTests();
        }
    }

    private static HostAtomicOfficialPostingRequest BuildRequest(
        TaxpayerRegistrationMode mode,
        OfficialDocumentLineTaxClassification classification)
    {
        Guid saleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "PENDING",
            SaleDate = now,
            CashierUserId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            CashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Subtotal = Money.FromCents(11200),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(11200),
            AmountPaid = Money.FromCents(11200),
            Change = Money.FromCents(0),
            Status = SaleStatus.Completed,
            CreatedAt = now
        };

        SaleItem item = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            ProductNameSnapshot = "Item",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromX1000(1000).Value,
            UnitPrice = Money.FromCents(11200),
            LineTotal = Money.FromCents(11200)
        };

        return new HostAtomicOfficialPostingRequest(
            new CompleteCheckoutSaleRequest(sale, new[] { item }, Array.Empty<Payment>(), Array.Empty<StockMovement>()),
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DocumentKind: SalrynInvoiceDocumentPolicy.PrimarySalesDocumentKind,
            SequenceScopeCode: "LOCAL_SINGLE_STORE",
            Year: 2026,
            SnapshotDocumentKind: OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            CashierIdentifier: "cashier@test",
            TerminalIdentifier: "POS-1",
            RegistrationMode: mode,
            LineTaxClassifications: new[] { classification });
    }

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message) => RequireTrue(!condition, message);

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }
}

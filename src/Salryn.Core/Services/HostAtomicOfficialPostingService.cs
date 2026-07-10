using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

public sealed record HostAtomicOfficialPostingRequest(
    CompleteCheckoutSaleRequest CheckoutSale,
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year,
    OfficialDocumentSnapshotDraftKind SnapshotDocumentKind,
    string CashierIdentifier,
    string? TerminalIdentifier,
    TaxpayerRegistrationMode RegistrationMode,
    IReadOnlyList<OfficialDocumentLineTaxClassification> LineTaxClassifications);

public sealed record HostAtomicOfficialPostingResult(
    Guid SaleId,
    string OfficialDocumentNumber,
    Guid JournalEntryId,
    Guid OriginalEventId,
    DateTimeOffset AppendedAt,
    OfficialTaxPolicySnapshot TaxPolicySnapshot,
    bool ActivationWasRequired,
    string Summary);

/// <summary>
/// Host-facing atomic official posting facade. Activation default OFF.
/// Delegates the real SQLite shared transaction to infrastructure when composed.
/// </summary>
public interface IHostAtomicOfficialPostingService
{
    Task<Result<HostAtomicOfficialPostingResult>> PostAtomicOfficialSaleAsync(
        HostAtomicOfficialPostingRequest request,
        CancellationToken cancellationToken = default);

    Result<HostAtomicOfficialPostingStatus> GetStatus();
}

public sealed record HostAtomicOfficialPostingStatus(
    bool ProductionActivationEnabled,
    bool LiveOfficialPostingAllowed,
    string PrimaryDocumentKind,
    string Summary,
    IReadOnlyList<string> Notes);

public delegate Task<Result<HostAtomicOfficialPostingResult>> HostAtomicOfficialPostingExecutor(
    HostAtomicOfficialPostingRequest request,
    OfficialTaxPolicySnapshot taxPolicySnapshot,
    OfficialDocumentTaxBucketDraftTaxpayerMode engineMode,
    CancellationToken cancellationToken);

/// <summary>
/// Core facade: validates tax spine + activation, then optionally delegates to injected executor.
/// </summary>
public sealed class HostAtomicOfficialPostingService : IHostAtomicOfficialPostingService
{
    private readonly HostAtomicOfficialPostingExecutor? _executor;

    public HostAtomicOfficialPostingService(HostAtomicOfficialPostingExecutor? executor = null)
    {
        _executor = executor;
    }

    public Result<HostAtomicOfficialPostingStatus> GetStatus()
    {
        bool enabled = ProductionOfficialPostingActivation.IsEnabled;
        return Result<HostAtomicOfficialPostingStatus>.Success(new HostAtomicOfficialPostingStatus(
            ProductionActivationEnabled: enabled,
            LiveOfficialPostingAllowed: false,
            PrimaryDocumentKind: SalrynInvoiceDocumentPolicy.PrimarySalesDocumentKind,
            Summary: enabled
                ? "Production activation flag is ON in this process — still not a compliance claim; readiness gates still apply."
                : OfficialSafetyConstants.LivePostingBlocked,
            Notes: new[]
            {
                ProductionOfficialPostingActivation.NotAComplianceClaimNote,
                SalrynInvoiceDocumentPolicy.InvoiceFirstBoundaryNote,
                SalrynInvoiceDocumentPolicy.NoAccreditationClaimNote,
                OfficialSafetyConstants.CheckoutNotConnected
            }));
    }

    public async Task<Result<HostAtomicOfficialPostingResult>> PostAtomicOfficialSaleAsync(
        HostAtomicOfficialPostingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null)
        {
            return Result<HostAtomicOfficialPostingResult>.Fail(Error.Validation(
                "AtomicOfficialPosting.RequestRequired",
                "Atomic official posting request is required."));
        }

        if (!ProductionOfficialPostingActivation.IsEnabled)
        {
            return Result<HostAtomicOfficialPostingResult>.Fail(Error.Forbidden(
                ProductionOfficialPostingActivation.DisabledCode,
                ProductionOfficialPostingActivation.DisabledMessage));
        }

        Result modeOk = SalrynTaxpayerTaxPolicy.RequireKnownRegistrationMode(request.RegistrationMode);
        if (modeOk.IsFailure)
        {
            return Result<HostAtomicOfficialPostingResult>.Fail(modeOk.ErrorDetail);
        }

        if (request.LineTaxClassifications is null || request.LineTaxClassifications.Count == 0)
        {
            return Result<HostAtomicOfficialPostingResult>.Fail(Error.Validation(
                "AtomicOfficialPosting.LineTaxClassificationsRequired",
                "Per-line tax classifications are required."));
        }

        OfficialDocumentTaxBucketDraftTaxpayerMode engineMode =
            SalrynTaxpayerTaxPolicy.ToEngineTaxpayerMode(request.RegistrationMode);

        foreach (OfficialDocumentLineTaxClassification classification in request.LineTaxClassifications)
        {
            if (!SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(engineMode, classification))
            {
                return Result<HostAtomicOfficialPostingResult>.Fail(Error.Validation(
                    "AtomicOfficialPosting.LineTaxClassificationNotAllowed",
                    "Line tax classification is not allowed for the installation taxpayer mode."));
            }
        }

        OfficialTaxPolicySnapshot freeze = SalrynTaxpayerTaxPolicy.Freeze(request.RegistrationMode);

        if (_executor is null)
        {
            return Result<HostAtomicOfficialPostingResult>.Fail(Error.Forbidden(
                "AtomicOfficialPosting.ExecutorNotComposed",
                "Atomic official posting executor is not composed. Host posting remains blocked."));
        }

        return await _executor(request, freeze, engineMode, cancellationToken).ConfigureAwait(false);
    }
}

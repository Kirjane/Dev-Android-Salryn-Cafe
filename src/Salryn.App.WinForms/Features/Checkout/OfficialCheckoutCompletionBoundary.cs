using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed record OfficialCheckoutPersistenceBoundaryRequest(
    CompleteCheckoutSaleRequest CheckoutSale,
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year,
    OfficialDocumentSnapshotDraftKind SnapshotDocumentKind,
    string CashierIdentifier,
    string? TerminalIdentifier,
    string TaxModeCode,
    string? TaxModeNotes);

public interface IOfficialCheckoutPersistenceBoundary
{
    Task<Result> CompleteOfficialSaleAsync(
        OfficialCheckoutPersistenceBoundaryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOfficialCheckoutCompletionBoundary
{
    Result<CheckoutCompletionReadiness> Evaluate(PosCheckoutDraftTransaction transaction);

    Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialCheckoutCompletionBoundary : IOfficialCheckoutCompletionBoundary
{
    private const string TaxModeCode = "VAT_BUCKET_ENGINE_DEFERRED";
    private const string TaxModeNotes = "Official checkout completion boundary keeps VAT bucket engine deferred until C05 is implemented.";

    private readonly ITrustedPosCheckoutSaleBuilder _trustedSaleBuilder;
    private readonly ICashierSessionService _cashierSessionService;
    private readonly IOfficialCheckoutReadinessService _readinessService;
    private readonly IOfficialCheckoutPersistenceBoundary _officialPersistence;
    private readonly CheckoutCompletionBoundary _completionReadiness = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?> _terminalIdentifierProvider;

    public OfficialCheckoutCompletionBoundary(
        ITrustedPosCheckoutSaleBuilder trustedSaleBuilder,
        ICashierSessionService cashierSessionService,
        IOfficialCheckoutReadinessService readinessService,
        IOfficialCheckoutPersistenceBoundary officialPersistence,
        Func<DateTimeOffset>? clock = null,
        Func<string?>? terminalIdentifierProvider = null)
    {
        _trustedSaleBuilder = trustedSaleBuilder ?? throw new ArgumentNullException(nameof(trustedSaleBuilder));
        _cashierSessionService = cashierSessionService ?? throw new ArgumentNullException(nameof(cashierSessionService));
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _officialPersistence = officialPersistence ?? throw new ArgumentNullException(nameof(officialPersistence));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _terminalIdentifierProvider = terminalIdentifierProvider ?? (() => Environment.MachineName);
    }

    public Result<CheckoutCompletionReadiness> Evaluate(PosCheckoutDraftTransaction transaction)
    {
        return _completionReadiness.Evaluate(transaction);
    }

    public async Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.RequestRequired",
                "Checkout request is required."));
        }

        DateTimeOffset stableSaleDate = request.SaleDate ?? _clock();
        PosCheckoutApplicationRequest stableRequest = request.SaleDate.HasValue
            ? request
            : request with { SaleDate = stableSaleDate };

        // Kirch: why - official completion must share the trusted sale builder but must not persist through the standard sale path.
        Result<CompleteCheckoutSaleRequest> trustedSaleResult =
            await _trustedSaleBuilder.BuildAsync(stableRequest, cancellationToken).ConfigureAwait(false);

        if (trustedSaleResult.IsFailure)
        {
            return Result.Fail(trustedSaleResult.ErrorDetail);
        }

        // Kirch: why - official completion bypasses standard sale persistence, so it must enforce the same open shift gate here.
        Result sessionResult = await EnsureOpenCashierSessionAsync(stableRequest, cancellationToken).ConfigureAwait(false);

        if (sessionResult.IsFailure)
        {
            return sessionResult;
        }

        Result<OfficialCheckoutReadinessResult> readinessResult =
            await _readinessService.CheckActiveScopeAsync(
                new OfficialCheckoutActiveScopeReadinessRequest(
                    CheckoutOfficialModeReadinessBoundary.LocalSingleStoreScopeCode,
                    CheckoutOfficialModeReadinessBoundary.OfficialReceiptDocumentKind,
                    CheckoutOfficialModeReadinessBoundary.LocalSingleStoreScopeCode,
                    stableSaleDate.Year),
                cancellationToken).ConfigureAwait(false);

        if (readinessResult.IsFailure)
        {
            return Result.Fail(readinessResult.ErrorDetail);
        }

        OfficialCheckoutReadinessResult readiness = readinessResult.Value;

        if (!readiness.IsReady || readiness.MachineProfile is null)
        {
            return Result.Fail(Error.Forbidden(
                "OfficialCheckout.ReadinessBlocked",
                BuildReadinessBlockedMessage(readiness)));
        }

        OfficialCheckoutPersistenceBoundaryRequest persistenceRequest = new(
            trustedSaleResult.Value,
            readiness.BirMachineProfileId,
            readiness.DocumentKind,
            readiness.SequenceScopeCode,
            readiness.Year,
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            CashierIdentifier: stableRequest.CashierUserId.ToString("D"),
            TerminalIdentifier: _terminalIdentifierProvider(),
            TaxModeCode: TaxModeCode,
            TaxModeNotes: TaxModeNotes);

        Result persistenceResult =
            await _officialPersistence.CompleteOfficialSaleAsync(
                persistenceRequest,
                cancellationToken).ConfigureAwait(false);

        if (persistenceResult.IsFailure)
        {
            return Result.Fail(persistenceResult.ErrorDetail);
        }

        return Result.Success();
    }

    private async Task<Result> EnsureOpenCashierSessionAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken)
    {
        Result<CashierSessionReadModel?> openSessionResult =
            await _cashierSessionService.FindOpenSessionByUserIdAsync(
                request.CashierUserId,
                cancellationToken).ConfigureAwait(false);

        if (openSessionResult.IsFailure)
        {
            return Result.Fail(openSessionResult.ErrorDetail);
        }

        CashierSessionReadModel? openSession = openSessionResult.Value;

        if (openSession is null)
        {
            return Result.Fail(Error.Conflict(
                "Checkout.OpenCashierSessionRequired",
                "An open cashier session is required before completing a sale."));
        }

        if (openSession.UserId != request.CashierUserId)
        {
            return Result.Fail(Error.Unexpected(
                "Checkout.OpenSessionUserMismatch",
                "Open cashier session user does not match the sale cashier."));
        }

        if (request.CashierSessionId != openSession.Id)
        {
            return Result.Fail(Error.Validation(
                "Checkout.CashierSessionMismatch",
                "Sale cashier session must match the currently open cashier session."));
        }

        return Result.Success();
    }

    private static string BuildReadinessBlockedMessage(OfficialCheckoutReadinessResult readiness)
    {
        if (readiness.Blocks.Count == 0)
        {
            return "Official checkout is not ready because trusted persisted machine profile evidence is unavailable.";
        }

        return "Official checkout is not ready for official output: "
            + string.Join(" ", readiness.Blocks.Select(block => $"{block.Code}: {block.Message}"));
    }
}

using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.App.WinForms.Composition;

internal sealed class SqliteOfficialCheckoutPersistenceBoundary : IOfficialCheckoutPersistenceBoundary
{
    private readonly SqliteOfficialCheckoutPersistenceService _officialPersistence;

    public SqliteOfficialCheckoutPersistenceBoundary(
        SqliteOfficialCheckoutPersistenceService officialPersistence)
    {
        _officialPersistence = officialPersistence ?? throw new ArgumentNullException(nameof(officialPersistence));
    }

    public async Task<Result> CompleteOfficialSaleAsync(
        OfficialCheckoutPersistenceBoundaryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.PersistenceBoundaryRequestRequired",
                "Official checkout persistence boundary request is required."));
        }

        // Kirch: why - SQLite official persistence stays behind composition so checkout UI cannot depend on infrastructure types.
        Result<SqliteOfficialCheckoutPersistenceResult> result =
            await _officialPersistence.CompleteOfficialSaleAsync(
                new SqliteOfficialCheckoutPersistenceRequest(
                    request.CheckoutSale,
                    new OfficialDocumentSequenceAllocationRequest(
                        request.BirMachineProfileId,
                        request.DocumentKind,
                        request.SequenceScopeCode,
                        request.Year),
                    request.SnapshotDocumentKind,
                    request.CashierIdentifier,
                    request.TerminalIdentifier,
                    request.TaxModeCode,
                    request.TaxModeNotes),
                cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Fail(result.ErrorDetail)
            : Result.Success();
    }
}

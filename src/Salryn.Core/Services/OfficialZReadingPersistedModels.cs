using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialZReadingPersistedRecord(
    OfficialZReadingSummary Summary,
    IReadOnlyList<OfficialXReadingPaymentSummary> Payments,
    IReadOnlyList<OfficialXReadingTaxModeSummary> TaxModes,
    OfficialZReadingEnrichmentPayload? Enrichment);

public interface IOfficialZReadingReadService
{
    Task<Result<OfficialZReadingPersistedRecord>> ReadByIdAsync(
        Guid zReadingId,
        CancellationToken cancellationToken = default);
}

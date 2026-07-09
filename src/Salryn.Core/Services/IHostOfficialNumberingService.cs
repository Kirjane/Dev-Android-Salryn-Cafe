using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official numbering preview/validation (RMO-BIR-M5).
/// Does not advance series, post sales, or allow live official allocation.
/// </summary>
public interface IHostOfficialNumberingService
{
    Task<Result<OfficialNumberingPreviewContract>> GetPreviewAsync(
        CancellationToken cancellationToken = default);

    Task<Result<OfficialNumberingValidationResult>> ValidateSeriesAsync(
        CancellationToken cancellationToken = default);
}

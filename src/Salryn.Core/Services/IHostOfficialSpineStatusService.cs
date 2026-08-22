using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local aggregated RMO pre-live spine status (RMO-BIR-M7).
/// </summary>
public interface IHostOfficialSpineStatusService
{
    Task<Result<OfficialSpineStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

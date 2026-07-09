using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official integrity status (RMO-BIR-M6).
/// Reports EJ + void/refund skeleton state. Never mutates sales or posts official documents.
/// </summary>
public interface IHostOfficialIntegrityService
{
    Task<Result<OfficialIntegrityStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

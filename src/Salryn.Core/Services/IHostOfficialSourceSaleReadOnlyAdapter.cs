using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local read-only adapter from operational sales into official dry-run planning inputs.
/// Must never mutate sales, sequences, EJ, shift, payment, or inventory.
/// </summary>
public interface IHostOfficialSourceSaleReadOnlyAdapter
{
    Task<Result<OfficialSourceSaleReadOnlyResult>> ReadAsync(
        OfficialSourceSaleReadOnlyRequest request,
        CancellationToken cancellationToken = default);
}

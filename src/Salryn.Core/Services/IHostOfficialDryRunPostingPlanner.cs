using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official sale dry-run planner (RMO-BIR-M7).
/// Builds planning package only — never mutates sales, sequences, or EJ.
/// </summary>
public interface IHostOfficialDryRunPostingPlanner
{
    Task<Result<OfficialDryRunPostingPlan>> BuildPlanAsync(
        OfficialDryRunPostingRequest request,
        CancellationToken cancellationToken = default);
}

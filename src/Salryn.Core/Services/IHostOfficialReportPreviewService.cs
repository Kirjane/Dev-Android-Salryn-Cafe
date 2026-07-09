using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local internal report preview builder (RMO-BIR-M8).
/// Never generates legal output, PDF, printer jobs, or Crystal reports.
/// </summary>
public interface IHostOfficialReportPreviewService
{
    Task<Result<OfficialReportPreviewPackageContract>> BuildPreviewAsync(
        OfficialDryRunPostingPlan plan,
        CancellationToken cancellationToken = default);
}

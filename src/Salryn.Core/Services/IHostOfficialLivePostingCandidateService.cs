using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Guarded internal live-posting candidate. Disabled by default; always blocked until legal/product gates pass.
/// Must not be called from checkout.
/// </summary>
public interface IHostOfficialLivePostingCandidateService
{
    Task<Result<OfficialLivePostingCandidateResult>> EvaluateAsync(
        OfficialLivePostingCandidateRequest request,
        CancellationToken cancellationToken = default);
}

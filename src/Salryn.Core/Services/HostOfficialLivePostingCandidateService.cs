using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Always-blocked guarded live posting candidate (RMO-BIR-M9).
/// Disabled by default. Not production activation. Not checkout-bound.
/// </summary>
public sealed class HostOfficialLivePostingCandidateService : IHostOfficialLivePostingCandidateService
{
    public Task<Result<OfficialLivePostingCandidateResult>> EvaluateAsync(
        OfficialLivePostingCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<OfficialLivePostingCandidateBlocker> blockers = new()
        {
            new(
                "HostOfficialLivePostingCandidate.DisabledByDefault",
                "Live official posting candidate is disabled by default."),
            new(
                "HostOfficialLivePostingCandidate.LegalSourceClosureIncomplete",
                OfficialSafetyConstants.LegalClosureIncomplete),
            new(
                "HostOfficialLivePostingCandidate.LiveCheckoutBindingStopped",
                "Live checkout binding is stopped; official posting is not wired to checkout."),
            new(
                "HostOfficialLivePostingCandidate.NoProductionActivation",
                "Production activation is not available."),
            new(
                "HostOfficialLivePostingCandidate.OutputEngineMissing",
                "Official output engine is not implemented.")
        };

        if (request?.ExplicitInternalActivationRequested == true)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.ExplicitActivationRejected",
                "Explicit internal activation was requested but is rejected while legal/source gates are incomplete."));
        }

        List<OfficialLivePostingCandidateWarning> warnings = new()
        {
            new(
                "HostOfficialLivePostingCandidate.CandidateOnly",
                "This service is an internal candidate evaluation only."),
            new(
                "HostOfficialLivePostingCandidate.NoComplianceClaim",
                "No BIR compliance, accreditation, or certification is claimed.")
        };

        OfficialLivePostingCandidateResult result = new(
            ContractVersion: OfficialContractVersions.M3,
            CandidateOnly: true,
            DisabledByDefault: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            OfficialReceiptIssued: false,
            ComplianceClaimed: false,
            ProductionActivation: false,
            Blocked: true,
            Blockers: blockers,
            Warnings: warnings,
            Summary:
                "Live official posting candidate remains blocked. " +
                OfficialSafetyConstants.LegalClosureIncomplete + " " +
                OfficialSafetyConstants.NoOfficialNumberIssued);

        return Task.FromResult(Result<OfficialLivePostingCandidateResult>.Success(result));
    }
}

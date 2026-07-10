using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Always-blocked guarded live posting candidate (RMO-BIR-M9/M11).
/// Dual-track planning only. Disabled by default. Not production activation. Not checkout-bound.
/// Never mutates sequence, sales, or EJ. Never generates official output. Never transmits EIS/eSales.
/// </summary>
public sealed class HostOfficialLivePostingCandidateService : IHostOfficialLivePostingCandidateService
{
    public const bool EisJsonSchemaResolved = false;

    public Task<Result<OfficialLivePostingCandidateResult>> EvaluateAsync(
        OfficialLivePostingCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<OfficialLivePostingCandidateResult>.Success(
            BuildBlockedPlan(request, numberingPreviewHint: null, setupPresent: false, seriesActive: false)));
    }

    /// <summary>
    /// Enriched evaluate path used by host infrastructure after read-only probes.
    /// Still always blocked before mutation.
    /// </summary>
    public Task<Result<OfficialLivePostingCandidateResult>> EvaluateEnrichedAsync(
        OfficialLivePostingCandidateRequest request,
        string? numberingPreviewHint,
        bool setupPresent,
        bool seriesActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<OfficialLivePostingCandidateResult>.Success(
            BuildBlockedPlan(request, numberingPreviewHint, setupPresent, seriesActive)));
    }

    public Task<Result<OfficialLivePostingCandidateStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OfficialLivePostingCandidateStatusContract status = new(
            ContractVersion: OfficialContractVersions.M3,
            CandidateServicePresent: true,
            CandidateOnly: true,
            DisabledByDefault: true,
            LiveOfficialPostingAllowed: false,
            CheckoutBound: false,
            Blocked: true,
            EisJsonSchemaResolved: EisJsonSchemaResolved,
            DefaultTrack: OfficialLivePostingCandidateTrack.TraditionalCrmPos,
            Summary:
                "Live posting candidate service is present and blocked. " +
                "Internal candidate plan only. Checkout is not connected. " +
                OfficialSafetyConstants.NoOfficialNumberIssued);

        return Task.FromResult(Result<OfficialLivePostingCandidateStatusContract>.Success(status));
    }

    public static OfficialLivePostingCandidateFlags ForcedBlockedFlags() =>
        new(
            CandidateOnly: true,
            DisabledByDefault: true,
            DryRunOnly: true,
            NonOfficial: true,
            LiveOfficialPostingAllowed: false,
            CheckoutBound: false,
            RemotePosAllowed: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            EisWillTransmit: false,
            OfficialReceiptIssued: false,
            ComplianceClaimed: false,
            ProductionActivation: false);

    public static OfficialLivePostingCandidateResult BuildBlockedPlan(
        OfficialLivePostingCandidateRequest? request,
        string? numberingPreviewHint,
        bool setupPresent,
        bool seriesActive)
    {
        OfficialLivePostingCandidateTrack track = ResolveTrack(request);
        string documentKind = string.IsNullOrWhiteSpace(request?.DocumentKind)
            ? "OFFICIAL_INVOICE"
            : request!.DocumentKind!.Trim();

        List<OfficialLivePostingCandidateBlocker> blockers = BuildCoreBlockers(track);
        if (request?.ExplicitInternalActivationRequested == true)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.ExplicitActivationRejected",
                "Explicit internal activation was requested but is rejected; production activation is not allowed."));
        }

        if (!setupPresent)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.SetupNotVerified",
                "Official setup was not verified as complete for this candidate plan."));
        }

        if (!seriesActive)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.SeriesNotVerifiedActive",
                "Active document series was not verified for this candidate plan."));
        }

        if (track == OfficialLivePostingCandidateTrack.EisInvoiceReceipt)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.EisJsonSchemaUnresolved",
                "EIS JSON API field schema remains unresolved; EIS track is modeled as blocked candidate only."));
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.EisCertPttMissing",
                "EIS CERT and Permit to Transmit are not productized; EIS transmission is not implemented."));
        }

        List<OfficialLivePostingCandidateWarning> warnings = new()
        {
            new(
                "HostOfficialLivePostingCandidate.CandidateOnly",
                "This service is an internal candidate evaluation only."),
            new(
                "HostOfficialLivePostingCandidate.NoComplianceClaim",
                "No BIR compliance, accreditation, or certification is claimed."),
            new(
                "HostOfficialLivePostingCandidate.DualTrackModeled",
                "Traditional CRM/POS and EIS e-invoice/e-receipt tracks are modeled; both remain blocked before mutation."),
            new(
                "HostOfficialLivePostingCandidate.NumberPreviewOnly",
                "Official number is preview-only; sequence will not mutate.")
        };

        if (request?.OperationalSaleId is Guid saleId && saleId != Guid.Empty)
        {
            warnings.Add(new OfficialLivePostingCandidateWarning(
                "HostOfficialLivePostingCandidate.SourceSaleReadOnly",
                "Source sale id was provided for planning; sale is not mutated."));
        }

        OfficialLivePostingCandidateFlags flags = ForcedBlockedFlags();

        OfficialLivePostingCandidateAtomicPlan atomic = new(
            SameTransactionForNumberSaleAndEj: true,
            AllocateOnlyOnCommit: true,
            NoDraftCartAllocation: true,
            NoRemoteClientAllocation: true,
            Summary:
                "Future live path must allocate official number, write immutable official sale, and append EJ in one host transaction. " +
                "Not executed in this candidate.");

        OfficialLivePostingCandidateFailurePlan failure = new(
            NumberNotConsumedOnPreAllocateFailure: true,
            RetryOutputUsesSameNumber: true,
            CrashLeavesNoOrphanAllocatedNumber: true,
            Summary:
                "Failure plan requires no orphan allocated numbers and output retry without re-allocation. " +
                "Not executed in this candidate.");

        OfficialLivePostingCandidateOutputPlan output = new(
            OutputWillGenerate: false,
            CrystalSelected: false,
            PdfSelected: false,
            PrinterSelected: false,
            WatermarkOnlyInternalPath: true,
            Summary:
                "Output package is planned as shape only. Official output engine is missing. " +
                OfficialSafetyConstants.InternalWatermark);

        OfficialLivePostingCandidateTransmissionPlan transmission = new(
            Track: track,
            EisWillTransmit: false,
            TraditionalESalesWillPush: false,
            MonthlyESalesExportPlanned: track == OfficialLivePostingCandidateTrack.TraditionalCrmPos,
            EisJsonSchemaResolved: EisJsonSchemaResolved,
            Summary: track == OfficialLivePostingCandidateTrack.EisInvoiceReceipt
                ? "EIS track: JSON push planned as blocked candidate only; schema unresolved; no transmit."
                : "Traditional track: monthly eSales + examinable journal/Z planned as blocked candidate only; no live push.");

        IReadOnlyList<OfficialLivePostingCandidateStageStatus> stages = BuildStages(track, numberingPreviewHint);
        IReadOnlyList<OfficialLivePostingCandidateInvariant> invariants = BuildInvariants();

        return new OfficialLivePostingCandidateResult(
            ContractVersion: OfficialContractVersions.M3,
            CandidateOnly: true,
            DisabledByDefault: true,
            DryRunOnly: true,
            NonOfficial: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            CheckoutBound: false,
            RemotePosAllowed: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            EisWillTransmit: false,
            OfficialReceiptIssued: false,
            ComplianceClaimed: false,
            ProductionActivation: false,
            Blocked: true,
            SelectedTrack: track,
            TerminalStage: OfficialLivePostingCandidateStage.BlockBeforeMutation,
            SourceSaleId: request?.OperationalSaleId,
            DocumentKind: documentKind,
            NumberingPreviewHint: numberingPreviewHint,
            AtomicPlan: atomic,
            FailurePlan: failure,
            OutputPlan: output,
            TransmissionPlan: transmission,
            Flags: flags,
            Stages: stages,
            Invariants: invariants,
            Blockers: blockers,
            Warnings: warnings,
            Summary:
                "Live official posting candidate remains blocked before mutation. " +
                "Track=" + track + ". " +
                OfficialSafetyConstants.LivePostingBlocked + " " +
                OfficialSafetyConstants.NoOfficialNumberIssued + " " +
                "Checkout is not connected. EIS JSON schema unresolved for EIS transmit.");
    }

    private static OfficialLivePostingCandidateTrack ResolveTrack(OfficialLivePostingCandidateRequest? request)
    {
        if (request?.PreferredTrack is OfficialLivePostingCandidateTrack preferred &&
            preferred != OfficialLivePostingCandidateTrack.Unknown)
        {
            return preferred;
        }

        if (request?.PreferEisTrack == true)
        {
            return OfficialLivePostingCandidateTrack.EisInvoiceReceipt;
        }

        return OfficialLivePostingCandidateTrack.TraditionalCrmPos;
    }

    private static List<OfficialLivePostingCandidateBlocker> BuildCoreBlockers(
        OfficialLivePostingCandidateTrack track)
    {
        List<OfficialLivePostingCandidateBlocker> blockers = new()
        {
            new(
                "HostOfficialLivePostingCandidate.DisabledByDefault",
                "Live official posting candidate is disabled by default."),
            new(
                "HostOfficialLivePostingCandidate.LiveCheckoutBindingNotImplemented",
                "Live checkout binding is not implemented; official posting is not wired to checkout."),
            new(
                "HostOfficialLivePostingCandidate.LiveOfficialPostingDisabled",
                "Live official posting is disabled."),
            new(
                "HostOfficialLivePostingCandidate.OutputEngineMissing",
                "Official output engine is missing; no receipt/invoice legal output."),
            new(
                "HostOfficialLivePostingCandidate.ESalesLivePushNotImplemented",
                "eSales live push is not implemented."),
            new(
                "HostOfficialLivePostingCandidate.OperationalizationIncomplete",
                "Annex F / retention / eSales process text is source-acquired but not fully operationalized in product."),
            new(
                "HostOfficialLivePostingCandidate.NoProductionActivation",
                "Production activation is not available."),
            new(
                "HostOfficialLivePostingCandidate.BlockBeforeMutation",
                "Candidate terminal stage is BlockBeforeMutation; sequence, sales, and EJ will not mutate.")
        };

        if (track != OfficialLivePostingCandidateTrack.EisInvoiceReceipt)
        {
            blockers.Add(new OfficialLivePostingCandidateBlocker(
                "HostOfficialLivePostingCandidate.EisJsonSchemaUnresolved",
                "EIS JSON API field schema remains unresolved (recorded for dual-track awareness; traditional track selected)."));
        }

        return blockers;
    }

    private static IReadOnlyList<OfficialLivePostingCandidateStageStatus> BuildStages(
        OfficialLivePostingCandidateTrack track,
        string? numberingPreviewHint)
    {
        string numberSummary = string.IsNullOrWhiteSpace(numberingPreviewHint)
            ? "Official number preview unavailable or blocked; sequence will not mutate."
            : "Official number preview hint: " + numberingPreviewHint + "; sequence will not mutate.";

        List<OfficialLivePostingCandidateStageStatus> stages = new()
        {
            new(OfficialLivePostingCandidateStage.ReadSourceSaleSnapshot, Planned: true, MutatesState: false,
                "Read operational sale snapshot if provided (read-only)."),
            new(OfficialLivePostingCandidateStage.ValidateOfficialSetup, Planned: true, MutatesState: false,
                "Validate official business/machine/receipt setup."),
            new(OfficialLivePostingCandidateStage.ValidateDocumentSeries, Planned: true, MutatesState: false,
                "Validate active official document series."),
            new(OfficialLivePostingCandidateStage.PreviewOfficialNumber, Planned: true, MutatesState: false,
                numberSummary),
            new(OfficialLivePostingCandidateStage.PlanOfficialNumberAllocation, Planned: true, MutatesState: false,
                "Plan atomic official number allocation (not executed)."),
            new(OfficialLivePostingCandidateStage.PlanImmutableOfficialSaleRecord, Planned: true, MutatesState: false,
                "Plan immutable official sale record linked to operational sale (not executed)."),
            new(OfficialLivePostingCandidateStage.PlanElectronicJournalAppend, Planned: true, MutatesState: false,
                "Plan EJ append in same transaction (not executed; ejWillAppendLive false)."),
            new(OfficialLivePostingCandidateStage.PlanAuditLink, Planned: true, MutatesState: false,
                "Plan audit link for who/when/terminal (not executed)."),
            new(OfficialLivePostingCandidateStage.PlanOutputPackage, Planned: true, MutatesState: false,
                "Plan output package shape only; outputWillGenerate false."),
            new(
                track == OfficialLivePostingCandidateTrack.EisInvoiceReceipt
                    ? OfficialLivePostingCandidateStage.PlanEisTransmission
                    : OfficialLivePostingCandidateStage.PlanTraditionalESales,
                Planned: true,
                MutatesState: false,
                track == OfficialLivePostingCandidateTrack.EisInvoiceReceipt
                    ? "Plan EIS JSON transmission (blocked; schema unresolved; eisWillTransmit false)."
                    : "Plan traditional monthly eSales + journal/Z examiner path (blocked; no live push)."),
            new(OfficialLivePostingCandidateStage.BlockBeforeMutation, Planned: true, MutatesState: false,
                "Terminal stage: block before any mutation.")
        };

        return stages;
    }

    private static IReadOnlyList<OfficialLivePostingCandidateInvariant> BuildInvariants() =>
        new OfficialLivePostingCandidateInvariant[]
        {
            new("NoReuseAfterCommit", "Official number never reused after committed allocation.", SatisfiedInPlan: true),
            new("NoDraftAllocation", "No draft/cart allocation of official numbers.", SatisfiedInPlan: true),
            new("NoRemoteAllocation", "No remote client / Admin remote official posting.", SatisfiedInPlan: true),
            new("ImmutablePostedSale", "Posted official sale immutable; correction by reversal only.", SatisfiedInPlan: true),
            new("EjTransactionalLink", "EJ append transactionally linked to official post.", SatisfiedInPlan: true),
            new("OutputFailureNoReallocate", "Output generation failure does not allocate a new number.", SatisfiedInPlan: true),
            new("ReprintSameNumber", "Reprint uses same official number.", SatisfiedInPlan: true),
            new("VoidRequiresReasonAuth", "Void/refund requires reason and authorization; distinct series.", SatisfiedInPlan: true),
            new("CandidateDoesNotMutate", "This candidate plan does not mutate sequence/sales/EJ.", SatisfiedInPlan: true)
        };
}

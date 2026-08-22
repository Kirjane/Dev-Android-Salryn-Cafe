namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M11 guarded live official posting candidate contracts.
/// Dual-track planning only. Always blocked before mutation. Not production activation. Not checkout-bound.
/// </summary>
public enum OfficialLivePostingCandidateTrack
{
    TraditionalCrmPos = 1,
    EisInvoiceReceipt = 2,
    Unknown = 3
}

public enum OfficialLivePostingCandidateStage
{
    ReadSourceSaleSnapshot = 1,
    ValidateOfficialSetup = 2,
    ValidateDocumentSeries = 3,
    PreviewOfficialNumber = 4,
    PlanOfficialNumberAllocation = 5,
    PlanImmutableOfficialSaleRecord = 6,
    PlanElectronicJournalAppend = 7,
    PlanAuditLink = 8,
    PlanOutputPackage = 9,
    PlanTraditionalESales = 10,
    PlanEisTransmission = 11,
    BlockBeforeMutation = 12
}

public sealed record OfficialLivePostingCandidateRequest(
    Guid? OperationalSaleId,
    string? DocumentKind,
    bool ExplicitInternalActivationRequested,
    OfficialLivePostingCandidateTrack? PreferredTrack = null,
    bool PreferEisTrack = false);

public sealed record OfficialLivePostingCandidateBlocker(
    string Code,
    string Message);

public sealed record OfficialLivePostingCandidateWarning(
    string Code,
    string Message);

public sealed record OfficialLivePostingCandidateInvariant(
    string Code,
    string Description,
    bool SatisfiedInPlan);

public sealed record OfficialLivePostingCandidateAtomicPlan(
    bool SameTransactionForNumberSaleAndEj,
    bool AllocateOnlyOnCommit,
    bool NoDraftCartAllocation,
    bool NoRemoteClientAllocation,
    string Summary);

public sealed record OfficialLivePostingCandidateFailurePlan(
    bool NumberNotConsumedOnPreAllocateFailure,
    bool RetryOutputUsesSameNumber,
    bool CrashLeavesNoOrphanAllocatedNumber,
    string Summary);

public sealed record OfficialLivePostingCandidateOutputPlan(
    bool OutputWillGenerate,
    bool CrystalSelected,
    bool PdfSelected,
    bool PrinterSelected,
    bool WatermarkOnlyInternalPath,
    string Summary);

public sealed record OfficialLivePostingCandidateTransmissionPlan(
    OfficialLivePostingCandidateTrack Track,
    bool EisWillTransmit,
    bool TraditionalESalesWillPush,
    bool MonthlyESalesExportPlanned,
    bool EisJsonSchemaResolved,
    string Summary);

public sealed record OfficialLivePostingCandidateStageStatus(
    OfficialLivePostingCandidateStage Stage,
    bool Planned,
    bool MutatesState,
    string Summary);

/// <summary>Forced safety flags for live-posting candidate responses.</summary>
public sealed record OfficialLivePostingCandidateFlags(
    bool CandidateOnly,
    bool DisabledByDefault,
    bool DryRunOnly,
    bool NonOfficial,
    bool LiveOfficialPostingAllowed,
    bool CheckoutBound,
    bool RemotePosAllowed,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool EisWillTransmit,
    bool OfficialReceiptIssued,
    bool ComplianceClaimed,
    bool ProductionActivation);

public sealed record OfficialLivePostingCandidateResult(
    int ContractVersion,
    bool CandidateOnly,
    bool DisabledByDefault,
    bool DryRunOnly,
    bool NonOfficial,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool CheckoutBound,
    bool RemotePosAllowed,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool EisWillTransmit,
    bool OfficialReceiptIssued,
    bool ComplianceClaimed,
    bool ProductionActivation,
    bool Blocked,
    OfficialLivePostingCandidateTrack SelectedTrack,
    OfficialLivePostingCandidateStage TerminalStage,
    Guid? SourceSaleId,
    string? DocumentKind,
    string? NumberingPreviewHint,
    OfficialLivePostingCandidateAtomicPlan AtomicPlan,
    OfficialLivePostingCandidateFailurePlan FailurePlan,
    OfficialLivePostingCandidateOutputPlan OutputPlan,
    OfficialLivePostingCandidateTransmissionPlan TransmissionPlan,
    OfficialLivePostingCandidateFlags Flags,
    IReadOnlyList<OfficialLivePostingCandidateStageStatus> Stages,
    IReadOnlyList<OfficialLivePostingCandidateInvariant> Invariants,
    IReadOnlyList<OfficialLivePostingCandidateBlocker> Blockers,
    IReadOnlyList<OfficialLivePostingCandidateWarning> Warnings,
    string Summary);

/// <summary>Host-local status surface for candidate wiring (no activation).</summary>
public sealed record OfficialLivePostingCandidateStatusContract(
    int ContractVersion,
    bool CandidateServicePresent,
    bool CandidateOnly,
    bool DisabledByDefault,
    bool LiveOfficialPostingAllowed,
    bool CheckoutBound,
    bool Blocked,
    bool EisJsonSchemaResolved,
    OfficialLivePostingCandidateTrack DefaultTrack,
    string Summary);

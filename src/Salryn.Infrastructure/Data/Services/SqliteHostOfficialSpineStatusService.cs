using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M7 aggregated spine status. Composes setup/numbering/integrity probes.
/// No mutation. No live official posting.
/// </summary>
public sealed class SqliteHostOfficialSpineStatusService : IHostOfficialSpineStatusService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHostOfficialSpineStatusService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<OfficialSpineStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(_connectionFactory);
            IHostOfficialNumberingService numbering = new SqliteHostOfficialNumberingService(_connectionFactory);
            IHostOfficialIntegrityService integrity = new SqliteHostOfficialIntegrityService(_connectionFactory);

            Result<OfficialSetupSnapshotContract> setupResult =
                await setup.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            Result<OfficialNumberingPreviewContract> numberingResult =
                await numbering.GetPreviewAsync(cancellationToken).ConfigureAwait(false);
            Result<OfficialIntegrityStatusContract> integrityResult =
                await integrity.GetStatusAsync(cancellationToken).ConfigureAwait(false);

            List<OfficialSpineComponentStatusContract> components = new();
            List<OfficialSpineBlocker> blockers = new();
            List<OfficialSpineWarning> warnings = new();

            bool setupPresent = setupResult.IsSuccess &&
                (setupResult.Value.Business is not null || setupResult.Value.Machine is not null);
            bool seriesPresent = setupResult.IsSuccess && setupResult.Value.Series is not null;
            bool numberingPreviewReady = numberingResult.IsSuccess &&
                numberingResult.Value.Blockers.Count == 0 &&
                !string.IsNullOrWhiteSpace(numberingResult.Value.FormattedNextNumber);
            bool integrityPresent = integrityResult.IsSuccess;
            bool ejSkeleton = integrityResult.IsSuccess && integrityResult.Value.Journal.SkeletonPresent;
            bool voidSkeleton = integrityResult.IsSuccess && integrityResult.Value.VoidRefund.SkeletonPresent;

            components.Add(Component(
                OfficialSpineComponentName.SetupProfile,
                setupPresent ? OfficialSpineReadinessLevel.SkeletonPresent : OfficialSpineReadinessLevel.Missing,
                setupPresent,
                liveBound: false,
                setupPresent
                    ? "Business/machine setup data is present for internal dry-run configuration only."
                    : "Official setup profile is missing."));

            components.Add(Component(
                OfficialSpineComponentName.DocumentSeries,
                seriesPresent ? OfficialSpineReadinessLevel.SkeletonPresent : OfficialSpineReadinessLevel.Missing,
                seriesPresent,
                liveBound: false,
                seriesPresent
                    ? "Document series setup is present; current = start-1 setup semantics; not checkout allocation."
                    : "Document series setup is missing."));

            components.Add(Component(
                OfficialSpineComponentName.NumberingPreview,
                numberingPreviewReady
                    ? OfficialSpineReadinessLevel.PreviewReady
                    : (numberingResult.IsSuccess
                        ? OfficialSpineReadinessLevel.SkeletonPresent
                        : OfficialSpineReadinessLevel.Missing),
                numberingResult.IsSuccess,
                liveBound: false,
                numberingPreviewReady
                    ? "Official numbering preview is available. Sequence is not mutated. Checkout is not connected."
                    : "Official numbering preview is not ready (series incomplete or blocked)."));

            components.Add(Component(
                OfficialSpineComponentName.IntegritySkeleton,
                integrityPresent ? OfficialSpineReadinessLevel.SkeletonPresent : OfficialSpineReadinessLevel.Missing,
                integrityPresent,
                liveBound: false,
                "Integrity skeleton status is available for engineering visibility only."));

            components.Add(Component(
                OfficialSpineComponentName.ElectronicJournalSkeleton,
                ejSkeleton ? OfficialSpineReadinessLevel.SkeletonPresent : OfficialSpineReadinessLevel.Missing,
                ejSkeleton,
                liveBound: false,
                ejSkeleton
                    ? "Electronic journal schema/skeleton is present but not live-bound."
                    : "Electronic journal skeleton is missing."));

            components.Add(Component(
                OfficialSpineComponentName.VoidRefundSkeleton,
                voidSkeleton ? OfficialSpineReadinessLevel.SkeletonPresent : OfficialSpineReadinessLevel.Missing,
                voidSkeleton,
                liveBound: false,
                "Void/refund integrity skeleton is present but not live-integrated."));

            components.Add(Component(
                OfficialSpineComponentName.DryRunPlanner,
                OfficialSpineReadinessLevel.DryRunReady,
                present: true,
                liveBound: false,
                "Official sale dry-run planner is available; not live-bound and does not post."));

            components.Add(Component(
                OfficialSpineComponentName.DryRunPersistence,
                OfficialSpineReadinessLevel.DryRunReady,
                present: true,
                liveBound: false,
                "Internal dry-run package persistence is available (engineering only)."));

            components.Add(Component(
                OfficialSpineComponentName.ReportPreviewPackage,
                OfficialSpineReadinessLevel.PreviewReady,
                present: true,
                liveBound: false,
                "Internal report preview package is available; watermarked; not legal output."));

            components.Add(Component(
                OfficialSpineComponentName.ReportDataContract,
                OfficialSpineReadinessLevel.SkeletonPresent,
                present: true,
                liveBound: false,
                "Report data handoff contracts are defined; no report engine selected."));

            components.Add(Component(
                OfficialSpineComponentName.OfficialOutputEngine,
                OfficialSpineReadinessLevel.Missing,
                present: false,
                liveBound: false,
                "Official receipt/invoice output engine (text/PDF/print/Crystal) is not implemented."));

            components.Add(Component(
                OfficialSpineComponentName.LiveCheckoutBinding,
                OfficialSpineReadinessLevel.Missing,
                present: false,
                liveBound: false,
                "Live host checkout does not post official documents."));

            components.Add(Component(
                OfficialSpineComponentName.LegalSourceClosure,
                OfficialSpineReadinessLevel.PreviewReady,
                present: true,
                liveBound: false,
                "Seal/push/eSales process source text acquired (dual-track). EIS JSON API schema and product live paths remain incomplete; live issuance blocked."));

            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.LiveOfficialCheckoutNotImplemented",
                "Live official checkout is not implemented."));
            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.OfficialOutputEngineMissing",
                "Official output engine is missing."));
            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.EjLiveBindMissing",
                "Electronic journal is not live-bound to official sale posting."));
            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.VoidRefundLiveIncomplete",
                "Void/refund live official integration is incomplete."));
            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.AnnexFUnresolved",
                "Annex F product demo readiness incomplete; EIS API field schema still open; live issuance blocked."));
            blockers.Add(new OfficialSpineBlocker(
                "HostOfficialSpine.LegalSourceClosureIncomplete",
                "Legal source process text improved; live official issuance remains blocked (product + EIS schema + accreditation)."));

            warnings.Add(new OfficialSpineWarning(
                "HostOfficialSpine.InternalDryRunOnly",
                "Spine status is internal dry-run engineering visibility only."));
            warnings.Add(new OfficialSpineWarning(
                "HostOfficialSpine.NoComplianceClaim",
                "No BIR/RMO compliance is claimed."));
            warnings.Add(new OfficialSpineWarning(
                "HostOfficialSpine.CheckoutDisconnected",
                "Checkout is not connected to official numbering, journal, or dry-run planner."));

            return Result<OfficialSpineStatusContract>.Success(new OfficialSpineStatusContract(
                ContractVersion: OfficialContractVersions.M3,
                LiveOfficialPostingAllowed: false,
                OfficialCheckoutAllocationEnabled: false,
                CheckoutUsesAllocator: false,
                OfficialOutputImplemented: false,
                ComplianceClaimed: false,
                DryRunOnly: true,
                NonOfficial: true,
                Components: components,
                Blockers: blockers,
                Warnings: warnings,
                Summary:
                    "RMO pre-live spine status: setup/numbering/integrity/dry-run planner/report contracts available for engineering only. " +
                    "Live official checkout and official output remain not implemented."));
        }
        catch (Exception ex)
        {
            return Result<OfficialSpineStatusContract>.Fail(Error.Unexpected(
                "HostOfficialSpine.StatusFailed",
                "Failed to load official spine status: " + ex.Message));
        }
    }

    private static OfficialSpineComponentStatusContract Component(
        OfficialSpineComponentName name,
        OfficialSpineReadinessLevel level,
        bool present,
        bool liveBound,
        string summary) =>
        new(name, level, present, liveBound, summary);
}

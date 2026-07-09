using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official readiness evaluator (RMO-BIR-M3).
/// Does not advance document series, post sales, or claim accreditation.
/// </summary>
public interface IHostOfficialReadinessService
{
    Task<Result<HostOfficialReadinessSnapshot>> GetReadinessAsync(
        CancellationToken cancellationToken = default);
}

public sealed class HostOfficialReadinessService : IHostOfficialReadinessService
{
    private readonly IOfficialCheckoutReadinessService? _configReadiness;

    public HostOfficialReadinessService(IOfficialCheckoutReadinessService? configReadiness = null)
    {
        _configReadiness = configReadiness;
    }

    public async Task<Result<HostOfficialReadinessSnapshot>> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<HostOfficialReadinessItem> blockers = new();
        List<HostOfficialReadinessItem> warnings = new();

        // Product spine always incomplete in M3 — never allow live official posting.
        AddProductSpineBlockers(blockers);
        AddProductSpineWarnings(warnings);

        OfficialBusinessProfileSnapshot? business = null;
        OfficialMachineProfileSnapshot? machine = null;
        OfficialDocumentSeriesContract? seriesHint = null;
        string complianceMode = "Unknown";
        bool officialOutputEnabledInConfig = false;
        bool configReadyForDryRun = false;

        if (_configReadiness is null)
        {
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.ConfigServiceUnavailable",
                "Official configuration readiness service is not wired."));
        }
        else
        {
            Result<OfficialCheckoutReadinessResult> configResult = await _configReadiness
                .CheckActiveScopeAsync(
                    new OfficialCheckoutActiveScopeReadinessRequest(
                        RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                        DocumentKind: "OFFICIAL_INVOICE",
                        SequenceScopeCode: "LOCAL_SINGLE_STORE",
                        Year: DateTime.UtcNow.Year),
                    cancellationToken)
                .ConfigureAwait(false);

            if (configResult.IsFailure)
            {
                blockers.Add(new HostOfficialReadinessItem(
                    "HostOfficialReadiness.ConfigCheckFailed",
                    configResult.ErrorDetail.Message));
            }
            else
            {
                OfficialCheckoutReadinessResult config = configResult.Value;
                foreach (OfficialCheckoutReadinessBlock block in config.Blocks)
                {
                    blockers.Add(new HostOfficialReadinessItem(block.Code, block.Message));
                }

                bool hasNotAccredited = config.Blocks.Any(b =>
                    b.Code.Contains("NotAccredited", StringComparison.OrdinalIgnoreCase));
                bool hasOutputDisabled = config.Blocks.Any(b =>
                    b.Code.Contains("OfficialOutputDisabled", StringComparison.OrdinalIgnoreCase));
                bool hasProfileMissing = config.Blocks.Any(b =>
                    b.Code.Contains("MachineProfileMissing", StringComparison.OrdinalIgnoreCase) ||
                    b.Code.Contains("ActiveMachineProfileMissing", StringComparison.OrdinalIgnoreCase));

                if (hasNotAccredited)
                {
                    complianceMode = "NOT_ACCREDITED";
                }
                else if (config.MachineProfile is not null && !hasProfileMissing)
                {
                    // Config service only returns ACCREDITED path when blocks empty of not-accredited.
                    complianceMode = hasOutputDisabled ? "ACCREDITED" : "ACCREDITED";
                }

                officialOutputEnabledInConfig = config.MachineProfile is not null &&
                    !hasOutputDisabled &&
                    !hasProfileMissing &&
                    !hasNotAccredited;

                if (config.MachineProfile is not null)
                {
                    OfficialDocumentSnapshotDraftMachineProfileInput mp = config.MachineProfile;
                    business = new OfficialBusinessProfileSnapshot(
                        RegisteredTaxpayerName: mp.RegisteredTaxpayerName,
                        BusinessName: mp.BusinessName,
                        RegisteredAddress: mp.RegisteredBusinessAddress,
                        BranchAddress: null,
                        TaxpayerIdentificationNumber: mp.TaxpayerIdentificationNumber,
                        BranchCode: mp.BranchCode,
                        VatRegistrationPhrase: null);
                    machine = new OfficialMachineProfileSnapshot(
                        BirMachineProfileId: config.BirMachineProfileId == Guid.Empty
                            ? null
                            : config.BirMachineProfileId,
                        MachineIdentificationNumber: mp.MachineIdentificationNumber,
                        MachineSerialNumber: mp.MachineSerialNumber ?? string.Empty,
                        PosTerminalNumber: null,
                        AccreditationNumber: mp.AccreditationNumber,
                        PermitToUseNumber: mp.PermitToUseNumber,
                        SoftwareName: "Salryn",
                        SoftwareVersion: "UNSET",
                        SoftwareBuild: null,
                        ComplianceMode: complianceMode,
                        OfficialOutputEnabledInConfig: officialOutputEnabledInConfig,
                        IsActive: !hasProfileMissing);
                }

                if (config.ActiveSequenceId is Guid sequenceId)
                {
                    seriesHint = new OfficialDocumentSeriesContract(
                        ContractVersion: OfficialContractVersions.M3,
                        SequenceId: sequenceId,
                        BirMachineProfileId: config.BirMachineProfileId == Guid.Empty
                            ? null
                            : config.BirMachineProfileId,
                        DocumentKind: OfficialDocumentKind.OfficialInvoice,
                        SequenceScopeCode: config.SequenceScopeCode,
                        Prefix: "(active sequence)",
                        Year: config.Year,
                        StartNumber: 0,
                        CurrentNumber: 0,
                        EndNumber: null,
                        PaddingDigits: 6,
                        IsActive: true,
                        IsExhausted: false);
                }

                // Config dry-run: profile+sequence ok per OfficialCheckoutReadinessService (IsReady).
                // Product spine blockers remain; live posting still forbidden.
                configReadyForDryRun = config.IsReady;
                if (config.IsReady)
                {
                    complianceMode = "ACCREDITED";
                    officialOutputEnabledInConfig = true;
                }
            }
        }

        HostOfficialReadinessStatus status = ResolveStatus(
            configReadyForDryRun,
            officialOutputEnabledInConfig,
            complianceMode);

        string summary = status switch
        {
            HostOfficialReadinessStatus.Disabled =>
                "Official output is disabled or not configured. View readiness only. No official posting.",
            HostOfficialReadinessStatus.ReadyForInternalDryRun =>
                "Configuration appears complete for internal engineering dry-run only. Live official posting is not implemented and is not allowed.",
            HostOfficialReadinessStatus.NotReady =>
                "Official output is not ready. See blockers. No official posting.",
            _ =>
                "Official readiness is unknown. No official posting."
        };

        HostOfficialReadinessSnapshot snapshot = new(
            ContractVersion: OfficialContractVersions.M3,
            Status: status,
            OfficialOutputEnabledInConfig: officialOutputEnabledInConfig,
            LiveOfficialPostingAllowed: false,
            ComplianceMode: complianceMode,
            Blockers: blockers,
            Warnings: warnings,
            AllowedActions: new[] { "ViewReadiness" },
            BusinessProfile: business,
            MachineProfile: machine,
            ActiveSeriesHint: seriesHint,
            Summary: summary);

        return Result<HostOfficialReadinessSnapshot>.Success(snapshot);
    }

    private static void AddProductSpineBlockers(List<HostOfficialReadinessItem> blockers)
    {
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.LiveOfficialCheckoutNotImplemented",
            "Live host checkout does not post official documents or advance official document series."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.OfficialOutputEngineNotImplemented",
            "No official receipt/invoice output engine (text/PDF/print/Crystal) is implemented."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.LiveElectronicJournalBindingMissing",
            "Electronic journal append is not bound to live official sale posting."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.VoidRefundIntegrityPathIncomplete",
            "Official void/refund/cancel integrity path is not productized."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.UnresolvedAnnexFRetentionESales",
            "Annex F push/seal details, retention years, and eSales transmission remain unresolved."));
    }

    private static void AddProductSpineWarnings(List<HostOfficialReadinessItem> warnings)
    {
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.CrystalNotSelected",
            "Crystal Reports is not selected; report engine remains deferred."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.ComplianceNotClaimed",
            "Salryn does not claim BIR/RMO compliance, accreditation, or official receipt readiness."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.OperationalNumbersRemainNonOfficial",
            "Host operational POS numbers remain non-official and are distinct from official document numbers."));
    }

    private static HostOfficialReadinessStatus ResolveStatus(
        bool configReadyForDryRun,
        bool officialOutputEnabledInConfig,
        string complianceMode)
    {
        if (configReadyForDryRun)
        {
            return HostOfficialReadinessStatus.ReadyForInternalDryRun;
        }

        if (!officialOutputEnabledInConfig ||
            string.Equals(complianceMode, "NOT_ACCREDITED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(complianceMode, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return HostOfficialReadinessStatus.Disabled;
        }

        return HostOfficialReadinessStatus.NotReady;
    }
}

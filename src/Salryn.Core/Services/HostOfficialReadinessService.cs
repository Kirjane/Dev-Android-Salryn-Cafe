using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official readiness evaluator (RMO-BIR-M3/M4).
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
    private readonly IHostOfficialSetupService? _setupService;

    public HostOfficialReadinessService(
        IOfficialCheckoutReadinessService? configReadiness = null,
        IHostOfficialSetupService? setupService = null)
    {
        _configReadiness = configReadiness;
        _setupService = setupService;
    }

    public async Task<Result<HostOfficialReadinessSnapshot>> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<HostOfficialReadinessItem> blockers = new();
        List<HostOfficialReadinessItem> warnings = new();

        AddProductSpineBlockers(blockers);
        AddProductSpineWarnings(warnings);

        OfficialBusinessProfileSnapshot? business = null;
        OfficialMachineProfileSnapshot? machine = null;
        OfficialDocumentSeriesContract? seriesHint = null;
        string complianceMode = "Unknown";
        bool officialOutputEnabledInConfig = false;
        bool setupConfiguredForDryRun = false;
        bool setupPresent = false;

        if (_setupService is not null)
        {
            Result<OfficialSetupSnapshotContract> setupResult =
                await _setupService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (setupResult.IsSuccess)
            {
                OfficialSetupSnapshotContract setup = setupResult.Value;
                setupPresent = setup.Business is not null || setup.Machine is not null;
                EvaluateSetupCompleteness(setup, blockers, out setupConfiguredForDryRun);

                if (setup.Business is not null)
                {
                    business = new OfficialBusinessProfileSnapshot(
                        setup.Business.RegisteredTaxpayerName,
                        setup.Business.BusinessName,
                        setup.Business.RegisteredAddress,
                        setup.Business.BranchAddress,
                        setup.Business.TaxpayerIdentificationNumber,
                        setup.Business.BranchCode,
                        setup.Business.VatRegistrationMode);
                }

                if (setup.Machine is not null)
                {
                    complianceMode = setup.Machine.ComplianceMode;
                    officialOutputEnabledInConfig = setup.Machine.OfficialOutputEnabledInConfig;
                    machine = new OfficialMachineProfileSnapshot(
                        setup.Machine.ProfileId,
                        setup.Machine.MachineIdentificationNumber,
                        setup.Machine.MachineSerialNumber,
                        setup.Machine.PosTerminalNumber,
                        setup.Machine.AccreditationNumber,
                        setup.Machine.PermitToUseNumber,
                        "Salryn",
                        setup.Machine.SoftwareVersion ?? "UNSET",
                        setup.Machine.SoftwareBuild,
                        setup.Machine.ComplianceMode,
                        setup.Machine.OfficialOutputEnabledInConfig,
                        setup.Machine.IsActive);
                }

                if (setup.Series is not null)
                {
                    seriesHint = new OfficialDocumentSeriesContract(
                        ContractVersion: OfficialContractVersions.M3,
                        SequenceId: setup.Series.SeriesId,
                        BirMachineProfileId: setup.Series.BirMachineProfileId,
                        DocumentKind: string.Equals(
                            setup.Series.DocumentKind,
                            "OFFICIAL_RECEIPT",
                            StringComparison.OrdinalIgnoreCase)
                            ? OfficialDocumentKind.OfficialReceipt
                            : OfficialDocumentKind.OfficialInvoice,
                        SequenceScopeCode: "LOCAL_SINGLE_STORE",
                        Prefix: setup.Series.Prefix,
                        Year: setup.Series.Year,
                        StartNumber: setup.Series.StartNumber,
                        CurrentNumber: setup.Series.CurrentNumber,
                        EndNumber: setup.Series.EndNumber,
                        PaddingDigits: setup.Series.PaddingDigits,
                        IsActive: setup.Series.IsActive,
                        IsExhausted: setup.Series.IsExhausted);
                }
            }
            else
            {
                blockers.Add(new HostOfficialReadinessItem(
                    "HostOfficialReadiness.SetupLoadFailed",
                    setupResult.ErrorDetail.Message));
            }
        }
        else if (_configReadiness is not null)
        {
            // M3 fallback path when setup service not wired.
            Result<OfficialCheckoutReadinessResult> configResult = await _configReadiness
                .CheckActiveScopeAsync(
                    new OfficialCheckoutActiveScopeReadinessRequest(
                        RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                        DocumentKind: "OFFICIAL_INVOICE",
                        SequenceScopeCode: "LOCAL_SINGLE_STORE",
                        Year: DateTime.UtcNow.Year),
                    cancellationToken)
                .ConfigureAwait(false);

            if (configResult.IsSuccess)
            {
                foreach (OfficialCheckoutReadinessBlock block in configResult.Value.Blocks)
                {
                    blockers.Add(new HostOfficialReadinessItem(block.Code, block.Message));
                }

                setupConfiguredForDryRun = configResult.Value.IsReady;
            }
        }
        else
        {
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.ConfigServiceUnavailable",
                "Official setup/readiness services are not fully wired."));
        }

        HostOfficialReadinessStatus status = ResolveStatus(
            setupConfiguredForDryRun,
            setupPresent,
            officialOutputEnabledInConfig,
            complianceMode);

        string summary = status switch
        {
            HostOfficialReadinessStatus.Disabled =>
                "Official output is disabled or not configured. View readiness only. No official posting.",
            HostOfficialReadinessStatus.ConfiguredForInternalDryRun or
                HostOfficialReadinessStatus.ReadyForInternalDryRun =>
                "Setup appears complete for internal engineering dry-run only. Live official posting is not implemented and is not allowed.",
            HostOfficialReadinessStatus.SetupIncomplete =>
                "Official setup is incomplete. See blockers. No official posting.",
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
            AllowedActions: new[] { "ViewReadiness", "EditOfficialSetup" },
            BusinessProfile: business,
            MachineProfile: machine,
            ActiveSeriesHint: seriesHint,
            Summary: summary);

        return Result<HostOfficialReadinessSnapshot>.Success(snapshot);
    }

    private static void EvaluateSetupCompleteness(
        OfficialSetupSnapshotContract setup,
        List<HostOfficialReadinessItem> blockers,
        out bool configuredForDryRun)
    {
        configuredForDryRun = false;
        bool businessOk = setup.Business is not null
            && !IsUnset(setup.Business.RegisteredTaxpayerName)
            && !IsUnset(setup.Business.RegisteredAddress)
            && !IsUnset(setup.Business.TaxpayerIdentificationNumber)
            && !IsUnset(setup.Business.BranchCode);

        bool machineOk = setup.Machine is not null
            && !IsUnset(setup.Machine.MachineSerialNumber);

        bool seriesOk = setup.Series is not null
            && !string.IsNullOrWhiteSpace(setup.Series.Prefix)
            && setup.Series.StartNumber > 0
            && setup.Series.PaddingDigits >= 6
            && setup.Series.IsActive
            && !setup.Series.IsExhausted;

        if (!businessOk)
        {
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.BusinessSetupIncomplete",
                "Business/taxpayer setup is incomplete (name, address, TIN, branch required)."));
        }

        if (!machineOk)
        {
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.MachineSetupIncomplete",
                "Machine/POS setup is incomplete (serial required)."));
        }

        if (!seriesOk)
        {
            string seriesMessage = setup.Series is null
                ? "Document series setup is incomplete (prefix, start, padding >= 6)."
                : setup.Series.IsExhausted
                    ? "Document series is exhausted or inactive; configure a valid series for internal dry-run setup."
                    : "Document series setup is incomplete (prefix, start, padding >= 6, active, not exhausted).";
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.SeriesSetupIncomplete",
                seriesMessage));
        }

        if (setup.Receipt is null)
        {
            blockers.Add(new HostOfficialReadinessItem(
                "HostOfficialReadiness.ReceiptProfileMissing",
                "Receipt profile setup is missing (title/legal markers for dry-run)."));
        }

        configuredForDryRun = businessOk && machineOk && seriesOk && setup.Receipt is not null
            && setup.Machine is not null
            && !setup.Machine.OfficialOutputEnabledInConfig
            && string.Equals(setup.Machine.ComplianceMode, "NOT_ACCREDITED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnset(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "UNSET", StringComparison.OrdinalIgnoreCase);

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
            "Electronic journal skeleton may be present, but append is not live-bound to official sale posting."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.VoidRefundIntegrityPathIncomplete",
            "Void/refund integrity skeleton may be present, but live official void/refund integration is incomplete."));
        blockers.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.UnresolvedAnnexFRetentionESales",
            "Annex F / seal / push / eSales process text is source-acquired (dual-track: traditional monthly eSales + EIS JSON when applicable); EIS API field schema and product live paths remain incomplete. Retention years source-closed (3/10). Live posting stays blocked."));
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
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.NumberingPreviewOnly",
            "Official numbering preview/validate exists for setup visibility only; checkout does not allocate official numbers."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.IntegritySkeletonStatusOnly",
            "Official integrity status reports EJ and void/refund skeleton state only; not live-bound and not compliance-ready."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.DryRunPlannerAvailableNotLiveBound",
            "Official sale dry-run planner is available for engineering package shape only; not live-bound and does not post."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.ReportDataContractsDefinedNoEngine",
            "Report data handoff contracts are defined; no report engine is selected and no official output is generated."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.InternalDryRunPackageAvailable",
            "Internal dry-run package persistence and watermarked report previews are available for engineering only."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.SourceSaleReadOnlyAvailable",
            "Operational sale read-only dry-run source mode is available; adapter does not mutate sales, sequences, or EJ."));
        warnings.Add(new HostOfficialReadinessItem(
            "HostOfficialReadiness.OfficialSourceAcquisitionM10",
            "Official source acquisition improved seal/push/eSales process definitions; live official issuance and EIS push remain not implemented."));
    }

    private static HostOfficialReadinessStatus ResolveStatus(
        bool setupConfiguredForDryRun,
        bool setupPresent,
        bool officialOutputEnabledInConfig,
        string complianceMode)
    {
        if (setupConfiguredForDryRun)
        {
            return HostOfficialReadinessStatus.ConfiguredForInternalDryRun;
        }

        if (setupPresent)
        {
            return HostOfficialReadinessStatus.SetupIncomplete;
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

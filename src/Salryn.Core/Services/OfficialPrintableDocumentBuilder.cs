using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public enum OfficialPrintableDocumentCopyMode
{
    Original = 1,
    Copy = 2,
    Reprint = 3
}

public sealed record OfficialPrintableDocument(
    OfficialPrintableDocumentHeader Header,
    OfficialPrintableMachineProfile MachineProfile,
    IReadOnlyList<OfficialPrintableLineItem> LineItems,
    IReadOnlyList<OfficialPrintablePayment> Payments,
    OfficialPrintableTotals Totals,
    OfficialPrintableEvidenceFooter Evidence,
    OfficialPrintableLifecycleNotice Lifecycle,
    OfficialPrintableCopyInfo CopyAudit);

public sealed record OfficialPrintableDocumentHeader(
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionTimestamp,
    Guid JournalEntryId,
    Guid SaleId,
    Guid CashierUserId,
    string CashierIdentifier,
    string? TerminalId);

public sealed record OfficialPrintableLineItem(
    string Description,
    string? Barcode,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record OfficialPrintablePayment(
    PaymentMethod Method,
    long AmountCents,
    DateTimeOffset PaidAt);

public sealed record OfficialPrintableTotals(
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    string TaxModeCode,
    bool VatBucketEngineDeferred,
    string? TaxModeNotes,
    IReadOnlyList<string> DeferredComplianceNotes);

public sealed record OfficialPrintableMachineProfile(
    Guid BirMachineProfileId,
    string? RegistrationScopeCode,
    string RegisteredTaxpayerName,
    string? BusinessName,
    string RegisteredAddress,
    string? BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string? MachineIdentificationNumber,
    string? MachineSerialNumber,
    string? PosTerminalNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string? SoftwareName,
    string? SoftwareVersion,
    string? SoftwareBuild,
    string? ComplianceMode);

public sealed record OfficialPrintableEvidenceFooter(
    Guid OriginalEventId,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool SnapshotHashMatches);

public sealed record OfficialPrintableLifecycleNotice(
    OfficialDocumentLifecycleState State,
    string NoticeCode,
    string NoticeText,
    int CorrectionCount,
    IReadOnlyList<string> CorrectionNotes,
    bool IsCoveredByFinalizedZReading,
    Guid? CoveringZReadingId,
    IReadOnlyList<OfficialDocumentLifecycleIssue> Issues);

public sealed record OfficialPrintableCopyInfo(
    OfficialPrintableDocumentCopyMode CopyMode,
    string OutputMarker,
    bool IsCopyOrReprint,
    int PriorReprintEventCount);

// Kirch: why - later report templates get deterministic DTOs so official-output rules stay in backend services.
public sealed class OfficialPrintableDocumentBuilder
{
    public Result<OfficialPrintableDocument> Build(
        OfficialOutputSourceResult? source,
        OfficialPrintableDocumentCopyMode copyMode = OfficialPrintableDocumentCopyMode.Original)
    {
        Result validation = ValidateSource(source, copyMode);

        if (validation.IsFailure)
        {
            return Result<OfficialPrintableDocument>.Fail(validation.ErrorDetail);
        }

        OfficialOutputSourceResult safeSource = source!;

        OfficialPrintableDocument document = new(
            Header: BuildHeader(safeSource),
            MachineProfile: BuildMachineProfile(safeSource.MachineProfile),
            LineItems: safeSource.LineItems.Select(BuildLineItem).ToArray(),
            Payments: safeSource.Payments.Select(BuildPayment).ToArray(),
            Totals: BuildTotals(safeSource.Totals),
            Evidence: BuildEvidence(safeSource.Evidence),
            Lifecycle: BuildLifecycle(safeSource.Lifecycle),
            CopyAudit: BuildCopyInfo(safeSource.Lifecycle, copyMode));

        return Result<OfficialPrintableDocument>.Success(document);
    }

    public static Result ValidateSource(
        OfficialOutputSourceResult? source,
        OfficialPrintableDocumentCopyMode copyMode)
    {
        if (source is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.SourceRequired",
                "Official printable document requires an output source result."));
        }

        if (!Enum.IsDefined(typeof(OfficialPrintableDocumentCopyMode), copyMode))
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.UnsupportedCopyMode",
                "Official printable document copy mode is unsupported."));
        }

        if (source.Header is null ||
            string.IsNullOrWhiteSpace(source.Header.OfficialDocumentNumber) ||
            source.Header.JournalEntryId == Guid.Empty ||
            source.Header.SaleId == Guid.Empty ||
            source.Header.BirMachineProfileId == Guid.Empty ||
            source.Header.CashierUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Header.CashierIdentifier))
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.HeaderRequired",
                "Official printable document requires complete header identity."));
        }

        if (source.MachineProfile is null ||
            source.MachineProfile.BirMachineProfileId == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.MachineProfile.RegisteredTaxpayerName) ||
            string.IsNullOrWhiteSpace(source.MachineProfile.RegisteredAddress) ||
            string.IsNullOrWhiteSpace(source.MachineProfile.TaxpayerIdentificationNumber) ||
            string.IsNullOrWhiteSpace(source.MachineProfile.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.MachineProfileRequired",
                "Official printable document requires persisted machine profile identity."));
        }

        if (source.LineItems is null || source.LineItems.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.LineItemsRequired",
                "Official printable document requires line items."));
        }

        if (source.Payments is null || source.Payments.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.PaymentsRequired",
                "Official printable document requires payments."));
        }

        if (source.Totals is null ||
            string.IsNullOrWhiteSpace(source.Totals.TaxModeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.TotalsRequired",
                "Official printable document requires totals and tax mode."));
        }

        if (source.Evidence is null ||
            source.Evidence.OriginalEventId == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Evidence.SnapshotPayloadHash) ||
            string.IsNullOrWhiteSpace(source.Evidence.SnapshotHashAlgorithm) ||
            !source.Evidence.SnapshotHashMatches)
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.VerifiedEvidenceRequired",
                "Official printable document requires verified snapshot evidence."));
        }

        if (source.Lifecycle is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialPrintableDocument.LifecycleRequired",
                "Official printable document requires lifecycle state."));
        }

        return Result.Success();
    }

    private static OfficialPrintableDocumentHeader BuildHeader(
        OfficialOutputSourceResult source)
    {
        return new OfficialPrintableDocumentHeader(
            DocumentKind: source.Header.DocumentKind,
            OfficialDocumentNumber: source.Header.OfficialDocumentNumber,
            TransactionTimestamp: source.Header.TransactionTimestamp,
            JournalEntryId: source.Header.JournalEntryId,
            SaleId: source.Header.SaleId,
            CashierUserId: source.Header.CashierUserId,
            CashierIdentifier: source.Header.CashierIdentifier,
            TerminalId: source.Header.TerminalId ?? source.Header.SnapshotTerminalIdentifier);
    }

    private static OfficialPrintableLineItem BuildLineItem(
        OfficialOutputLineItem lineItem)
    {
        return new OfficialPrintableLineItem(
            lineItem.Description,
            lineItem.Barcode,
            lineItem.UnitName,
            lineItem.QuantityX1000,
            lineItem.UnitPriceCents,
            lineItem.DiscountCents,
            lineItem.LineTotalCents);
    }

    private static OfficialPrintablePayment BuildPayment(
        OfficialOutputPayment payment)
    {
        return new OfficialPrintablePayment(
            payment.Method,
            payment.AmountCents,
            payment.PaidAt);
    }

    private static OfficialPrintableTotals BuildTotals(
        OfficialOutputTotals totals)
    {
        return new OfficialPrintableTotals(
            totals.SubtotalCents,
            totals.DiscountCents,
            totals.TaxCents,
            totals.TotalCents,
            totals.AmountPaidCents,
            totals.ChangeCents,
            totals.TaxModeCode,
            totals.VatBucketEngineDeferred,
            totals.TaxModeNotes,
            totals.DeferredComplianceNotes.ToArray());
    }

    private static OfficialPrintableMachineProfile BuildMachineProfile(
        OfficialOutputMachineProfile profile)
    {
        return new OfficialPrintableMachineProfile(
            profile.BirMachineProfileId,
            profile.RegistrationScopeCode,
            profile.RegisteredTaxpayerName,
            profile.BusinessName,
            profile.RegisteredAddress,
            profile.BranchAddress,
            profile.TaxpayerIdentificationNumber,
            profile.BranchCode,
            profile.MachineIdentificationNumber,
            profile.MachineSerialNumber,
            profile.PosTerminalNumber,
            profile.AccreditationNumber,
            profile.PermitToUseNumber,
            profile.SoftwareName,
            profile.SoftwareVersion,
            profile.SoftwareBuild,
            profile.ComplianceMode);
    }

    private static OfficialPrintableEvidenceFooter BuildEvidence(
        OfficialOutputEvidence evidence)
    {
        return new OfficialPrintableEvidenceFooter(
            evidence.OriginalEventId,
            evidence.SnapshotPayloadHash,
            evidence.SnapshotHashAlgorithm,
            evidence.SnapshotHashMatches);
    }

    private static OfficialPrintableLifecycleNotice BuildLifecycle(
        OfficialOutputLifecycle lifecycle)
    {
        (string code, string text) = lifecycle.State switch
        {
            OfficialDocumentLifecycleState.Active => ("ACTIVE", "Document lifecycle is active."),
            OfficialDocumentLifecycleState.Voided => ("VOIDED", "Document lifecycle is voided; original evidence remains preserved."),
            OfficialDocumentLifecycleState.Cancelled => ("CANCELLED", "Document lifecycle is cancelled; original evidence remains preserved."),
            OfficialDocumentLifecycleState.Invalid => ("INVALID", "Document lifecycle is invalid; audit review is required."),
            _ => ("UNKNOWN", "Document lifecycle state is unsupported.")
        };

        return new OfficialPrintableLifecycleNotice(
            lifecycle.State,
            code,
            text,
            lifecycle.CorrectionCount,
            lifecycle.CorrectionNotes.ToArray(),
            lifecycle.IsCoveredByFinalizedZReading,
            lifecycle.CoveringZReadingId,
            lifecycle.Issues.ToArray());
    }

    private static OfficialPrintableCopyInfo BuildCopyInfo(
        OfficialOutputLifecycle lifecycle,
        OfficialPrintableDocumentCopyMode copyMode)
    {
        int reprintCount = lifecycle.Events.Count(static eventRow =>
            string.Equals(eventRow.EventType, "REPRINT", StringComparison.Ordinal) ||
            eventRow.ReprintMarker);

        return copyMode switch
        {
            OfficialPrintableDocumentCopyMode.Original => new OfficialPrintableCopyInfo(
                copyMode,
                "ORIGINAL",
                IsCopyOrReprint: false,
                PriorReprintEventCount: reprintCount),
            OfficialPrintableDocumentCopyMode.Copy => new OfficialPrintableCopyInfo(
                copyMode,
                "COPY",
                IsCopyOrReprint: true,
                PriorReprintEventCount: reprintCount),
            OfficialPrintableDocumentCopyMode.Reprint => new OfficialPrintableCopyInfo(
                copyMode,
                "COPY/REPRINT",
                IsCopyOrReprint: true,
                PriorReprintEventCount: reprintCount),
            _ => throw new InvalidOperationException("Unsupported official printable document copy mode.")
        };
    }
}

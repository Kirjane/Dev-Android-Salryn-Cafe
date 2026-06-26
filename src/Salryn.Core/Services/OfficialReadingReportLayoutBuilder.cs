using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialReadingReportKind
{
    XReading = 1,
    ZReading = 2
}

public sealed record OfficialReadingReportInput(
    OfficialReadingReportKind ReportKind,
    string? ReadingNumber,
    string? ReportNumber,
    DateTimeOffset GeneratedAt,
    string TerminalId,
    Guid? CashierUserId,
    string? CashierIdentifier,
    string? ReportLabel,
    OfficialReadingReportMachineProfile? MachineProfile,
    OfficialReadingReportBusinessPeriod? BusinessPeriod,
    OfficialReadingReportSalesTotals? SalesTotals,
    OfficialReadingReportTaxTotals? TaxTotals,
    IReadOnlyList<OfficialReadingReportPaymentTotal>? PaymentTotals,
    IReadOnlyList<OfficialReadingReportBreakdownLine>? CashierOrTerminalBreakdown,
    OfficialReadingReportLifecycleCoverage? LifecycleCoverage,
    OfficialReadingReportEvidence? Evidence);

public sealed record OfficialReadingReportMachineProfile(
    string RegisteredTaxpayerName,
    string BusinessName,
    string RegisteredAddress,
    string BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string MachineIdentificationNumber,
    string MachineSerialNumber,
    string PosTerminalNumber,
    string AccreditationNumber,
    string PermitToUseNumber,
    string SoftwareName,
    string SoftwareVersion,
    string SoftwareBuild,
    string ComplianceMode);

public sealed record OfficialReadingReportBusinessPeriod(
    DateOnly BusinessDate,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    bool IsFinalizedZReading,
    Guid? PreviousZReadingId,
    Guid? CurrentZReadingId);

public sealed record OfficialReadingReportSalesTotals(
    long GrossSalesCents,
    long NetSalesCents,
    long DiscountCents,
    long? RefundVoidCancelCents,
    long? TaxableSalesCents,
    long? NonTaxableSalesCents,
    long TotalSalesCents,
    int TransactionCount,
    int VoidedCount,
    int CancelledCount,
    int? CorrectedCount);

public sealed record OfficialReadingReportTaxTotals(
    string TaxModeCode,
    long TaxCents,
    bool VatBucketEngineDeferred,
    string? TaxModeNotes,
    IReadOnlyList<string>? DeferredComplianceNotes);

public sealed record OfficialReadingReportPaymentTotal(
    string Method,
    long AmountCents,
    int? Count);

public sealed record OfficialReadingReportBreakdownLine(
    string? CashierIdentifier,
    string? TerminalId,
    int TransactionCount,
    long TotalCents);

public sealed record OfficialReadingReportLifecycleCoverage(
    int? CoveredEventCount,
    int? CoveredSaleCount,
    Guid? FirstOriginalEventId,
    Guid? LastOriginalEventId,
    Guid? FirstJournalEntryId,
    Guid? LastJournalEntryId);

public sealed record OfficialReadingReportEvidence(
    string? SourceEvidenceHash,
    string? SnapshotPayloadHash,
    string HashAlgorithm,
    string GeneratedFrom);

public sealed record OfficialReadingReportLayoutDocument(
    OfficialReadingReportKind ReportKind,
    IReadOnlyList<OfficialReadingReportLayoutSection> Sections);

public sealed record OfficialReadingReportLayoutSection(
    string Name,
    IReadOnlyList<OfficialReadingReportLayoutLine> Lines);

public sealed record OfficialReadingReportLayoutLine(
    string Label,
    string Value);

public sealed record OfficialReadingReportTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public sealed record OfficialReadingReportPreviewArtifactPackage(
    OfficialReadingReportKind ReportKind,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    int LineCount,
    OfficialReadingReportTextRenderResult TextResult);

public sealed record OfficialReadingReportPreviewResult(
    OfficialReadingReportKind ReportKind,
    OfficialReadingReportLayoutDocument LayoutDocument,
    OfficialReadingReportTextRenderResult TextResult,
    OfficialReadingReportPreviewArtifactPackage ArtifactPackage,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    int LineCount);

// Kirch: why - X/Z reading output gets a deterministic Core report shape without finalizing, persisting, printing, or templating.
public sealed class OfficialReadingReportLayoutBuilder
{
    public Result<OfficialReadingReportLayoutDocument> Build(
        OfficialReadingReportInput? input)
    {
        Result validation = ValidateInput(input);

        if (validation.IsFailure)
        {
            return Result<OfficialReadingReportLayoutDocument>.Fail(validation.ErrorDetail);
        }

        OfficialReadingReportInput report = input!;

        OfficialReadingReportLayoutDocument document = new(
            report.ReportKind,
            new[]
            {
                BuildReportHeader(report),
                BuildMachineProfile(report.MachineProfile!),
                BuildBusinessPeriod(report.BusinessPeriod!),
                BuildSalesTotals(report.SalesTotals!),
                BuildTaxTotals(report.TaxTotals!),
                BuildPaymentTotals(report.PaymentTotals!),
                BuildCashierOrTerminalBreakdown(report.CashierOrTerminalBreakdown!),
                BuildLifecycleCoverage(report.ReportKind, report.LifecycleCoverage!),
                BuildEvidenceFooter(report.Evidence!)
            });

        return Result<OfficialReadingReportLayoutDocument>.Success(document);
    }

    public static Result ValidateInput(OfficialReadingReportInput? input)
    {
        if (input is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InputRequired",
                "Official reading report input is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialReadingReportKind), input.ReportKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidKind",
                "Official reading report kind is invalid."));
        }

        if (input.GeneratedAt == default ||
            string.IsNullOrWhiteSpace(input.TerminalId))
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.HeaderRequired",
                "Official reading report header is incomplete."));
        }

        if (input.CashierUserId.HasValue && input.CashierUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidCashierUserId",
                "Official reading report cashier user id is invalid."));
        }

        Result machineProfileValidation = ValidateMachineProfile(input.MachineProfile);

        if (machineProfileValidation.IsFailure)
        {
            return machineProfileValidation;
        }

        Result periodValidation = ValidateBusinessPeriod(input.BusinessPeriod);

        if (periodValidation.IsFailure)
        {
            return periodValidation;
        }

        Result salesValidation = ValidateSalesTotals(input.SalesTotals);

        if (salesValidation.IsFailure)
        {
            return salesValidation;
        }

        Result taxValidation = ValidateTaxTotals(input.TaxTotals);

        if (taxValidation.IsFailure)
        {
            return taxValidation;
        }

        Result paymentValidation = ValidatePaymentTotals(input.PaymentTotals);

        if (paymentValidation.IsFailure)
        {
            return paymentValidation;
        }

        Result breakdownValidation = ValidateBreakdown(input.CashierOrTerminalBreakdown);

        if (breakdownValidation.IsFailure)
        {
            return breakdownValidation;
        }

        Result lifecycleValidation = ValidateLifecycleCoverage(input.LifecycleCoverage);

        if (lifecycleValidation.IsFailure)
        {
            return lifecycleValidation;
        }

        return ValidateEvidence(input.Evidence);
    }

    private static Result ValidateMachineProfile(
        OfficialReadingReportMachineProfile? machineProfile)
    {
        if (machineProfile is null ||
            string.IsNullOrWhiteSpace(machineProfile.RegisteredTaxpayerName) ||
            string.IsNullOrWhiteSpace(machineProfile.RegisteredAddress) ||
            string.IsNullOrWhiteSpace(machineProfile.TaxpayerIdentificationNumber) ||
            string.IsNullOrWhiteSpace(machineProfile.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.MachineProfileRequired",
                "Official reading report requires machine profile identity."));
        }

        return Result.Success();
    }

    private static Result ValidateBusinessPeriod(
        OfficialReadingReportBusinessPeriod? businessPeriod)
    {
        if (businessPeriod is null ||
            businessPeriod.BusinessDate == default ||
            businessPeriod.PeriodStart == default ||
            businessPeriod.PeriodEnd == default ||
            businessPeriod.PeriodEnd <= businessPeriod.PeriodStart)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidBusinessPeriod",
                "Official reading report business period is invalid."));
        }

        if (businessPeriod.PreviousZReadingId == Guid.Empty ||
            businessPeriod.CurrentZReadingId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidZReadingId",
                "Official reading report Z-reading id is invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateSalesTotals(
        OfficialReadingReportSalesTotals? salesTotals)
    {
        if (salesTotals is null ||
            salesTotals.GrossSalesCents < 0 ||
            salesTotals.NetSalesCents < 0 ||
            salesTotals.DiscountCents < 0 ||
            salesTotals.RefundVoidCancelCents is < 0 ||
            salesTotals.TaxableSalesCents is < 0 ||
            salesTotals.NonTaxableSalesCents is < 0 ||
            salesTotals.TotalSalesCents < 0 ||
            salesTotals.TransactionCount < 0 ||
            salesTotals.VoidedCount < 0 ||
            salesTotals.CancelledCount < 0 ||
            salesTotals.CorrectedCount is < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidSalesTotals",
                "Official reading report sales totals are invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateTaxTotals(
        OfficialReadingReportTaxTotals? taxTotals)
    {
        if (taxTotals is null ||
            string.IsNullOrWhiteSpace(taxTotals.TaxModeCode) ||
            taxTotals.TaxCents < 0 ||
            taxTotals.DeferredComplianceNotes is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidTaxTotals",
                "Official reading report tax totals are invalid."));
        }

        return Result.Success();
    }

    private static Result ValidatePaymentTotals(
        IReadOnlyList<OfficialReadingReportPaymentTotal>? paymentTotals)
    {
        if (paymentTotals is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.PaymentTotalsRequired",
                "Official reading report payment totals are required."));
        }

        HashSet<string> methods = new(StringComparer.Ordinal);

        foreach (OfficialReadingReportPaymentTotal payment in paymentTotals)
        {
            string method = Normalize(payment.Method);

            if (method.Length == 0 ||
                payment.AmountCents < 0 ||
                payment.Count is < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReport.InvalidPaymentTotal",
                    "Official reading report payment total is invalid."));
            }

            if (!methods.Add(method))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReport.DuplicatePaymentMethod",
                    "Official reading report payment totals contain duplicate methods."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateBreakdown(
        IReadOnlyList<OfficialReadingReportBreakdownLine>? breakdown)
    {
        if (breakdown is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.BreakdownRequired",
                "Official reading report cashier or terminal breakdown is required."));
        }

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (OfficialReadingReportBreakdownLine line in breakdown)
        {
            string key = BreakdownKey(line);

            if (key.Length == 0 ||
                line.TransactionCount < 0 ||
                line.TotalCents < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReport.InvalidBreakdownLine",
                    "Official reading report breakdown line is invalid."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReport.DuplicateBreakdownKey",
                    "Official reading report breakdown contains duplicate keys."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateLifecycleCoverage(
        OfficialReadingReportLifecycleCoverage? lifecycleCoverage)
    {
        if (lifecycleCoverage is null ||
            lifecycleCoverage.CoveredEventCount is < 0 ||
            lifecycleCoverage.CoveredSaleCount is < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidLifecycleCoverage",
                "Official reading report lifecycle coverage is invalid."));
        }

        if (lifecycleCoverage.FirstOriginalEventId == Guid.Empty ||
            lifecycleCoverage.LastOriginalEventId == Guid.Empty ||
            lifecycleCoverage.FirstJournalEntryId == Guid.Empty ||
            lifecycleCoverage.LastJournalEntryId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.InvalidLifecycleCoverageId",
                "Official reading report lifecycle coverage id is invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateEvidence(
        OfficialReadingReportEvidence? evidence)
    {
        if (evidence is null ||
            string.IsNullOrWhiteSpace(evidence.HashAlgorithm) ||
            string.IsNullOrWhiteSpace(evidence.GeneratedFrom) ||
            (string.IsNullOrWhiteSpace(evidence.SourceEvidenceHash) &&
                string.IsNullOrWhiteSpace(evidence.SnapshotPayloadHash)))
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReport.EvidenceRequired",
                "Official reading report evidence footer is required."));
        }

        return Result.Success();
    }

    private static OfficialReadingReportLayoutSection BuildReportHeader(
        OfficialReadingReportInput input)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "ReportKind", input.ReportKind.ToString());
        AddOptional(lines, "ReadingNumber", input.ReadingNumber);
        AddOptional(lines, "ReportNumber", input.ReportNumber);
        Add(lines, "GeneratedAt", Format(input.GeneratedAt));
        Add(lines, "TerminalId", input.TerminalId);
        AddOptional(lines, "CashierUserId", input.CashierUserId);
        AddOptional(lines, "CashierIdentifier", input.CashierIdentifier);
        Add(lines, "ReportLabel", Normalize(input.ReportLabel).Length == 0
            ? DefaultArtifactLabel(input.ReportKind)
            : Normalize(input.ReportLabel));

        return Section("ReportHeader", lines);
    }

    private static OfficialReadingReportLayoutSection BuildMachineProfile(
        OfficialReadingReportMachineProfile profile)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "RegisteredTaxpayerName", profile.RegisteredTaxpayerName);
        Add(lines, "BusinessName", profile.BusinessName);
        Add(lines, "RegisteredAddress", profile.RegisteredAddress);
        Add(lines, "BranchAddress", profile.BranchAddress);
        Add(lines, "TaxpayerIdentificationNumber", profile.TaxpayerIdentificationNumber);
        Add(lines, "BranchCode", profile.BranchCode);
        Add(lines, "MachineIdentificationNumber", profile.MachineIdentificationNumber);
        Add(lines, "MachineSerialNumber", profile.MachineSerialNumber);
        Add(lines, "PosTerminalNumber", profile.PosTerminalNumber);
        Add(lines, "AccreditationNumber", profile.AccreditationNumber);
        Add(lines, "PermitToUseNumber", profile.PermitToUseNumber);
        Add(lines, "SoftwareName", profile.SoftwareName);
        Add(lines, "SoftwareVersion", profile.SoftwareVersion);
        Add(lines, "SoftwareBuild", profile.SoftwareBuild);
        Add(lines, "ComplianceMode", profile.ComplianceMode);

        return Section("MachineProfile", lines);
    }

    private static OfficialReadingReportLayoutSection BuildBusinessPeriod(
        OfficialReadingReportBusinessPeriod period)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "BusinessDate", period.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(lines, "PeriodStart", Format(period.PeriodStart));
        Add(lines, "PeriodEnd", Format(period.PeriodEnd));
        Add(lines, "IsFinalizedZReading", Format(period.IsFinalizedZReading));
        AddOptional(lines, "PreviousZReadingId", period.PreviousZReadingId);
        AddOptional(lines, "CurrentZReadingId", period.CurrentZReadingId);

        return Section("BusinessPeriod", lines);
    }

    private static OfficialReadingReportLayoutSection BuildSalesTotals(
        OfficialReadingReportSalesTotals totals)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "GrossSalesCents", totals.GrossSalesCents);
        Add(lines, "NetSalesCents", totals.NetSalesCents);
        Add(lines, "DiscountCents", totals.DiscountCents);
        AddOptional(lines, "RefundVoidCancelCents", totals.RefundVoidCancelCents);
        AddOptional(lines, "TaxableSalesCents", totals.TaxableSalesCents);
        AddOptional(lines, "NonTaxableSalesCents", totals.NonTaxableSalesCents);
        Add(lines, "TotalSalesCents", totals.TotalSalesCents);
        Add(lines, "TransactionCount", totals.TransactionCount);
        Add(lines, "VoidedCount", totals.VoidedCount);
        Add(lines, "CancelledCount", totals.CancelledCount);
        AddOptional(lines, "CorrectedCount", totals.CorrectedCount);

        return Section("SalesTotals", lines);
    }

    private static OfficialReadingReportLayoutSection BuildTaxTotals(
        OfficialReadingReportTaxTotals totals)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "TaxModeCode", totals.TaxModeCode);
        Add(lines, "TaxCents", totals.TaxCents);
        Add(lines, "VatBucketEngineDeferred", Format(totals.VatBucketEngineDeferred));
        AddOptional(lines, "TaxModeNotes", totals.TaxModeNotes);
        AddOptional(lines, "DeferredComplianceNotes", JoinValues(totals.DeferredComplianceNotes!));

        return Section("TaxTotals", lines);
    }

    private static OfficialReadingReportLayoutSection BuildPaymentTotals(
        IReadOnlyList<OfficialReadingReportPaymentTotal> payments)
    {
        OfficialReadingReportLayoutLine[] lines = payments
            .Select(payment =>
            {
                List<(string Name, string Value)> fields = new()
                {
                    ("Method", Normalize(payment.Method)),
                    ("AmountCents", Format(payment.AmountCents))
                };

                if (payment.Count.HasValue)
                {
                    fields.Add(("Count", Format(payment.Count.Value)));
                }

                return CompoundLine("PaymentTotal", fields);
            })
            .ToArray();

        return Section("PaymentTotals", lines);
    }

    private static OfficialReadingReportLayoutSection BuildCashierOrTerminalBreakdown(
        IReadOnlyList<OfficialReadingReportBreakdownLine> breakdown)
    {
        OfficialReadingReportLayoutLine[] lines = breakdown
            .Select(line =>
            {
                List<(string Name, string Value)> fields = new();

                if (Normalize(line.CashierIdentifier).Length > 0)
                {
                    fields.Add(("CashierIdentifier", Normalize(line.CashierIdentifier)));
                }

                if (Normalize(line.TerminalId).Length > 0)
                {
                    fields.Add(("TerminalId", Normalize(line.TerminalId)));
                }

                fields.Add(("TransactionCount", Format(line.TransactionCount)));
                fields.Add(("TotalCents", Format(line.TotalCents)));

                return CompoundLine("Breakdown", fields);
            })
            .ToArray();

        return Section("CashierOrTerminalBreakdown", lines);
    }

    private static OfficialReadingReportLayoutSection BuildLifecycleCoverage(
        OfficialReadingReportKind reportKind,
        OfficialReadingReportLifecycleCoverage coverage)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        Add(lines, "CoverageMode", reportKind == OfficialReadingReportKind.XReading
            ? "NON_FINALIZING_PREVIEW_REPORT_COVERAGE"
            : "FINALIZING_COVERAGE_DATA_ONLY");
        Add(lines, "StateMutationMarker", "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION");
        AddOptional(lines, "CoveredEventCount", coverage.CoveredEventCount);
        AddOptional(lines, "CoveredSaleCount", coverage.CoveredSaleCount);
        AddOptional(lines, "FirstOriginalEventId", coverage.FirstOriginalEventId);
        AddOptional(lines, "LastOriginalEventId", coverage.LastOriginalEventId);
        AddOptional(lines, "FirstJournalEntryId", coverage.FirstJournalEntryId);
        AddOptional(lines, "LastJournalEntryId", coverage.LastJournalEntryId);

        return Section("LifecycleCoverage", lines);
    }

    private static OfficialReadingReportLayoutSection BuildEvidenceFooter(
        OfficialReadingReportEvidence evidence)
    {
        List<OfficialReadingReportLayoutLine> lines = new();

        AddOptional(lines, "SourceEvidenceHash", evidence.SourceEvidenceHash);
        AddOptional(lines, "SnapshotPayloadHash", evidence.SnapshotPayloadHash);
        Add(lines, "HashAlgorithm", evidence.HashAlgorithm);
        Add(lines, "GeneratedFrom", evidence.GeneratedFrom);

        return Section("EvidenceFooter", lines);
    }

    private static OfficialReadingReportLayoutSection Section(
        string name,
        IReadOnlyList<OfficialReadingReportLayoutLine> lines)
    {
        return new OfficialReadingReportLayoutSection(name, lines.ToArray());
    }

    private static OfficialReadingReportLayoutLine CompoundLine(
        string label,
        IReadOnlyList<(string Name, string Value)> fields)
    {
        return new OfficialReadingReportLayoutLine(
            label,
            string.Join(" | ", fields.Select(static field => field.Name + "=" + Normalize(field.Value))));
    }

    private static void Add(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        string? value)
    {
        lines.Add(new OfficialReadingReportLayoutLine(label, Normalize(value)));
    }

    private static void Add(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        long value)
    {
        Add(lines, label, Format(value));
    }

    private static void Add(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        int value)
    {
        Add(lines, label, Format(value));
    }

    private static void AddOptional(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        string? value)
    {
        string normalized = Normalize(value);

        if (normalized.Length > 0)
        {
            Add(lines, label, normalized);
        }
    }

    private static void AddOptional(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        Guid? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, Format(value.Value));
        }
    }

    private static void AddOptional(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        long? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, Format(value.Value));
        }
    }

    private static void AddOptional(
        List<OfficialReadingReportLayoutLine> lines,
        string label,
        int? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, Format(value.Value));
        }
    }

    internal static string DefaultArtifactLabel(OfficialReadingReportKind reportKind)
    {
        return reportKind == OfficialReadingReportKind.XReading
            ? "OFFICIAL_X_READING_PREVIEW"
            : "OFFICIAL_Z_READING_PREVIEW";
    }

    private static string BreakdownKey(OfficialReadingReportBreakdownLine line)
    {
        string cashierIdentifier = Normalize(line.CashierIdentifier);

        if (cashierIdentifier.Length > 0)
        {
            return "CashierIdentifier=" + cashierIdentifier;
        }

        string terminalId = Normalize(line.TerminalId);

        return terminalId.Length == 0
            ? string.Empty
            : "TerminalId=" + terminalId;
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Format(Guid value)
    {
        return value.ToString("D");
    }

    private static string Format(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static string JoinValues(IReadOnlyList<string> values)
    {
        return string.Join(" | ", values.Select(Normalize).Where(value => value.Length > 0));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialReadingReportTextRenderer
{
    private const string Title = "SALRYN OFFICIAL X/Z READING LAYOUT PROOF";

    private static readonly IReadOnlyList<string> SectionOrder = new[]
    {
        "ReportHeader",
        "MachineProfile",
        "BusinessPeriod",
        "SalesTotals",
        "TaxTotals",
        "PaymentTotals",
        "CashierOrTerminalBreakdown",
        "LifecycleCoverage",
        "EvidenceFooter"
    };

    private static readonly IReadOnlyDictionary<string, string> SectionMarkers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReportHeader"] = "[REPORT]",
            ["MachineProfile"] = "[MACHINE]",
            ["BusinessPeriod"] = "[PERIOD]",
            ["SalesTotals"] = "[SALES_TOTALS]",
            ["TaxTotals"] = "[TAX_TOTALS]",
            ["PaymentTotals"] = "[PAYMENT_TOTALS]",
            ["CashierOrTerminalBreakdown"] = "[BREAKDOWN]",
            ["LifecycleCoverage"] = "[LIFECYCLE_COVERAGE]",
            ["EvidenceFooter"] = "[EVIDENCE]"
        };

    public Result<OfficialReadingReportTextRenderResult> Render(
        OfficialReadingReportLayoutDocument? document)
    {
        Result validation = ValidateDocument(document);

        if (validation.IsFailure)
        {
            return Result<OfficialReadingReportTextRenderResult>.Fail(validation.ErrorDetail);
        }

        List<string> lines = new()
        {
            Title
        };

        foreach (OfficialReadingReportLayoutSection section in OrderedSections(document!))
        {
            lines.Add(SectionMarker(section.Name));

            foreach (OfficialReadingReportLayoutLine line in section.Lines)
            {
                lines.Add(RenderLine(line));
            }
        }

        return Result<OfficialReadingReportTextRenderResult>.Success(
            new OfficialReadingReportTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static Result ValidateDocument(
        OfficialReadingReportLayoutDocument? document)
    {
        if (document is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReportText.LayoutRequired",
                "Official reading report text rendering requires a layout document."));
        }

        if (!Enum.IsDefined(typeof(OfficialReadingReportKind), document.ReportKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReportText.InvalidKind",
                "Official reading report text rendering found an invalid report kind."));
        }

        if (document.Sections is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReadingReportText.SectionsRequired",
                "Official reading report text rendering requires layout sections."));
        }

        HashSet<string> sectionNames = new(StringComparer.Ordinal);

        foreach (OfficialReadingReportLayoutSection section in document.Sections)
        {
            if (section is null ||
                string.IsNullOrWhiteSpace(section.Name) ||
                section.Lines is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReportText.InvalidSection",
                    "Official reading report text rendering found an invalid layout section."));
            }

            if (!sectionNames.Add(section.Name))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReadingReportText.DuplicateSection",
                    "Official reading report text rendering found duplicate layout sections."));
            }

            foreach (OfficialReadingReportLayoutLine line in section.Lines)
            {
                if (line is null || string.IsNullOrWhiteSpace(line.Label))
                {
                    return Result.Fail(Error.Validation(
                        "OfficialReadingReportText.InvalidLine",
                        "Official reading report text rendering found an invalid layout line."));
                }
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<OfficialReadingReportLayoutSection> OrderedSections(
        OfficialReadingReportLayoutDocument document)
    {
        Dictionary<string, OfficialReadingReportLayoutSection> sectionsByName =
            document.Sections.ToDictionary(
                static section => section.Name,
                static section => section,
                StringComparer.Ordinal);

        List<OfficialReadingReportLayoutSection> ordered = new();

        foreach (string sectionName in SectionOrder)
        {
            if (sectionsByName.TryGetValue(sectionName, out OfficialReadingReportLayoutSection? section))
            {
                ordered.Add(section);
            }
        }

        HashSet<string> knownSectionNames = new(SectionOrder, StringComparer.Ordinal);

        ordered.AddRange(document.Sections
            .Where(section => !knownSectionNames.Contains(section.Name))
            .OrderBy(static section => section.Name, StringComparer.Ordinal));

        return ordered;
    }

    private static string SectionMarker(string sectionName)
    {
        return SectionMarkers.TryGetValue(sectionName, out string? marker)
            ? marker
            : "[" + Normalize(sectionName).ToUpperInvariant() + "]";
    }

    private static string RenderLine(OfficialReadingReportLayoutLine line)
    {
        string value = Normalize(line.Value);

        return value.Length == 0
            ? Normalize(line.Label) + ":"
            : Normalize(line.Label) + ": " + value;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialReadingReportPreviewBuilder
{
    private const string ArtifactReferencePrefix = "salryn://official-reading-preview/";

    private readonly OfficialReadingReportLayoutBuilder _layoutBuilder;
    private readonly OfficialReadingReportTextRenderer _textRenderer;

    public OfficialReadingReportPreviewBuilder(
        OfficialReadingReportLayoutBuilder? layoutBuilder = null,
        OfficialReadingReportTextRenderer? textRenderer = null)
    {
        _layoutBuilder = layoutBuilder ?? new OfficialReadingReportLayoutBuilder();
        _textRenderer = textRenderer ?? new OfficialReadingReportTextRenderer();
    }

    public Result<OfficialReadingReportPreviewResult> BuildPreview(
        OfficialReadingReportInput? input,
        string? artifactLabel = null)
    {
        Result<OfficialReadingReportLayoutDocument> layoutResult = _layoutBuilder.Build(input);

        if (layoutResult.IsFailure)
        {
            return Result<OfficialReadingReportPreviewResult>.Fail(layoutResult.ErrorDetail);
        }

        Result<OfficialReadingReportTextRenderResult> textResult =
            _textRenderer.Render(layoutResult.Value);

        if (textResult.IsFailure)
        {
            return Result<OfficialReadingReportPreviewResult>.Fail(textResult.ErrorDetail);
        }

        string contentHash = ComputeContentHash(textResult.Value.Text);
        string referenceKind = layoutResult.Value.ReportKind.ToString();
        string artifactReference = ArtifactReferencePrefix + referenceKind + "/" + contentHash;
        string safeArtifactLabel = string.IsNullOrWhiteSpace(artifactLabel)
            ? OfficialReadingReportLayoutBuilder.DefaultArtifactLabel(layoutResult.Value.ReportKind)
            : artifactLabel.Trim();

        OfficialReadingReportPreviewArtifactPackage package = new(
            layoutResult.Value.ReportKind,
            safeArtifactLabel,
            artifactReference,
            contentHash,
            textResult.Value.Lines.Count,
            textResult.Value);

        return Result<OfficialReadingReportPreviewResult>.Success(
            new OfficialReadingReportPreviewResult(
                layoutResult.Value.ReportKind,
                layoutResult.Value,
                textResult.Value,
                package,
                safeArtifactLabel,
                artifactReference,
                contentHash,
                textResult.Value.Lines.Count));
    }

    private static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

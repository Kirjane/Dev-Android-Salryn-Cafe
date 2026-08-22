using System.Globalization;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialReportBindingDocument(
    IReadOnlyList<OfficialReportBindingTable> Tables);

public sealed record OfficialReportBindingTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<OfficialReportBindingRow> Rows);

public sealed record OfficialReportBindingRow(
    IReadOnlyList<OfficialReportBindingCell> Cells);

public sealed record OfficialReportBindingCell(
    string ColumnName,
    string Value);

// Kirch: why - report adapters need stable table-shaped data; compliance decisions stay in backend DTO builders, not templates.
public sealed class OfficialReportBindingBuilder
{
    public Result<OfficialReportBindingDocument> Build(
        OfficialPrintableDocument? document)
    {
        Result validation = ValidateDocument(document);

        if (validation.IsFailure)
        {
            return Result<OfficialReportBindingDocument>.Fail(validation.ErrorDetail);
        }

        OfficialPrintableDocument safeDocument = document!;

        OfficialReportBindingDocument bindingDocument = new(new[]
        {
            BuildHeaderTable(safeDocument),
            BuildMachineProfileTable(safeDocument),
            BuildLineItemsTable(safeDocument),
            BuildPaymentsTable(safeDocument),
            BuildTotalsTable(safeDocument),
            BuildEvidenceTable(safeDocument),
            BuildLifecycleTable(safeDocument),
            BuildCorrectionsTable(safeDocument),
            BuildLifecycleIssuesTable(safeDocument),
            BuildCopyAuditTable(safeDocument)
        });

        return Result<OfficialReportBindingDocument>.Success(bindingDocument);
    }

    public static Result ValidateDocument(OfficialPrintableDocument? document)
    {
        if (document is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.DocumentRequired",
                "Official report binding requires a printable document."));
        }

        if (document.Header is null ||
            string.IsNullOrWhiteSpace(document.Header.OfficialDocumentNumber) ||
            document.Header.JournalEntryId == Guid.Empty ||
            document.Header.SaleId == Guid.Empty ||
            document.Header.CashierUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.Header.CashierIdentifier))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.HeaderRequired",
                "Official report binding requires complete document header identity."));
        }

        if (document.MachineProfile is null ||
            document.MachineProfile.BirMachineProfileId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.MachineProfile.RegisteredTaxpayerName) ||
            string.IsNullOrWhiteSpace(document.MachineProfile.RegisteredAddress) ||
            string.IsNullOrWhiteSpace(document.MachineProfile.TaxpayerIdentificationNumber) ||
            string.IsNullOrWhiteSpace(document.MachineProfile.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.MachineProfileRequired",
                "Official report binding requires persisted machine profile identity."));
        }

        if (document.LineItems is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.LineItemsRequired",
                "Official report binding requires a line item collection."));
        }

        if (document.Payments is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.PaymentsRequired",
                "Official report binding requires a payment collection."));
        }

        if (document.Totals is null ||
            string.IsNullOrWhiteSpace(document.Totals.TaxModeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.TotalsRequired",
                "Official report binding requires totals and tax mode."));
        }

        if (document.Evidence is null ||
            document.Evidence.OriginalEventId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.Evidence.SnapshotPayloadHash) ||
            string.IsNullOrWhiteSpace(document.Evidence.SnapshotHashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.EvidenceRequired",
                "Official report binding requires official evidence footer data."));
        }

        if (document.Lifecycle is null ||
            string.IsNullOrWhiteSpace(document.Lifecycle.NoticeCode) ||
            string.IsNullOrWhiteSpace(document.Lifecycle.NoticeText))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.LifecycleRequired",
                "Official report binding requires lifecycle notice data."));
        }

        if (document.CopyAudit is null ||
            string.IsNullOrWhiteSpace(document.CopyAudit.OutputMarker))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportBinding.CopyAuditRequired",
                "Official report binding requires copy audit data."));
        }

        return Result.Success();
    }

    private static OfficialReportBindingTable BuildHeaderTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "DocumentKind",
            "OfficialDocumentNumber",
            "TransactionTimestamp",
            "JournalEntryId",
            "SaleId",
            "CashierUserId",
            "CashierIdentifier",
            "TerminalId"
        };

        return Table(
            "Header",
            columns,
            Row(columns,
                document.Header.DocumentKind.ToString(),
                document.Header.OfficialDocumentNumber,
                Format(document.Header.TransactionTimestamp),
                Format(document.Header.JournalEntryId),
                Format(document.Header.SaleId),
                Format(document.Header.CashierUserId),
                document.Header.CashierIdentifier,
                document.Header.TerminalId));
    }

    private static OfficialReportBindingTable BuildMachineProfileTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "BirMachineProfileId",
            "RegistrationScopeCode",
            "RegisteredTaxpayerName",
            "BusinessName",
            "RegisteredAddress",
            "BranchAddress",
            "TaxpayerIdentificationNumber",
            "BranchCode",
            "MachineIdentificationNumber",
            "MachineSerialNumber",
            "PosTerminalNumber",
            "AccreditationNumber",
            "PermitToUseNumber",
            "SoftwareName",
            "SoftwareVersion",
            "SoftwareBuild",
            "ComplianceMode"
        };

        OfficialPrintableMachineProfile profile = document.MachineProfile;

        return Table(
            "MachineProfile",
            columns,
            Row(columns,
                Format(profile.BirMachineProfileId),
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
                profile.ComplianceMode));
    }

    private static OfficialReportBindingTable BuildLineItemsTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "LineNumber",
            "Description",
            "Barcode",
            "UnitName",
            "QuantityX1000",
            "UnitPriceCents",
            "DiscountCents",
            "LineTotalCents"
        };

        OfficialReportBindingRow[] rows = document.LineItems
            .Select((line, index) => Row(columns,
                Format(index + 1),
                line.Description,
                line.Barcode,
                line.UnitName,
                Format(line.QuantityX1000),
                Format(line.UnitPriceCents),
                Format(line.DiscountCents),
                Format(line.LineTotalCents)))
            .ToArray();

        return Table("LineItems", columns, rows);
    }

    private static OfficialReportBindingTable BuildPaymentsTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "PaymentNumber",
            "Method",
            "AmountCents",
            "PaidAt"
        };

        OfficialReportBindingRow[] rows = document.Payments
            .Select((payment, index) => Row(columns,
                Format(index + 1),
                payment.Method.ToString(),
                Format(payment.AmountCents),
                Format(payment.PaidAt)))
            .ToArray();

        return Table("Payments", columns, rows);
    }

    private static OfficialReportBindingTable BuildTotalsTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "SubtotalCents",
            "DiscountCents",
            "TaxCents",
            "TotalCents",
            "AmountPaidCents",
            "ChangeCents",
            "TaxModeCode",
            "VatBucketEngineDeferred",
            "TaxModeNotes",
            "DeferredComplianceNotes"
        };

        OfficialPrintableTotals totals = document.Totals;

        return Table(
            "Totals",
            columns,
            Row(columns,
                Format(totals.SubtotalCents),
                Format(totals.DiscountCents),
                Format(totals.TaxCents),
                Format(totals.TotalCents),
                Format(totals.AmountPaidCents),
                Format(totals.ChangeCents),
                totals.TaxModeCode,
                Format(totals.VatBucketEngineDeferred),
                totals.TaxModeNotes,
                JoinValues(totals.DeferredComplianceNotes)));
    }

    private static OfficialReportBindingTable BuildEvidenceTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "OriginalEventId",
            "SnapshotPayloadHash",
            "SnapshotHashAlgorithm",
            "SnapshotHashMatches"
        };

        OfficialPrintableEvidenceFooter evidence = document.Evidence;

        return Table(
            "Evidence",
            columns,
            Row(columns,
                Format(evidence.OriginalEventId),
                evidence.SnapshotPayloadHash,
                evidence.SnapshotHashAlgorithm,
                Format(evidence.SnapshotHashMatches)));
    }

    private static OfficialReportBindingTable BuildLifecycleTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "State",
            "NoticeCode",
            "NoticeText",
            "CorrectionCount",
            "IsCoveredByFinalizedZReading",
            "CoveringZReadingId"
        };

        OfficialPrintableLifecycleNotice lifecycle = document.Lifecycle;

        return Table(
            "Lifecycle",
            columns,
            Row(columns,
                lifecycle.State.ToString(),
                lifecycle.NoticeCode,
                lifecycle.NoticeText,
                Format(lifecycle.CorrectionCount),
                Format(lifecycle.IsCoveredByFinalizedZReading),
                lifecycle.CoveringZReadingId.HasValue ? Format(lifecycle.CoveringZReadingId.Value) : null));
    }

    private static OfficialReportBindingTable BuildCorrectionsTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "CorrectionNumber",
            "CorrectionNote"
        };

        OfficialReportBindingRow[] rows = document.Lifecycle.CorrectionNotes
            .Select((note, index) => Row(columns, Format(index + 1), note))
            .ToArray();

        return Table("Corrections", columns, rows);
    }

    private static OfficialReportBindingTable BuildLifecycleIssuesTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "IssueNumber",
            "Code",
            "Message"
        };

        OfficialReportBindingRow[] rows = document.Lifecycle.Issues
            .Select((issue, index) => Row(columns, Format(index + 1), issue.Code, issue.Message))
            .ToArray();

        return Table("LifecycleIssues", columns, rows);
    }

    private static OfficialReportBindingTable BuildCopyAuditTable(
        OfficialPrintableDocument document)
    {
        string[] columns =
        {
            "CopyMode",
            "OutputMarker",
            "IsCopyOrReprint",
            "PriorReprintEventCount"
        };

        OfficialPrintableCopyInfo copyAudit = document.CopyAudit;

        return Table(
            "CopyAudit",
            columns,
            Row(columns,
                copyAudit.CopyMode.ToString(),
                copyAudit.OutputMarker,
                Format(copyAudit.IsCopyOrReprint),
                Format(copyAudit.PriorReprintEventCount)));
    }

    private static OfficialReportBindingTable Table(
        string name,
        IReadOnlyList<string> columns,
        params OfficialReportBindingRow[] rows)
    {
        return new OfficialReportBindingTable(name, columns.ToArray(), rows);
    }

    private static OfficialReportBindingRow Row(
        IReadOnlyList<string> columns,
        params string?[] values)
    {
        if (columns.Count != values.Length)
        {
            throw new InvalidOperationException("Official report binding row column/value count mismatch.");
        }

        OfficialReportBindingCell[] cells = columns
            .Select((column, index) => new OfficialReportBindingCell(column, Safe(values[index])))
            .ToArray();

        return new OfficialReportBindingRow(cells);
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
        return string.Join(" | ", values.Select(Safe));
    }

    private static string Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

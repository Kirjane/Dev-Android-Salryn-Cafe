using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialReportRenderFormat
{
    Preview = 1,
    Pdf = 2,
    PrintJob = 3,
    Export = 4
}

public sealed record OfficialReportRenderRequest(
    OfficialReportBindingDocument? SourceDocument,
    OfficialReportRenderFormat Format,
    string? ArtifactLabel,
    Guid? RequestedByUserId,
    DateTimeOffset? RequestedAt,
    string? TerminalId,
    string? CopyModeMarker,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialReportRenderResult(
    OfficialReportRenderedArtifact Artifact,
    OfficialReportRendererContract RendererContract);

public sealed record OfficialReportRenderedArtifact(
    OfficialReportRenderFormat Format,
    string ArtifactLabel,
    string? ArtifactReference,
    DateTimeOffset? CreatedAt);

public sealed record OfficialReportRendererCapability(
    OfficialReportRenderFormat Format,
    string Description);

public sealed record OfficialReportRendererContract(
    string RendererName,
    IReadOnlyList<OfficialReportRendererCapability> Capabilities);

public interface IOfficialReportRenderer
{
    Task<Result<OfficialReportRenderResult>> RenderAsync(
        OfficialReportRenderRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - renderers are adapters over prepared binding DTOs only; they must not own source evidence or compliance logic.
public static class OfficialReportRendererContractValidator
{
    public static readonly IReadOnlyList<string> RequiredTableNames = new[]
    {
        "Header",
        "MachineProfile",
        "LineItems",
        "Payments",
        "Totals",
        "Evidence",
        "Lifecycle",
        "Corrections",
        "LifecycleIssues",
        "CopyAudit"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredTableColumns =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Header"] = new[]
            {
                "DocumentKind",
                "OfficialDocumentNumber",
                "TransactionTimestamp",
                "JournalEntryId",
                "SaleId",
                "CashierUserId",
                "CashierIdentifier",
                "TerminalId"
            },
            ["MachineProfile"] = new[]
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
            },
            ["LineItems"] = new[]
            {
                "LineNumber",
                "Description",
                "Barcode",
                "UnitName",
                "QuantityX1000",
                "UnitPriceCents",
                "DiscountCents",
                "LineTotalCents"
            },
            ["Payments"] = new[]
            {
                "PaymentNumber",
                "Method",
                "AmountCents",
                "PaidAt"
            },
            ["Totals"] = new[]
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
            },
            ["Evidence"] = new[]
            {
                "OriginalEventId",
                "SnapshotPayloadHash",
                "SnapshotHashAlgorithm",
                "SnapshotHashMatches"
            },
            ["Lifecycle"] = new[]
            {
                "State",
                "NoticeCode",
                "NoticeText",
                "CorrectionCount",
                "IsCoveredByFinalizedZReading",
                "CoveringZReadingId"
            },
            ["Corrections"] = new[]
            {
                "CorrectionNumber",
                "CorrectionNote"
            },
            ["LifecycleIssues"] = new[]
            {
                "IssueNumber",
                "Code",
                "Message"
            },
            ["CopyAudit"] = new[]
            {
                "CopyMode",
                "OutputMarker",
                "IsCopyOrReprint",
                "PriorReprintEventCount"
            }
        };

    public static Result ValidateRequest(OfficialReportRenderRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.RequestRequired",
                "Official report render request is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialReportRenderFormat), request.Format))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.UnsupportedFormat",
                "Official report render format is unsupported."));
        }

        if (request.SourceDocument is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.SourceDocumentRequired",
                "Official report renderer requires an official report binding document."));
        }

        Result bindingValidation = ValidateBindingDocument(request.SourceDocument);

        if (bindingValidation.IsFailure)
        {
            return bindingValidation;
        }

        if (RequiresArtifactLabel(request.Format) &&
            string.IsNullOrWhiteSpace(request.ArtifactLabel))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.ArtifactLabelRequired",
                "Official report renderer requires an artifact label for the requested format."));
        }

        if (request.RequestedByUserId.HasValue &&
            request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.InvalidRequestedByUserId",
                "Official report render requester user id is invalid."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static OfficialReportRenderRequest NormalizeRequest(
        OfficialReportRenderRequest request)
    {
        return request with
        {
            ArtifactLabel = NormalizeOptional(request.ArtifactLabel),
            TerminalId = NormalizeOptional(request.TerminalId),
            CopyModeMarker = NormalizeOptional(request.CopyModeMarker),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    public static Result ValidateContract(OfficialReportRendererContract? contract)
    {
        if (contract is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.ContractRequired",
                "Official report renderer contract is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.RendererName))
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.RendererNameRequired",
                "Official report renderer name is required."));
        }

        if (contract.Capabilities is null || contract.Capabilities.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.CapabilitiesRequired",
                "Official report renderer must declare capabilities."));
        }

        HashSet<OfficialReportRenderFormat> formats = new();

        foreach (OfficialReportRendererCapability capability in contract.Capabilities)
        {
            if (capability is null ||
                !Enum.IsDefined(typeof(OfficialReportRenderFormat), capability.Format) ||
                string.IsNullOrWhiteSpace(capability.Description))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.InvalidCapability",
                    "Official report renderer capability is invalid."));
            }

            if (!formats.Add(capability.Format))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.DuplicateCapability",
                    "Official report renderer contract contains duplicate capability formats."));
            }
        }

        return Result.Success();
    }

    public static Result ValidateBindingDocument(OfficialReportBindingDocument? document)
    {
        if (document is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.SourceDocumentRequired",
                "Official report renderer requires an official report binding document."));
        }

        if (document.Tables is null || document.Tables.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.BindingTablesRequired",
                "Official report binding document must contain tables."));
        }

        HashSet<string> tableNames = new(StringComparer.Ordinal);

        foreach (OfficialReportBindingTable table in document.Tables)
        {
            Result tableValidation = ValidateTableShape(table);

            if (tableValidation.IsFailure)
            {
                return tableValidation;
            }

            if (!tableNames.Add(table.Name))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.DuplicateBindingTable",
                    "Official report binding document contains duplicate table names."));
            }
        }

        foreach (string requiredTableName in RequiredTableNames)
        {
            if (!tableNames.Contains(requiredTableName))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.RequiredBindingTableMissing",
                    "Official report binding document is missing a required table."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateTableShape(OfficialReportBindingTable? table)
    {
        if (table is null ||
            string.IsNullOrWhiteSpace(table.Name) ||
            table.Columns is null ||
            table.Rows is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.InvalidBindingTable",
                "Official report binding table is invalid."));
        }

        Result columnValidation = ValidateColumns(table);

        if (columnValidation.IsFailure)
        {
            return columnValidation;
        }

        Result rowValidation = ValidateRows(table);

        if (rowValidation.IsFailure)
        {
            return rowValidation;
        }

        return Result.Success();
    }

    private static Result ValidateColumns(OfficialReportBindingTable table)
    {
        HashSet<string> columnNames = new(StringComparer.Ordinal);

        foreach (string columnName in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.BindingTableSchemaMismatch",
                    "Official report binding table has an empty column name."));
            }

            if (!columnNames.Add(columnName))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.DuplicateBindingColumn",
                    "Official report binding table contains duplicate columns."));
            }
        }

        if (!RequiredTableColumns.TryGetValue(table.Name, out IReadOnlyList<string>? expectedColumns))
        {
            return Result.Success();
        }

        if (table.Columns.Count != expectedColumns.Count)
        {
            return Result.Fail(Error.Validation(
                "OfficialReportRenderer.BindingTableSchemaMismatch",
                "Official report binding table columns do not match the required schema."));
        }

        for (int index = 0; index < expectedColumns.Count; index++)
        {
            if (!string.Equals(table.Columns[index], expectedColumns[index], StringComparison.Ordinal))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.BindingTableSchemaMismatch",
                    "Official report binding table columns do not match the required schema."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateRows(OfficialReportBindingTable table)
    {
        foreach (OfficialReportBindingRow row in table.Rows)
        {
            if (row is null ||
                row.Cells is null ||
                row.Cells.Count != table.Columns.Count)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.InvalidBindingRow",
                    "Official report binding row does not match table columns."));
            }

            for (int index = 0; index < row.Cells.Count; index++)
            {
                OfficialReportBindingCell cell = row.Cells[index];

                if (cell is null ||
                    string.IsNullOrWhiteSpace(cell.ColumnName) ||
                    cell.Value is null ||
                    !string.Equals(cell.ColumnName, table.Columns[index], StringComparison.Ordinal))
                {
                    return Result.Fail(Error.Validation(
                        "OfficialReportRenderer.InvalidBindingCell",
                        "Official report binding cell does not match table columns."));
                }
            }
        }

        return Result.Success();
    }

    private static Result ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (!keys.Add(pair.Key.Trim()))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.DuplicateMetadataKey",
                    "Official report render metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    private static bool RequiresArtifactLabel(OfficialReportRenderFormat format)
    {
        return format is OfficialReportRenderFormat.Pdf or
            OfficialReportRenderFormat.PrintJob or
            OfficialReportRenderFormat.Export;
    }

    private static IReadOnlyDictionary<string, string>? NormalizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string? key = NormalizeOptional(pair.Key);

            if (key is null || normalized.ContainsKey(key))
            {
                continue;
            }

            normalized.Add(key, pair.Value?.Trim() ?? string.Empty);
        }

        return normalized
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

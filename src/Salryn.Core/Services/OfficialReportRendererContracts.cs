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

        return Result.Success();
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

        foreach (OfficialReportRendererCapability capability in contract.Capabilities)
        {
            if (!Enum.IsDefined(typeof(OfficialReportRenderFormat), capability.Format) ||
                string.IsNullOrWhiteSpace(capability.Description))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.InvalidCapability",
                    "Official report renderer capability is invalid."));
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
            if (table is null ||
                string.IsNullOrWhiteSpace(table.Name) ||
                table.Columns is null ||
                table.Rows is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReportRenderer.InvalidBindingTable",
                    "Official report binding table is invalid."));
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

        return metadata
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value?.Trim() ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

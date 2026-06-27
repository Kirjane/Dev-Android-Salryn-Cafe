using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialOutputTextFileSourceSelectionRequest(
    OfficialOutputSourceReadRequest? SourceReadRequest,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata,
    OfficialPrintableDocumentCopyMode CopyMode);

public sealed record OfficialOutputTextFileSourceSelectionResult(
    bool Succeeded,
    string StatusCode,
    string StatusMessage,
    string? ErrorCode,
    string? ErrorMessage,
    OfficialOutputTextFileTrustedPackageProducerRequest? ProducerRequest)
{
    public static OfficialOutputTextFileSourceSelectionResult Success(
        OfficialOutputTextFileTrustedPackageProducerRequest producerRequest)
    {
        ArgumentNullException.ThrowIfNull(producerRequest);

        return new OfficialOutputTextFileSourceSelectionResult(
            Succeeded: true,
            StatusCode: "OfficialOutputTextFileSourceSelection.Selected",
            StatusMessage: "Official output text-file source selection was converted to a trusted package producer request.",
            ErrorCode: null,
            ErrorMessage: null,
            ProducerRequest: producerRequest);
    }

    public static OfficialOutputTextFileSourceSelectionResult Failure(Error error)
    {
        return new OfficialOutputTextFileSourceSelectionResult(
            Succeeded: false,
            StatusCode: "OfficialOutputTextFileSourceSelection.RequestInvalid",
            StatusMessage: "Official output text-file source selection request is invalid.",
            ErrorCode: error.Code,
            ErrorMessage: error.Message,
            ProducerRequest: null);
    }
}

public interface IOfficialOutputTextFileSourceSelectionBoundary
{
    OfficialOutputTextFileSourceSelectionResult SelectSource(
        OfficialOutputTextFileSourceSelectionRequest? request);
}

public sealed class OfficialOutputTextFileSourceSelectionBoundary : IOfficialOutputTextFileSourceSelectionBoundary
{
    public OfficialOutputTextFileSourceSelectionResult SelectSource(
        OfficialOutputTextFileSourceSelectionRequest? request)
    {
        Result validation = OfficialOutputTextFileSourceSelectionRules.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return OfficialOutputTextFileSourceSelectionResult.Failure(validation.ErrorDetail);
        }

        OfficialOutputTextFileSourceSelectionRequest normalizedRequest =
            OfficialOutputTextFileSourceSelectionRules.NormalizeRequest(request!);

        OfficialOutputTextFileTrustedPackageProducerRequest producerRequest =
            OfficialOutputTextFileSourceSelectionRules.ToProducerRequest(normalizedRequest);

        return OfficialOutputTextFileSourceSelectionResult.Success(producerRequest);
    }
}

public static class OfficialOutputTextFileSourceSelectionRules
{
    public static Result ValidateRequest(OfficialOutputTextFileSourceSelectionRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.RequestRequired",
                "Official output text-file source selection request is required."));
        }

        Result sourceValidation =
            OfficialOutputSourceReadService.ValidateRequest(request.SourceReadRequest);

        if (sourceValidation.IsFailure)
        {
            return sourceValidation;
        }

        if (!request.RequestedAt.HasValue)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.RequestedAtRequired",
                "Official output text-file source selection requested timestamp is required."));
        }

        if (!request.RequestedByUserId.HasValue)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.RequestedByUserIdRequired",
                "Official output text-file source selection requester user id is required."));
        }

        if (request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.InvalidRequestedByUserId",
                "Official output text-file source selection requester user id is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.TerminalIdRequired",
                "Official output text-file source selection terminal id is required."));
        }

        if (request.Metadata is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.MetadataRequired",
                "Official output text-file source selection metadata is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialPrintableDocumentCopyMode), request.CopyMode))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceSelection.UnsupportedCopyMode",
                "Official output text-file source selection copy mode is unsupported."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static OfficialOutputTextFileSourceSelectionRequest NormalizeRequest(
        OfficialOutputTextFileSourceSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            SourceReadRequest = OfficialOutputSourceReadService.NormalizeRequest(request.SourceReadRequest!),
            TerminalId = Normalize(request.TerminalId),
            Metadata = NormalizeMetadata(request.Metadata!)
        };
    }

    public static OfficialOutputTextFileTrustedPackageProducerRequest ToProducerRequest(
        OfficialOutputTextFileSourceSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        OfficialOutputTextFileSourceSelectionRequest normalizedRequest =
            NormalizeRequest(request);

        return new OfficialOutputTextFileTrustedPackageProducerRequest(
            normalizedRequest.SourceReadRequest,
            normalizedRequest.RequestedAt,
            normalizedRequest.RequestedByUserId,
            normalizedRequest.TerminalId,
            normalizedRequest.Metadata,
            normalizedRequest.CopyMode);
    }

    private static Result ValidateMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string key = Normalize(pair.Key);

            if (key.Length == 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileSourceSelection.MetadataKeyRequired",
                    "Official output text-file source selection metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileSourceSelection.DuplicateMetadataKey",
                    "Official output text-file source selection metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return metadata
            .Select(static pair => new KeyValuePair<string, string>(
                Normalize(pair.Key),
                Normalize(pair.Value)))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

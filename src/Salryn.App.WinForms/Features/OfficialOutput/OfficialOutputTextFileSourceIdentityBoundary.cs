using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialOutputTextFileSourceIdentityListRequest(
    OfficialEvidenceReadRequest? EvidenceReadRequest);

public sealed record OfficialOutputTextFileSourceIdentityCandidate(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset? TransactionTimestamp,
    DateTimeOffset AppendedAt,
    string? TerminalId,
    string? RegistrationScopeCode,
    string SourceReference,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool SnapshotHashMatches);

public sealed record OfficialOutputTextFileSourceIdentitySelectionRequest(
    OfficialOutputTextFileSourceIdentityCandidate? Candidate,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata,
    OfficialPrintableDocumentCopyMode CopyMode = OfficialPrintableDocumentCopyMode.Original);

public interface IOfficialOutputTextFileSourceIdentityBoundary
{
    Task<Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>>> ListCandidatesAsync(
        OfficialOutputTextFileSourceIdentityListRequest? request,
        CancellationToken cancellationToken = default);

    Result<OfficialOutputTextFileSourceSelectionRequest> BuildSourceSelectionRequest(
        OfficialOutputTextFileSourceIdentitySelectionRequest? request);
}

public sealed class OfficialOutputTextFileSourceIdentityBoundary : IOfficialOutputTextFileSourceIdentityBoundary
{
    private readonly IOfficialEvidenceReadService _officialEvidenceRead;

    public OfficialOutputTextFileSourceIdentityBoundary(
        IOfficialEvidenceReadService officialEvidenceRead)
    {
        _officialEvidenceRead = officialEvidenceRead ?? throw new ArgumentNullException(nameof(officialEvidenceRead));
    }

    public async Task<Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>>> ListCandidatesAsync(
        OfficialOutputTextFileSourceIdentityListRequest? request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = OfficialOutputTextFileSourceIdentityRules.ValidateListRequest(request);

        if (validation.IsFailure)
        {
            return Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>>.Fail(validation.ErrorDetail);
        }

        Result<OfficialEvidenceExportResult> evidenceResult =
            await _officialEvidenceRead.ExportAsync(request!.EvidenceReadRequest!, cancellationToken).ConfigureAwait(false);

        if (evidenceResult.IsFailure)
        {
            return Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>>.Fail(evidenceResult.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate> candidates =
            evidenceResult.Value.Entries
                .Select(entry => OfficialOutputTextFileSourceIdentityRules.FromEvidenceEntry(
                    entry,
                    evidenceResult.Value.Manifest.RegistrationScopeCode))
                .ToArray();

        return Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>>.Success(candidates);
    }

    public Result<OfficialOutputTextFileSourceSelectionRequest> BuildSourceSelectionRequest(
        OfficialOutputTextFileSourceIdentitySelectionRequest? request)
    {
        Result validation = OfficialOutputTextFileSourceIdentityRules.ValidateSelectionRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputTextFileSourceSelectionRequest>.Fail(validation.ErrorDetail);
        }

        OfficialOutputTextFileSourceIdentityCandidate candidate = request!.Candidate!;

        Result<IReadOnlyDictionary<string, string>> metadataResult =
            OfficialOutputTextFileSourceIdentityRules.BuildMetadata(candidate, request.Metadata!);

        if (metadataResult.IsFailure)
        {
            return Result<OfficialOutputTextFileSourceSelectionRequest>.Fail(metadataResult.ErrorDetail);
        }

        OfficialOutputTextFileSourceSelectionRequest selectionRequest = new(
            SourceReadRequest: new OfficialOutputSourceReadRequest(
                JournalEntryId: candidate.JournalEntryId,
                OfficialDocumentNumber: candidate.OfficialDocumentNumber,
                DocumentKind: candidate.DocumentKind,
                BirMachineProfileId: candidate.BirMachineProfileId,
                RegistrationScopeCode: candidate.RegistrationScopeCode),
            RequestedAt: request.RequestedAt,
            RequestedByUserId: request.RequestedByUserId,
            TerminalId: request.TerminalId,
            Metadata: metadataResult.Value,
            CopyMode: request.CopyMode);

        Result selectionValidation =
            OfficialOutputTextFileSourceSelectionRules.ValidateRequest(selectionRequest);

        if (selectionValidation.IsFailure)
        {
            return Result<OfficialOutputTextFileSourceSelectionRequest>.Fail(selectionValidation.ErrorDetail);
        }

        return Result<OfficialOutputTextFileSourceSelectionRequest>.Success(
            OfficialOutputTextFileSourceSelectionRules.NormalizeRequest(selectionRequest));
    }
}

public static class OfficialOutputTextFileSourceIdentityRules
{
    public static Result ValidateListRequest(
        OfficialOutputTextFileSourceIdentityListRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.RequestRequired",
                "Official output text-file source identity list request is required."));
        }

        return OfficialEvidenceExportBuilder.ValidateRequest(request.EvidenceReadRequest);
    }

    public static Result ValidateSelectionRequest(
        OfficialOutputTextFileSourceIdentitySelectionRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.SelectionRequestRequired",
                "Official output text-file source identity selection request is required."));
        }

        Result candidateValidation = ValidateCandidate(request.Candidate);

        if (candidateValidation.IsFailure)
        {
            return candidateValidation;
        }

        if (!request.RequestedAt.HasValue)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.RequestedAtRequired",
                "Official output text-file source identity selection requested timestamp is required."));
        }

        if (!request.RequestedByUserId.HasValue)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.RequestedByUserIdRequired",
                "Official output text-file source identity selection requester user id is required."));
        }

        if (request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.InvalidRequestedByUserId",
                "Official output text-file source identity selection requester user id is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.TerminalIdRequired",
                "Official output text-file source identity selection terminal id is required."));
        }

        if (request.Metadata is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.MetadataRequired",
                "Official output text-file source identity selection metadata is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialPrintableDocumentCopyMode), request.CopyMode))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.UnsupportedCopyMode",
                "Official output text-file source identity selection copy mode is unsupported."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static OfficialOutputTextFileSourceIdentityCandidate FromEvidenceEntry(
        OfficialEvidenceEntryReadModel entry,
        string? registrationScopeCode)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OfficialOutputTextFileSourceIdentityCandidate(
            JournalEntryId: entry.JournalEntryId,
            SaleId: entry.SaleId,
            BirMachineProfileId: entry.BirMachineProfileId,
            DocumentKind: entry.DocumentKind,
            OfficialDocumentNumber: NormalizeRequired(entry.OfficialDocumentNumber),
            TransactionTimestamp: null,
            AppendedAt: entry.AppendedAt,
            TerminalId: NormalizeOptional(entry.TerminalId),
            RegistrationScopeCode: NormalizeOptional(registrationScopeCode),
            SourceReference: "journal:" + entry.JournalEntryId.ToString("D"),
            SnapshotPayloadHash: NormalizeRequired(entry.SnapshotPayloadHash),
            SnapshotHashAlgorithm: NormalizeRequired(entry.SnapshotHashAlgorithm),
            SnapshotHashMatches: entry.SnapshotHashMatches);
    }

    public static Result<IReadOnlyDictionary<string, string>> BuildMetadata(
        OfficialOutputTextFileSourceIdentityCandidate candidate,
        IReadOnlyDictionary<string, string> metadata)
    {
        Result candidateValidation = ValidateCandidate(candidate);

        if (candidateValidation.IsFailure)
        {
            return Result<IReadOnlyDictionary<string, string>>.Fail(candidateValidation.ErrorDetail);
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return Result<IReadOnlyDictionary<string, string>>.Fail(metadataValidation.ErrorDetail);
        }

        Dictionary<string, string> normalized = NormalizeMetadata(metadata);

        normalized["sourceIdentity"] = "OfficialEvidenceRead";
        normalized["selectedJournalEntryId"] = candidate.JournalEntryId.ToString("D");
        normalized["selectedSaleId"] = candidate.SaleId.ToString("D");
        normalized["selectedBirMachineProfileId"] = candidate.BirMachineProfileId.ToString("D");
        normalized["selectedDocumentKind"] = candidate.DocumentKind.ToString();
        normalized["selectedOfficialDocumentNumber"] = candidate.OfficialDocumentNumber;
        normalized["selectedSourceReference"] = candidate.SourceReference;

        string? registrationScopeCode = NormalizeOptional(candidate.RegistrationScopeCode);

        if (registrationScopeCode is not null)
        {
            normalized["selectedRegistrationScopeCode"] = registrationScopeCode;
        }

        return Result<IReadOnlyDictionary<string, string>>.Success(
            normalized
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value,
                    StringComparer.Ordinal));
    }

    private static Result ValidateCandidate(
        OfficialOutputTextFileSourceIdentityCandidate? candidate)
    {
        if (candidate is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.CandidateRequired",
                "Official output text-file source identity candidate is required."));
        }

        if (candidate.JournalEntryId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.InvalidJournalEntryId",
                "Official output text-file source identity candidate journal entry id is invalid."));
        }

        if (candidate.SaleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.InvalidSaleId",
                "Official output text-file source identity candidate sale id is invalid."));
        }

        if (candidate.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.InvalidMachineProfileId",
                "Official output text-file source identity candidate machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(ElectronicJournalDocumentKind), candidate.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.UnsupportedDocumentKind",
                "Official output text-file source identity candidate document kind is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(candidate.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.OfficialDocumentNumberRequired",
                "Official output text-file source identity candidate official document number is required."));
        }

        if (!candidate.SnapshotHashMatches)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.SnapshotHashMismatch",
                "Official output text-file source identity candidate snapshot hash must match official evidence."));
        }

        if (string.IsNullOrWhiteSpace(candidate.SnapshotPayloadHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.SnapshotPayloadHashRequired",
                "Official output text-file source identity candidate snapshot payload hash is required."));
        }

        if (string.IsNullOrWhiteSpace(candidate.SnapshotHashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileSourceIdentity.SnapshotHashAlgorithmRequired",
                "Official output text-file source identity candidate snapshot hash algorithm is required."));
        }

        return Result.Success();
    }

    private static Result ValidateMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string key = Normalize(pair.Key);

            if (key.Length == 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileSourceIdentity.MetadataKeyRequired",
                    "Official output text-file source identity metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileSourceIdentity.DuplicateMetadataKey",
                    "Official output text-file source identity metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    private static Dictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
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

    private static string NormalizeRequired(string? value)
    {
        return Normalize(value);
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = Normalize(value);

        return normalized.Length == 0 ? null : normalized;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

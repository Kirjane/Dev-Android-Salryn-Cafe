using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialEvidenceReadRequest(
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind);

public sealed record OfficialEvidenceEntryReadModel(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    Guid OfficialDocumentSequenceId,
    string SnapshotPayloadJson,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool SnapshotHashMatches,
    string RecomputedSnapshotPayloadHash,
    Guid OriginalEventId,
    DateTimeOffset AppendedAt,
    Guid CashierUserId,
    string? TerminalId);

public sealed record OfficialEvidenceExportManifest(
    DateTimeOffset ExportGeneratedAt,
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    int EntryCount,
    string ManifestHash,
    string HashAlgorithm,
    bool AllSnapshotHashesMatch);

public sealed record OfficialEvidenceExportResult(
    OfficialEvidenceExportManifest Manifest,
    IReadOnlyList<OfficialEvidenceEntryReadModel> Entries);

public interface IOfficialEvidenceReadService
{
    Task<Result<OfficialEvidenceExportResult>> ExportAsync(
        OfficialEvidenceReadRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - official output must later read journal snapshot evidence, never UI receipt text or sales.receipt_snapshot.
public sealed class OfficialEvidenceExportBuilder
{
    public Result<OfficialEvidenceExportResult> Build(
        OfficialEvidenceReadRequest request,
        IReadOnlyList<OfficialEvidenceEntryReadModel> entries,
        DateTimeOffset exportGeneratedAt)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialEvidenceExportResult>.Fail(validation.ErrorDetail);
        }

        if (entries is null)
        {
            return Result<OfficialEvidenceExportResult>.Fail(Error.Validation(
                "OfficialEvidence.EntriesRequired",
                "Official evidence export requires an entry collection."));
        }

        IReadOnlyList<OfficialEvidenceEntryReadModel> normalizedEntries =
            entries
                .Select(VerifySnapshotHash)
                .OrderBy(entry => entry.AppendedAt)
                .ThenBy(entry => entry.OfficialDocumentNumber, StringComparer.Ordinal)
                .ThenBy(entry => entry.JournalEntryId)
                .ToList();

        bool allSnapshotHashesMatch = normalizedEntries.All(entry => entry.SnapshotHashMatches);
        string manifestHash = ComputeManifestHash(request, normalizedEntries);

        OfficialEvidenceExportManifest manifest = new(
            ExportGeneratedAt: exportGeneratedAt,
            RangeStartedAt: request.RangeStartedAt,
            RangeEndedAt: request.RangeEndedAt,
            BirMachineProfileId: request.BirMachineProfileId,
            RegistrationScopeCode: NormalizeOptional(request.RegistrationScopeCode),
            DocumentKind: request.DocumentKind,
            EntryCount: normalizedEntries.Count,
            ManifestHash: manifestHash,
            HashAlgorithm: "SHA-256",
            AllSnapshotHashesMatch: allSnapshotHashesMatch);

        return Result<OfficialEvidenceExportResult>.Success(
            new OfficialEvidenceExportResult(manifest, normalizedEntries));
    }

    public static Result ValidateRequest(OfficialEvidenceReadRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialEvidence.RequestRequired",
                "Official evidence read request is required."));
        }

        if (request.RangeStartedAt == default ||
            request.RangeEndedAt == default ||
            request.RangeEndedAt <= request.RangeStartedAt)
        {
            return Result.Fail(Error.Validation(
                "OfficialEvidence.InvalidDateRange",
                "Official evidence read date range is invalid."));
        }

        if (!request.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialEvidence.MachineProfileOrScopeRequired",
                "Official evidence read requires a BIR machine profile id or registration scope."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialEvidence.InvalidMachineProfileId",
                "Official evidence read machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialEvidence.UnsupportedDocumentKind",
                "Official evidence read document kind is unsupported."));
        }

        return Result.Success();
    }

    private static OfficialEvidenceEntryReadModel VerifySnapshotHash(
        OfficialEvidenceEntryReadModel entry)
    {
        string recomputedHash = ComputeSha256Hex(entry.SnapshotPayloadJson);
        bool matches =
            string.Equals(entry.SnapshotHashAlgorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.SnapshotPayloadHash, recomputedHash, StringComparison.OrdinalIgnoreCase);

        return entry with
        {
            SnapshotHashMatches = matches,
            RecomputedSnapshotPayloadHash = recomputedHash
        };
    }

    private static string ComputeManifestHash(
        OfficialEvidenceReadRequest request,
        IReadOnlyList<OfficialEvidenceEntryReadModel> entries)
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", "SALRYN_OFFICIAL_EVIDENCE_EXPORT_MANIFEST_V1");
        writer.WriteString("rangeStartedAt", request.RangeStartedAt.ToString("O"));
        writer.WriteString("rangeEndedAt", request.RangeEndedAt.ToString("O"));
        writer.WriteString("birMachineProfileId", request.BirMachineProfileId?.ToString("D"));
        writer.WriteString("registrationScopeCode", NormalizeOptional(request.RegistrationScopeCode));
        writer.WriteString("documentKind", request.DocumentKind.ToString());
        writer.WriteNumber("entryCount", entries.Count);

        writer.WritePropertyName("entries");
        writer.WriteStartArray();

        foreach (OfficialEvidenceEntryReadModel entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("journalEntryId", entry.JournalEntryId.ToString("D"));
            writer.WriteString("saleId", entry.SaleId.ToString("D"));
            writer.WriteString("birMachineProfileId", entry.BirMachineProfileId.ToString("D"));
            writer.WriteString("documentKind", entry.DocumentKind.ToString());
            writer.WriteString("officialDocumentNumber", entry.OfficialDocumentNumber);
            writer.WriteString("officialDocumentSequenceId", entry.OfficialDocumentSequenceId.ToString("D"));
            writer.WriteString("snapshotPayloadHash", entry.SnapshotPayloadHash);
            writer.WriteString("snapshotHashAlgorithm", entry.SnapshotHashAlgorithm);
            writer.WriteString("recomputedSnapshotPayloadHash", entry.RecomputedSnapshotPayloadHash);
            writer.WriteBoolean("snapshotHashMatches", entry.SnapshotHashMatches);
            writer.WriteString("originalEventId", entry.OriginalEventId.ToString("D"));
            writer.WriteString("appendedAt", entry.AppendedAt.ToString("O"));
            writer.WriteString("cashierUserId", entry.CashierUserId.ToString("D"));
            writer.WriteString("terminalId", NormalizeOptional(entry.TerminalId));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return ComputeSha256Hex(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string ComputeSha256Hex(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

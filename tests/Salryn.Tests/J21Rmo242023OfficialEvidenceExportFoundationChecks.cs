using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialEvidenceExportFoundationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialEvidenceReadService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialEvidenceReadService.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string infrastructureSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialCheckoutCompletionTrustSmokeChecks.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(coreText, "IOfficialEvidenceReadService", "Official evidence foundation must expose a Core read service contract.");
        RequireContains(coreText, "OfficialEvidenceExportManifest", "Official evidence foundation must expose an export manifest.");
        RequireContains(coreText, "SnapshotHashMatches", "Official evidence read model must expose snapshot hash verification status.");
        RequireContains(coreText, "never UI receipt text or sales.receipt_snapshot", "Official evidence contract must document the source-of-truth rule.");
        RequireContains(sqliteText, "ORDER BY", "SQLite official evidence reader must use deterministic ordering.");
        RequireContains(sqliteText, "e.created_at ASC", "SQLite official evidence reader must order by appended timestamp.");
        RequireContains(sqliteText, "e.official_document_number ASC", "SQLite official evidence reader must order by official document number.");
        RequireContains(sqliteText, "e.id ASC", "SQLite official evidence reader must order by journal entry id.");
        RequireNotContains(sqliteText, "receipt_snapshot", "SQLite official evidence reader must not read sales.receipt_snapshot.");
        RequireContains(appServicesText, "public IOfficialEvidenceReadService OfficialEvidenceRead { get; }", "AppServices must expose official evidence read through the Core contract.");
        RequireContains(appServicesText, "new SqliteOfficialEvidenceReadService", "AppServices must compose the SQLite official evidence reader.");
        RequireContains(infrastructureSmokeText, "Spoofed UI receipt snapshot", "Infrastructure smoke must prove UI receipt preview text is not evidence source.");
        RequireContains(infrastructureSmokeText, "AllSnapshotHashesMatch", "Infrastructure smoke must verify snapshot hashes through the export manifest.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialEvidenceExportFoundationChecks.Run();", "CoreSmoke must run official evidence export foundation checks.");

        VerifyBuilderValidationAndHashMismatch();
    }

    private static void VerifyBuilderValidationAndHashMismatch()
    {
        OfficialEvidenceExportBuilder builder = new();
        Guid machineProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        OfficialEvidenceReadRequest validRequest = new(
            RangeStartedAt: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            RangeEndedAt: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            BirMachineProfileId: machineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt);

        ExpectFailure(
            builder.Build(validRequest with
            {
                RangeStartedAt = validRequest.RangeEndedAt,
                RangeEndedAt = validRequest.RangeStartedAt
            }, Array.Empty<OfficialEvidenceEntryReadModel>(), DateTimeOffset.UtcNow),
            "OfficialEvidence.InvalidDateRange");

        ExpectFailure(
            builder.Build(validRequest with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }, Array.Empty<OfficialEvidenceEntryReadModel>(), DateTimeOffset.UtcNow),
            "OfficialEvidence.MachineProfileOrScopeRequired");

        ExpectFailure(
            builder.Build(validRequest with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }, Array.Empty<OfficialEvidenceEntryReadModel>(), DateTimeOffset.UtcNow),
            "OfficialEvidence.UnsupportedDocumentKind");

        OfficialEvidenceEntryReadModel mismatchedEntry = new(
            JournalEntryId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SaleId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            BirMachineProfileId: machineProfileId,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            OfficialDocumentNumber: "OR-2026-000001",
            OfficialDocumentSequenceId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            SnapshotPayloadJson: "{\"schema\":\"test\"}",
            SnapshotPayloadHash: "not-a-real-hash",
            SnapshotHashAlgorithm: "SHA-256",
            SnapshotHashMatches: false,
            RecomputedSnapshotPayloadHash: string.Empty,
            OriginalEventId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            AppendedAt: new DateTimeOffset(2026, 6, 25, 8, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            TerminalId: "POS-1");

        Result<OfficialEvidenceExportResult> first =
            builder.Build(validRequest, new[] { mismatchedEntry }, new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));

        Result<OfficialEvidenceExportResult> second =
            builder.Build(validRequest, new[] { mismatchedEntry }, new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero));

        RequireSuccess(first, "Official evidence mismatch export should still build with a deterministic flag.");
        RequireSuccess(second, "Official evidence mismatch export should be deterministic.");
        RequireFalse(first.Value.Manifest.AllSnapshotHashesMatch, "Manifest must flag mismatched snapshot hashes.");
        RequireFalse(first.Value.Entries[0].SnapshotHashMatches, "Entry must flag mismatched snapshot hash.");
        RequireEqual(first.Value.Manifest.ManifestHash, second.Value.Manifest.ManifestHash, "Manifest hash must not depend on generated timestamp.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void ExpectFailure(Result<OfficialEvidenceExportResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected official evidence validation code.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}

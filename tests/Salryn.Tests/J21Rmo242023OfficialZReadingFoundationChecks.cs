using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialZReadingFoundationChecks
{
    private static readonly Guid MachineProfileId = Guid.Parse("23111111-1111-1111-1111-111111111111");
    private static readonly Guid ZReadingId = Guid.Parse("23111111-1111-1111-1111-111111111112");
    private static readonly Guid CashierUserId = Guid.Parse("23111111-1111-1111-1111-111111111113");
    private static readonly DateTimeOffset RangeStartedAt = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEndedAt = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinalizedAt = new(2026, 6, 26, 0, 5, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string zReadingText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialZReadingService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialZReadingService.cs");
        string migrationText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "BirZReadingSchemaMigration.cs");
        string initializationText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infrastructureSmokeProgramText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string infrastructureSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialZReadingSmokeChecks.cs");

        RequireContains(zReadingText, "IOfficialZReadingService", "Z-reading foundation must expose a Core service contract.");
        RequireContains(zReadingText, "IOfficialZReadingPersistence", "Z-reading foundation must keep persistence behind a Core port.");
        RequireContains(zReadingText, "IOfficialXReadingService", "Z-reading must reuse X-reading as the computation source.");
        RequireContains(zReadingText, "OfficialZReading.EvidenceHashMismatch", "Z-reading must fail closed on evidence hash mismatch.");
        RequireContains(zReadingText, "Z-reading finalizes a read-only X-reading snapshot", "Z-reading must document the no-source-mutation rule.");
        RequireNotContains(zReadingText, "receipt_snapshot", "Z-reading must not read sales.receipt_snapshot.");
        RequireNotContains(zReadingText, "LocalHostCompleteSale", "Z-reading must not wire Local Host complete-sale.");
        RequireNotContains(zReadingText, "Crystal", "Z-reading must not wire report rendering.");
        RequireNotContains(zReadingText, "ESC/POS", "Z-reading must not wire printer output.");

        RequireContains(sqliteText, "OfficialZReading.DuplicateRange", "SQLite Z-reading persistence must reject duplicate exact ranges.");
        RequireContains(sqliteText, "OfficialZReading.OverlappingRange", "SQLite Z-reading persistence must reject overlapping finalized ranges.");
        RequireContains(sqliteText, "RangesOverlapHalfOpen", "SQLite Z-reading persistence must use half-open overlap semantics.");
        RequireContains(sqliteText, "existingStart.UtcDateTime < newEnd.UtcDateTime", "Half-open overlap must allow adjacent range boundaries.");
        RequireContains(sqliteText, "InsertZReading", "SQLite Z-reading persistence must insert only the final Z-reading proof.");
        RequireContains(sqliteText, "original journal evidence remains append-only", "SQLite Z-reading persistence must document append-only source evidence.");
        RequireNotContains(sqliteText, "receipt_snapshot", "SQLite Z-reading persistence must not read sales.receipt_snapshot.");
        RequireNotContains(sqliteText, "LocalHostCompleteSale", "SQLite Z-reading persistence must not wire Local Host complete-sale.");
        RequireNotContains(sqliteText, "Crystal", "SQLite Z-reading persistence must not wire report rendering.");
        RequireNotContains(sqliteText, "ESC/POS", "SQLite Z-reading persistence must not wire printer output.");

        RequireContains(migrationText, "CREATE TABLE bir_z_readings", "Z-reading migration must create a persistence table.");
        RequireContains(migrationText, "idx_bir_z_readings_unique_finalized_range", "Z-reading schema must protect duplicate exact ranges.");
        RequireContains(migrationText, "idx_bir_z_readings_overlap_lookup", "Z-reading schema must support overlap checks.");
        RequireContains(initializationText, "new BirZReadingSchemaMigration()", "Database initialization must register the Z-reading migration.");
        RequireContains(appServicesText, "public IOfficialZReadingService OfficialZReading { get; }", "AppServices must expose Z-reading through the Core contract.");
        RequireContains(appServicesText, "new OfficialZReadingService(", "AppServices must compose Z-reading over X-reading.");
        RequireContains(appServicesText, "new SqliteOfficialZReadingService(connectionFactory)", "AppServices must compose SQLite Z-reading persistence.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialZReadingFoundationChecks.Run();", "CoreSmoke must run Z-reading foundation checks.");
        RequireContains(infrastructureSmokeProgramText, "OfficialZReadingSmokeChecks.Run(projectRoot);", "Infrastructure smoke command must run Z-reading proof checks.");
        RequireContains(infrastructureSmokeText, "OfficialZReading.DuplicateRange", "Infrastructure smoke must prove duplicate rejection.");
        RequireContains(infrastructureSmokeText, "OfficialZReading.OverlappingRange", "Infrastructure smoke must prove overlap rejection.");
        RequireContains(infrastructureSmokeText, "OfficialZReading.AdjacentSecondDay", "Infrastructure smoke must prove adjacent daily Z-readings are allowed.");
        RequireContains(infrastructureSmokeText, "Boundary timestamp evidence must belong to the second half-open range", "Infrastructure smoke must prove boundary evidence belongs to the second range.");
        RequireContains(infrastructureSmokeText, "OfficialZReading.EvidenceHashMismatch", "Infrastructure smoke must prove hash mismatch blocks finalization.");
        RequireContains(infrastructureSmokeText, "Empty Z-reading", "Infrastructure smoke must document empty range behavior.");

        VerifyRuntimeFinalizationAndGuards();
    }

    private static void VerifyRuntimeFinalizationAndGuards()
    {
        OfficialXReadingResult xReading = BuildXReading(
            entryCount: 2,
            documentRange: new OfficialXReadingDocumentRange("OR-2026-000001", "OR-2026-000002"),
            allSnapshotHashesMatch: true);
        RecordingZReadingPersistence persistence = new();
        OfficialZReadingService service = new(
            new FakeOfficialXReadingService(Result<OfficialXReadingResult>.Success(xReading)),
            persistence,
            () => ZReadingId,
            () => FinalizedAt);

        Result<OfficialZReadingResult> result =
            service.FinalizeAsync(CreateRequest()).GetAwaiter().GetResult();

        RequireSuccess(result, "Z-reading runtime finalization should succeed.");
        RequireTrue(persistence.SaveCalled, "Z-reading must call persistence after X-reading succeeds.");
        RequireEqual(ZReadingId, result.Value.Summary.ZReadingId, "Z-reading id must come from the service id factory.");
        RequireEqual(FinalizedAt, result.Value.Summary.FinalizedAt, "Z-reading finalized timestamp must come from the service clock.");
        RequireEqual(2, result.Value.Summary.EntryCount, "Z-reading must preserve X-reading entry count.");
        RequireEqual("OR-2026-000001", result.Value.Summary.FirstOfficialDocumentNumber, "Z-reading must preserve first official document number.");
        RequireEqual("OR-2026-000002", result.Value.Summary.LastOfficialDocumentNumber, "Z-reading must preserve last official document number.");
        RequireEqual(3070L, result.Value.Summary.TotalSalesCents, "Z-reading must preserve evidence-derived totals.");
        RequireEqual("manifest-hash-2", result.Value.Summary.EvidenceManifestHash, "Z-reading must preserve the evidence manifest hash.");
        RequireEqual(CashierUserId, persistence.LastRequest?.Summary.CreatedByUserId, "Z-reading must persist the requested user id when supplied.");
        RequireEqual("POS-Z-READING", persistence.LastRequest?.Summary.TerminalId, "Z-reading must persist the requested terminal id when supplied.");

        RecordingZReadingPersistence emptyPersistence = new();
        Result<OfficialZReadingResult> emptyResult =
            new OfficialZReadingService(
                new FakeOfficialXReadingService(Result<OfficialXReadingResult>.Success(BuildXReading(
                    entryCount: 0,
                    documentRange: null,
                    allSnapshotHashesMatch: true))),
                emptyPersistence,
                () => ZReadingId,
                () => FinalizedAt)
            .FinalizeAsync(CreateRequest()).GetAwaiter().GetResult();

        RequireSuccess(emptyResult, "Empty Z-reading must be explicitly allowed for no-sales finalization.");
        RequireTrue(emptyPersistence.SaveCalled, "Empty Z-reading must still persist a finalization record.");
        RequireEqual(0, emptyResult.Value.Summary.EntryCount, "Empty Z-reading must preserve zero entry count.");
        RequireEqual(null, emptyResult.Value.Summary.FirstOfficialDocumentNumber, "Empty Z-reading must not invent a first document number.");
        RequireEqual(null, emptyResult.Value.Summary.LastOfficialDocumentNumber, "Empty Z-reading must not invent a last document number.");
        RequireEqual(0L, emptyResult.Value.Summary.TotalSalesCents, "Empty Z-reading must not invent totals.");

        ExpectFailure(
            new OfficialZReadingService(
                new FakeOfficialXReadingService(Result<OfficialXReadingResult>.Fail(Error.Failure(
                    "OfficialXReading.EvidenceHashMismatch",
                    "hash mismatch"))),
                new RecordingZReadingPersistence())
            .FinalizeAsync(CreateRequest()).GetAwaiter().GetResult(),
            "OfficialZReading.EvidenceHashMismatch");

        ExpectFailure(
            new OfficialZReadingService(
                new FakeOfficialXReadingService(Result<OfficialXReadingResult>.Success(BuildXReading(
                    entryCount: 1,
                    documentRange: new OfficialXReadingDocumentRange("OR-2026-000001", "OR-2026-000001"),
                    allSnapshotHashesMatch: false))),
                new RecordingZReadingPersistence())
            .FinalizeAsync(CreateRequest()).GetAwaiter().GetResult(),
            "OfficialZReading.EvidenceHashMismatch");

        ExpectFailure(
            new OfficialZReadingService(
                new FakeOfficialXReadingService(Result<OfficialXReadingResult>.Success(xReading)),
                new RecordingZReadingPersistence(Error.Conflict(
                    "OfficialZReading.DuplicateRange",
                    "duplicate")))
            .FinalizeAsync(CreateRequest()).GetAwaiter().GetResult(),
            "OfficialZReading.DuplicateRange");

        ExpectFailure(
            service.FinalizeAsync(CreateRequest() with
            {
                RangeStartedAt = RangeEndedAt,
                RangeEndedAt = RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialZReading.InvalidDateRange");

        ExpectFailure(
            service.FinalizeAsync(CreateRequest() with
            {
                RangeEndedAt = RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialZReading.InvalidDateRange");

        ExpectFailure(
            service.FinalizeAsync(CreateRequest() with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialZReading.MachineProfileOrScopeRequired");

        ExpectFailure(
            service.FinalizeAsync(CreateRequest() with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }).GetAwaiter().GetResult(),
            "OfficialZReading.UnsupportedDocumentKind");
    }

    private static OfficialZReadingRequest CreateRequest()
    {
        return new OfficialZReadingRequest(
            RangeStartedAt,
            RangeEndedAt,
            MachineProfileId,
            RegistrationScopeCode: null,
            ElectronicJournalDocumentKind.OfficialReceipt,
            CashierUserId,
            "POS-Z-READING");
    }

    private static OfficialXReadingResult BuildXReading(
        int entryCount,
        OfficialXReadingDocumentRange? documentRange,
        bool allSnapshotHashesMatch)
    {
        OfficialXReadingSummary summary = new(
            RangeStartedAt,
            RangeEndedAt,
            MachineProfileId,
            RegistrationScopeCode: null,
            ElectronicJournalDocumentKind.OfficialReceipt,
            entryCount,
            GrossSalesCents: entryCount == 0 ? 0 : 3150,
            SubtotalCents: entryCount == 0 ? 0 : 3000,
            DiscountTotalCents: entryCount == 0 ? 0 : 150,
            TaxTotalCents: entryCount == 0 ? 0 : 120,
            NetSalesCents: entryCount == 0 ? 0 : 2950,
            TotalSalesCents: entryCount == 0 ? 0 : 3070,
            EvidenceManifestHash: "manifest-hash-" + entryCount,
            allSnapshotHashesMatch);

        IReadOnlyList<OfficialXReadingPaymentSummary> payments = entryCount == 0
            ? Array.Empty<OfficialXReadingPaymentSummary>()
            : new[] { new OfficialXReadingPaymentSummary("Cash", 3070, 2) };
        IReadOnlyList<OfficialXReadingTaxModeSummary> taxModes = entryCount == 0
            ? Array.Empty<OfficialXReadingTaxModeSummary>()
            : new[]
            {
                new OfficialXReadingTaxModeSummary(
                    "VAT_DEFERRED",
                    entryCount,
                    3000,
                    150,
                    120,
                    3070,
                    VatBucketEngineDeferred: true,
                    Notes: "Detailed VAT bucket classification deferred.")
            };

        return new OfficialXReadingResult(summary, documentRange, payments, taxModes);
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

    private static void ExpectFailure(Result<OfficialZReadingResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected Z-reading failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected Z-reading validation code.");
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

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
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

    private sealed class FakeOfficialXReadingService : IOfficialXReadingService
    {
        private readonly Result<OfficialXReadingResult> _result;

        public FakeOfficialXReadingService(Result<OfficialXReadingResult> result)
        {
            _result = result;
        }

        public Task<Result<OfficialXReadingResult>> ReadAsync(
            OfficialXReadingRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingZReadingPersistence : IOfficialZReadingPersistence
    {
        private readonly Error? _failure;

        public RecordingZReadingPersistence(Error? failure = null)
        {
            _failure = failure;
        }

        public bool SaveCalled { get; private set; }

        public OfficialZReadingPersistenceRequest? LastRequest { get; private set; }

        public Task<Result<OfficialZReadingPersistenceResult>> SaveAsync(
            OfficialZReadingPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalled = true;
            LastRequest = request;

            return _failure is null
                ? Task.FromResult(Result<OfficialZReadingPersistenceResult>.Success(
                    new OfficialZReadingPersistenceResult(request.Summary.ZReadingId)))
                : Task.FromResult(Result<OfficialZReadingPersistenceResult>.Fail(_failure));
        }
    }
}

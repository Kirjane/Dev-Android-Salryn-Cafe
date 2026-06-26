using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialOutputSourceContractChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("25222222-2222-2222-2222-222222222201");
    private static readonly Guid SaleId = Guid.Parse("25222222-2222-2222-2222-222222222202");
    private static readonly Guid MachineProfileId = Guid.Parse("25222222-2222-2222-2222-222222222203");
    private static readonly Guid SequenceId = Guid.Parse("25222222-2222-2222-2222-222222222204");
    private static readonly Guid OriginalEventId = Guid.Parse("25222222-2222-2222-2222-222222222205");
    private static readonly Guid CashierUserId = Guid.Parse("25222222-2222-2222-2222-222222222206");
    private static readonly DateTimeOffset TransactionTimestamp = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputSourceReadService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialOutputSourceReadService.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infrastructureSmokeProgramText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        RequireContains(coreText, "IOfficialOutputSourceReadService", "Output source must expose a Core read service contract.");
        RequireContains(coreText, "OfficialOutputDocumentHeader", "Output source must expose document header data.");
        RequireContains(coreText, "OfficialOutputLineItem", "Output source must expose line item data.");
        RequireContains(coreText, "OfficialOutputPayment", "Output source must expose payment data.");
        RequireContains(coreText, "OfficialOutputTotals", "Output source must expose totals data.");
        RequireContains(coreText, "OfficialOutputMachineProfile", "Output source must expose persisted machine profile facts.");
        RequireContains(coreText, "OfficialOutputEvidence", "Output source must expose snapshot evidence metadata.");
        RequireContains(coreText, "OfficialOutputLifecycle", "Output source must expose lifecycle state.");
        RequireContains(coreText, "SnapshotHashMismatch", "Output source must fail closed when snapshot hash evidence is corrupted.");
        RequireContains(coreText, "OfficialDocumentLifecycleResult", "Output source must consume lifecycle state.");
        RequireNotContains(coreText, "receipt_snapshot", "Output source contract must not use saved sale receipt text.");
        RequireNotContains(coreText, "Crystal", "Output source contract must not wire report rendering.");
        RequireNotContains(coreText, "ESC/POS", "Output source contract must not wire printer output.");
        RequireNotContains(coreText, "LocalHostCompleteSale", "Output source contract must not wire Local Host complete-sale.");

        RequireContains(sqliteText, "bir_electronic_journal_entries", "SQLite output source must read journal entries.");
        RequireContains(sqliteText, "snapshot_payload_json", "SQLite output source must read snapshot payload JSON.");
        RequireContains(sqliteText, "snapshot_payload_hash", "SQLite output source must read snapshot payload hash.");
        RequireContains(sqliteText, "bir_electronic_journal_entry_events", "SQLite output source must require ORIGINAL event evidence.");
        RequireContains(sqliteText, "bir_machine_profiles", "SQLite output source must read persisted machine profile facts.");
        RequireContains(sqliteText, "OfficialDocumentLifecycleReadRequest", "SQLite output source must call the lifecycle read model.");
        RequireNotContains(sqliteText, "INSERT INTO", "SQLite output source must not insert rows.");
        RequireNotContains(sqliteText, "UPDATE ", "SQLite output source must not update rows.");
        RequireNotContains(sqliteText, "DELETE ", "SQLite output source must not delete rows.");
        RequireNotContains(sqliteText, "receipt_snapshot", "SQLite output source must not use saved sale receipt text.");

        RequireContains(appServicesText, "public IOfficialOutputSourceReadService OfficialOutputSourceRead { get; }", "AppServices must expose output source read service.");
        RequireContains(appServicesText, "new SqliteOfficialOutputSourceReadService", "AppServices must compose SQLite output source read service.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialOutputSourceContractChecks.Run();", "CoreSmoke must run output source contract checks.");
        RequireContains(infrastructureSmokeProgramText, "OfficialOutputSourceSmokeChecks.Run(projectRoot);", "Infrastructure smoke command must run output source proof checks.");

        VerifyRuntimeContractBehavior();
    }

    private static void VerifyRuntimeContractBehavior()
    {
        OfficialOutputSourceReadRequest validRequest = new(
            JournalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null);

        RequireSuccess(OfficialOutputSourceReadService.ValidateRequest(validRequest), "Output source validation should accept journal entry lookup.");

        OfficialOutputSourceReadRequest normalized =
            OfficialOutputSourceReadService.NormalizeRequest(validRequest with
            {
                OfficialDocumentNumber = "  OR-2026-000001  ",
                RegistrationScopeCode = "  LOCAL_SINGLE_STORE  "
            });

        RequireEqual("OR-2026-000001", normalized.OfficialDocumentNumber, "Output source document number must be normalized.");
        RequireEqual("LOCAL_SINGLE_STORE", normalized.RegistrationScopeCode, "Output source scope must be normalized.");

        ExpectValidationFailure(
            OfficialOutputSourceReadService.ValidateRequest(validRequest with
            {
                JournalEntryId = null,
                OfficialDocumentNumber = null
            }),
            "OfficialOutputSource.DocumentReferenceRequired");

        ExpectValidationFailure(
            OfficialOutputSourceReadService.ValidateRequest(validRequest with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                DocumentKind = null
            }),
            "OfficialOutputSource.DocumentKindRequired");

        ExpectValidationFailure(
            OfficialOutputSourceReadService.ValidateRequest(validRequest with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }),
            "OfficialOutputSource.MachineProfileOrScopeRequired");

        ElectronicJournalSnapshotEvidence evidence = CreateSnapshotEvidence();

        Result<OfficialOutputSourceResult> result =
            OfficialOutputSourceReadService.BuildResult(
                CreateSourceEvidence(evidence),
                CreateLifecycle(OfficialDocumentLifecycleState.Active));

        RequireSuccess(result, "Output source build should succeed with verified journal snapshot evidence.");
        RequireEqual("OR-2026-000001", result.Value.Header.OfficialDocumentNumber, "Output source header must expose official document number.");
        RequireEqual(1, result.Value.LineItems.Count, "Output source must expose line items.");
        RequireEqual(1, result.Value.Payments.Count, "Output source must expose payments.");
        RequireEqual(1200L, result.Value.Totals.TotalCents, "Output source must expose totals.");
        RequireEqual("VAT-DEFERRED", result.Value.Totals.TaxModeCode, "Output source must expose tax mode.");
        RequireTrue(result.Value.Evidence.SnapshotHashMatches, "Output source must expose verified hash match.");
        RequireEqual(OfficialDocumentLifecycleState.Active, result.Value.Lifecycle.State, "Output source must include lifecycle state.");

        Result<OfficialOutputSourceResult> mismatch =
            OfficialOutputSourceReadService.BuildResult(
                CreateSourceEvidence(evidence with { PayloadHash = "bad-hash" }),
                CreateLifecycle(OfficialDocumentLifecycleState.Active));

        ExpectSourceFailure(mismatch, "OfficialOutputSource.SnapshotHashMismatch");

        Result<OfficialOutputSourceResult> voided =
            OfficialOutputSourceReadService.BuildResult(
                CreateSourceEvidence(evidence),
                CreateLifecycle(OfficialDocumentLifecycleState.Voided, "Corrected tender note."));

        RequireSuccess(voided, "Voided lifecycle source should still build for controlled output consumers.");
        RequireEqual(OfficialDocumentLifecycleState.Voided, voided.Value.Lifecycle.State, "Output source must include voided lifecycle state.");
        RequireEqual(1, voided.Value.Lifecycle.CorrectionCount, "Output source must include correction count.");
        RequireEqual("Corrected tender note.", voided.Value.Lifecycle.CorrectionNotes[0], "Output source must include correction notes.");
    }

    private static ElectronicJournalSnapshotEvidence CreateSnapshotEvidence()
    {
        Result<ElectronicJournalSnapshotEvidence> result =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(new OfficialDocumentSnapshotDraft(
                OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                new OfficialDocumentSnapshotDraftHeader(
                    RegisteredTaxpayerName: "Output Source Taxpayer",
                    BusinessName: "Output Source Store",
                    TaxpayerIdentificationNumber: "000-111-222-333",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "Output Source Registered Address",
                    TransactionDateTime: TransactionTimestamp,
                    DocumentNumber: "OR-2026-000001",
                    MachineSerialNumber: "SERIAL-OUTPUT",
                    MachineIdentificationNumber: "MIN-OUTPUT",
                    AccreditationNumber: "ACC-OUTPUT",
                    PermitToUseNumber: "PTU-OUTPUT",
                    CashierIdentifier: "official-output-cashier",
                    TerminalIdentifier: "POS-OUTPUT"),
                new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        "Trusted Output Item",
                        "4800000000011",
                        "pc",
                        1000,
                        1200,
                        0,
                        1200)
                },
                new OfficialDocumentSnapshotDraftAmounts(
                    SubtotalCents: 1200,
                    DiscountCents: 0,
                    TaxCents: 0,
                    TotalCents: 1200,
                    AmountPaidCents: 1200,
                    ChangeCents: 0),
                new[]
                {
                    new OfficialDocumentSnapshotDraftPayment(
                        PaymentMethod.Cash,
                        1200,
                        TransactionTimestamp)
                },
                new OfficialDocumentSnapshotDraftTaxMode(
                    TaxModeCode: "VAT-DEFERRED",
                    VatBucketEngineDeferred: true,
                    Notes: "Detailed VAT bucket classification is deferred."),
                new[]
                {
                    "Official output source reads journal snapshot evidence only."
                }));

        RequireSuccess(result, "Snapshot evidence setup should succeed.");

        return result.Value;
    }

    private static OfficialOutputSourceEvidence CreateSourceEvidence(ElectronicJournalSnapshotEvidence evidence)
    {
        return new OfficialOutputSourceEvidence(
            JournalEntryId,
            SaleId,
            MachineProfileId,
            SequenceId,
            ElectronicJournalDocumentKind.OfficialReceipt,
            "OR-2026-000001",
            TransactionTimestamp,
            CashierUserId,
            TerminalId: "POS-OUTPUT",
            SnapshotPayloadJson: evidence.PayloadJson,
            SnapshotPayloadHash: evidence.PayloadHash,
            SnapshotHashAlgorithm: evidence.HashAlgorithm,
            OriginalEventId,
            CreateMachineProfile());
    }

    private static OfficialOutputMachineProfile CreateMachineProfile()
    {
        return new OfficialOutputMachineProfile(
            MachineProfileId,
            RegistrationScopeCode: "LOCAL_SINGLE_STORE",
            RegisteredTaxpayerName: "Output Source Taxpayer",
            BusinessName: "Output Source Store",
            RegisteredAddress: "Output Source Registered Address",
            BranchAddress: "Output Source Branch Address",
            TaxpayerIdentificationNumber: "000-111-222-333",
            BranchCode: "00001",
            MachineIdentificationNumber: "MIN-OUTPUT",
            MachineSerialNumber: "SERIAL-OUTPUT",
            PosTerminalNumber: "POS-001",
            AccreditationNumber: "ACC-OUTPUT",
            PermitToUseNumber: "PTU-OUTPUT",
            SoftwareName: "Salryn",
            SoftwareVersion: "TEST",
            SoftwareBuild: "SMOKE",
            ComplianceMode: "ACCREDITED");
    }

    private static OfficialDocumentLifecycleResult CreateLifecycle(
        OfficialDocumentLifecycleState state,
        string? correctionReason = null)
    {
        OfficialDocumentLifecycleEventReadModel original = CreateEvent("ORIGINAL", originalMarker: true);

        List<OfficialDocumentLifecycleEventReadModel> events = new()
        {
            original
        };

        if (!string.IsNullOrWhiteSpace(correctionReason))
        {
            events.Add(CreateEvent("CORRECTION", reason: correctionReason));
        }

        if (state == OfficialDocumentLifecycleState.Voided)
        {
            events.Add(CreateEvent("VOID", voidMarker: true));
        }
        else if (state == OfficialDocumentLifecycleState.Cancelled)
        {
            events.Add(CreateEvent("CANCEL", cancelMarker: true));
        }

        return OfficialDocumentLifecycleReadService.BuildResult(
            new OfficialDocumentLifecycleSource(
                JournalEntryId,
                SaleId,
                MachineProfileId,
                RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                ElectronicJournalDocumentKind.OfficialReceipt,
                "OR-2026-000001",
                TransactionTimestamp,
                CashierUserId,
                "POS-OUTPUT"),
            events,
            new OfficialDocumentLifecycleZReadingCoverage(
                IsCovered: state == OfficialDocumentLifecycleState.Cancelled,
                ZReadingId: state == OfficialDocumentLifecycleState.Cancelled ? Guid.NewGuid() : null,
                FinalizedAt: state == OfficialDocumentLifecycleState.Cancelled ? TransactionTimestamp.AddHours(8) : null));
    }

    private static OfficialDocumentLifecycleEventReadModel CreateEvent(
        string eventType,
        bool originalMarker = false,
        bool voidMarker = false,
        bool cancelMarker = false,
        string? reason = null)
    {
        return new OfficialDocumentLifecycleEventReadModel(
            Guid.NewGuid(),
            eventType,
            originalMarker,
            ReprintMarker: false,
            voidMarker,
            RefundMarker: false,
            cancelMarker,
            CashierUserId,
            reason,
            TransactionTimestamp.AddMinutes(1),
            TerminalId: "POS-OUTPUT",
            MetadataJson: null);
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

    private static void ExpectValidationFailure(Result result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected output source validation failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected output source validation code.");
    }

    private static void ExpectSourceFailure(Result<OfficialOutputSourceResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected output source failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected output source failure code.");
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

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
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
}

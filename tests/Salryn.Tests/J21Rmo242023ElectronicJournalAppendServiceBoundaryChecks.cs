﻿using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendServiceBoundaryChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendServiceBoundary.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(serviceText, "IElectronicJournalAppendService", "J21-5G must define append service boundary.");
        RequireContains(serviceText, "AppendOriginalAsync", "J21-5G must define ORIGINAL append method.");
        RequireContains(serviceText, "ElectronicJournalAppendContractValidator", "J21-5G must define append contract validator.");
        RequireContains(serviceText, "ElectronicJournalAppendResult", "J21-5G must define append result shape.");

        RequireDoesNotContain(serviceText, "Microsoft.Data.Sqlite", "J21-5G must not depend on SQLite.");
        RequireDoesNotContain(serviceText, "SqliteConnection", "J21-5G must not open database connections.");
        RequireDoesNotContain(serviceText, "IDatabaseMigration", "J21-5G must not define migrations.");
        RequireDoesNotContain(serviceText, "CREATE TABLE", "J21-5G must not define schema SQL.");
        RequireDoesNotContain(serviceText, "CompleteSale", "J21-5G must not wire checkout.");
        RequireDoesNotContain(serviceText, "PrintDocument", "J21-5G must not create print documents.");
        RequireDoesNotContain(serviceText, "Crystal", "J21-5G must not wire reporting output.");

        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendContractPlanningChecks.Run();", "CoreSmoke must still run J21-5F.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendServiceBoundaryChecks.Run();", "CoreSmoke must run J21-5G.");

        ElectronicJournalAppendContractValidator validator = new();

        RequireFailureCode(
            validator.Validate(null),
            "ElectronicJournalAppend.ContractRequired",
            "Null append contract must fail.");

        ElectronicJournalAppendContract valid = CreateValidContract();

        RequireSuccess(
            validator.Validate(valid),
            "Valid append contract must pass boundary validation.");

        RequireFailureCode(
            validator.Validate(valid with { BirMachineProfileId = Guid.Empty }),
            "ElectronicJournalAppend.MachineProfileRequired",
            "Machine profile id is required.");

        RequireFailureCode(
            validator.Validate(valid with { OfficialDocumentNumber = " " }),
            "ElectronicJournalAppend.OfficialDocumentNumberRequired",
            "Official document number is required.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
                }
            }),
            "ElectronicJournalAppend.AnchorBoundaryRequired",
            "Journal anchor boundary is required.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                SnapshotEvidence = new ElectronicJournalSnapshotEvidence(
                    PayloadJson: "{}",
                    PayloadHash: "",
                    HashAlgorithm: "SHA-256")
            }),
            "ElectronicJournalAppend.SnapshotHashRequired",
            "Snapshot hash is required when snapshot evidence is supplied.");
    }

    private static ElectronicJournalAppendContract CreateValidContract()
    {
        return new ElectronicJournalAppendContract(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: "OR-2026-000001",
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: null,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: new DateTimeOffset(2026, 6, 23, 12, 0, 1, TimeSpan.Zero)),
            AtomicBoundarySteps: new[]
            {
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: new[] { "J21-5G boundary validation only." });
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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
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
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFailureCode(Result result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }
}

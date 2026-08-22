﻿using Salryn.Core.Models;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendContractPlanningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Models", "ElectronicJournalAppendContracts.cs");
        string planText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_ELECTRONIC_JOURNAL_APPEND_CONTRACT_PLAN.md");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J21_5F_ELECTRONIC_JOURNAL_APPEND_CONTRACT_PLANNING.md");
        string matrixText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_COMPLIANCE_MATRIX.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "J21-5F names the future append inputs and atomic boundary without adding behavior.", "J21-5F contracts must explain ownership.");
        RequireContains(contractText, "ElectronicJournalAppendContract", "J21-5F must define append contract shape.");
        RequireContains(contractText, "ElectronicJournalSnapshotEvidence", "J21-5F must define snapshot evidence shape.");
        RequireContains(contractText, "ElectronicJournalOriginalEventContract", "J21-5F must define ORIGINAL event shape.");
        RequireContains(contractText, "ElectronicJournalAtomicBoundaryStep", "J21-5F must define atomic boundary steps.");
        RequireContains(contractText, "OfficialSequenceAllocation", "J21-5F must name official sequence allocation boundary.");
        RequireContains(contractText, "SnapshotPayloadHashPersistence", "J21-5F must name snapshot persistence boundary.");
        RequireContains(contractText, "JournalAnchorInsert", "J21-5F must name journal anchor insert boundary.");
        RequireContains(contractText, "OriginalEventInsert", "J21-5F must name ORIGINAL event insert boundary.");

        RequireDoesNotContain(contractText, "Microsoft.Data.Sqlite", "J21-5F contracts must not depend on SQLite.");
        RequireDoesNotContain(contractText, "SqliteConnection", "J21-5F contracts must not open database connections.");
        RequireDoesNotContain(contractText, "IDatabaseMigration", "J21-5F contracts must not define migrations.");
        RequireDoesNotContain(contractText, "CREATE TABLE", "J21-5F contracts must not define schema SQL.");
        RequireDoesNotContain(contractText, "CREATE TRIGGER", "J21-5F contracts must not define trigger SQL.");
        RequireDoesNotContain(contractText, "AllocateNextAsync", "J21-5F contracts must not call sequence allocation.");
        RequireDoesNotContain(contractText, "CompleteSale", "J21-5F contracts must not wire checkout.");
        RequireDoesNotContain(contractText, "PrintDocument", "J21-5F contracts must not create print documents.");

        RequireContains(planText, "Status: contract planning only.", "J21-5F plan must be contract planning only.");
        RequireContains(planText, "single explicit transaction", "J21-5F plan must define atomic transaction boundary.");
        RequireContains(planText, "No append service.", "J21-5F plan must block append service.");
        RequireContains(planText, "No checkout wiring.", "J21-5F plan must block checkout wiring.");
        RequireContains(planText, "No database migration.", "J21-5F plan must block migration work.");
        RequireContains(planText, "No official receipt/invoice output claim.", "J21-5F plan must block official output claim.");

        RequireContains(handoffText, "Status: contract planning only.", "J21-5F handoff must be contract planning only.");
        RequireContains(handoffText, "No append service.", "J21-5F handoff must block append service.");
        RequireContains(handoffText, "No checkout wiring.", "J21-5F handoff must block checkout wiring.");
        RequireContains(handoffText, "J21-5G must be explicitly confirmed before proceeding.", "J21-5F handoff must require confirmation before J21-5G.");

        RequireContains(matrixText, "## J21-5F Electronic Journal Append Contract Planning", "Compliance matrix must include J21-5F section.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalSchemaHardeningChecks.Run();", "CoreSmoke must still run J21-5E.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendContractPlanningChecks.Run();", "CoreSmoke must run J21-5F checks.");

        ElectronicJournalAppendContract contract = new(
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
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: new[]
            {
                "J21-5F contract planning only.",
                "No append service.",
                "No checkout wiring.",
                "No official output rendering."
            });

        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, contract.DocumentKind, "Document kind must be readable.");
        RequireEqual("OR-2026-000001", contract.OfficialDocumentNumber, "Official document number shape must be readable.");
        RequireEqual(ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert, contract.AtomicBoundarySteps[2], "Atomic boundary steps must be readable.");
        RequireEqual(true, contract.DeferredComplianceNotes.Any(note => note.Contains("No append service.", StringComparison.Ordinal)), "Deferred notes must preserve no-service guard.");
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

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message);
        }
    }
}

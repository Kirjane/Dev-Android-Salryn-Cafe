using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023SharedTransactionOfficialSequenceAllocationParticipantChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string participantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(participantText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5P participant must exist.");
        RequireContains(participantText, "ISqliteSharedTransactionParticipant<OfficialDocumentSequenceAllocationRequest, OfficialDocumentSequenceAllocation>", "J21-5P must implement the shared transaction participant contract.");
        RequireContains(participantText, "context.Connection", "J21-5P must use the caller-owned connection.");
        RequireContains(participantText, "context.Transaction", "J21-5P must use the caller-owned transaction.");
        RequireContains(participantText, "ReceiptNumber.Create", "J21-5P must preserve deterministic official number formatting.");
        RequireContains(participantText, "official_output_enabled = 1", "J21-5P must preserve official output gate.");
        RequireContains(participantText, "compliance_mode = 'ACCREDITED'", "J21-5P must preserve accredited machine gate.");
        RequireContains(participantText, "current_number = $currentNumber", "J21-5P must preserve concurrent allocation guard.");
        RequireContains(coreSmokeText, "J21Rmo242023SharedTransactionOfficialSequenceAllocationParticipantChecks.Run();", "CoreSmoke must run J21-5P checks.");

        RequireDoesNotContain(participantText, "CreateOpenConnection", "J21-5P participant must not open its own connection.");
        RequireDoesNotContain(participantText, "BeginTransaction", "J21-5P participant must not begin its own transaction.");
        RequireDoesNotContain(participantText, ".Commit(", "J21-5P participant must not commit.");
        RequireDoesNotContain(participantText, ".Rollback(", "J21-5P participant must not rollback.");
        RequireDoesNotContain(participantText, "INSERT INTO bir_electronic_journal", "J21-5P must not add journal persistence.");
        RequireDoesNotContain(participantText, "CompleteSale", "J21-5P must not wire checkout.");
        RequireDoesNotContain(participantText, "Crystal", "J21-5P must not wire Crystal Reports.");
        RequireDoesNotContain(participantText, "PrintDocument", "J21-5P must not wire printing.");

        RunRuntimeChecks();
    }

    private static void RunRuntimeChecks()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5P-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);

            MigrationRunner runner = new(
                factory,
                new IDatabaseMigration[]
                {
                    new InitialSchemaMigration(),
                    new BirMachineProfileSchemaMigration(),
                    new BirOfficialDocumentSequenceSchemaMigration()
                });

            runner.RunPendingMigrations();

            const string readyProfileId = "11111111-1111-1111-1111-111111111111";
            const string notReadyProfileId = "22222222-2222-2222-2222-222222222222";
            const string readySequenceId = "33333333-3333-3333-3333-333333333333";
            const string notReadySequenceId = "44444444-4444-4444-4444-444444444444";

            using (SqliteConnection connection = factory.CreateConnection())
            {
                connection.Open();

                InsertMachineProfile(connection, readyProfileId, isActive: 1, complianceMode: "ACCREDITED", officialOutputEnabled: 1);
                InsertMachineProfile(connection, notReadyProfileId, isActive: 0, complianceMode: "NOT_ACCREDITED", officialOutputEnabled: 0);

                InsertSequence(connection, readySequenceId, readyProfileId, "OFFICIAL_RECEIPT", "OR", 2026, currentNumber: 0, endNumber: 99);
                InsertSequence(connection, notReadySequenceId, notReadyProfileId, "OFFICIAL_RECEIPT", "OR", 2026, currentNumber: 0, endNumber: 99);
            }

            SqliteSharedTransactionBoundary boundary = new(factory);
            SharedSqliteOfficialDocumentSequenceAllocationParticipant participant = new();

            Result<OfficialDocumentSequenceAllocation> committed =
                boundary.ExecuteAsync<OfficialDocumentSequenceAllocation>(
                    (context, token) => participant.ExecuteAsync(
                        context,
                        new OfficialDocumentSequenceAllocationRequest(Guid.Parse(readyProfileId), "official_receipt", "local_single_store", 2026),
                        token)).GetAwaiter().GetResult();

            RequireSuccess(committed, "Shared transaction allocation should succeed.");
            RequireEqual("OR-2026-000001", committed.Value.FormattedDocumentNumber, "Shared transaction allocation must preserve formatted number.");
            RequireCurrentNumber(factory, readySequenceId, 1);

            Result<OfficialDocumentSequenceAllocation> rolledBack =
                boundary.ExecuteAsync<OfficialDocumentSequenceAllocation>(
                    async (context, token) =>
                    {
                        Result<OfficialDocumentSequenceAllocation> allocation = await participant.ExecuteAsync(
                            context,
                            new OfficialDocumentSequenceAllocationRequest(Guid.Parse(readyProfileId), "OFFICIAL_RECEIPT", "LOCAL_SINGLE_STORE", 2026),
                            token);

                        if (allocation.IsFailure)
                        {
                            return allocation;
                        }

                        return Result<OfficialDocumentSequenceAllocation>.Fail(Error.Validation(
                            "J21-5P.IntentionalRollback",
                            "Intentional rollback guard."));
                    }).GetAwaiter().GetResult();

            RequireFailureCode(rolledBack, "J21-5P.IntentionalRollback", "Shared transaction failure should surface caller failure.");
            RequireCurrentNumber(factory, readySequenceId, 1);

            Result<OfficialDocumentSequenceAllocation> notReady =
                boundary.ExecuteAsync<OfficialDocumentSequenceAllocation>(
                    (context, token) => participant.ExecuteAsync(
                        context,
                        new OfficialDocumentSequenceAllocationRequest(Guid.Parse(notReadyProfileId), "OFFICIAL_RECEIPT", "LOCAL_SINGLE_STORE", 2026),
                        token)).GetAwaiter().GetResult();

            RequireFailureCode(notReady, "OfficialSequence.MachineProfileNotReady", "Participant must preserve machine readiness gate.");
            RequireCurrentNumber(factory, notReadySequenceId, 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void InsertMachineProfile(SqliteConnection connection, string profileId, int isActive, string complianceMode, int officialOutputEnabled)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id, registered_taxpayer_name, registered_address, taxpayer_identification_number,
                branch_code, machine_serial_number, software_version, compliance_mode,
                official_output_enabled, is_active, created_at
            )
            VALUES (
                $id, 'Kirch Test Taxpayer', 'Test Registered Address', '000-000-000-000',
                '00000', $machineSerialNumber, 'TEST', $complianceMode,
                $officialOutputEnabled, $isActive, $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$machineSerialNumber", "TEST-SERIAL-" + profileId[..8]);
        command.Parameters.AddWithValue("$complianceMode", complianceMode);
        command.Parameters.AddWithValue("$officialOutputEnabled", officialOutputEnabled);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(SqliteConnection connection, string id, string profileId, string documentKind, string prefix, int year, long currentNumber, long? endNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_official_document_sequences (
                id, bir_machine_profile_id, document_kind, sequence_scope_code, prefix,
                year, start_number, current_number, end_number, padding_digits,
                is_active, is_exhausted, created_at
            )
            VALUES (
                $id, $profileId, $documentKind, 'LOCAL_SINGLE_STORE', $prefix,
                $year, 1, $currentNumber, $endNumber, 6,
                1, 0, $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$documentKind", documentKind);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$currentNumber", currentNumber);
        command.Parameters.AddWithValue("$endNumber", endNumber.HasValue ? endNumber.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void RequireCurrentNumber(SqliteConnectionFactory factory, string sequenceId, long expectedCurrentNumber)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT current_number FROM bir_official_document_sequences WHERE id = $sequenceId;";
        command.Parameters.AddWithValue("$sequenceId", sequenceId);

        long currentNumber = Convert.ToInt64(command.ExecuteScalar());

        if (currentNumber != expectedCurrentNumber)
        {
            throw new InvalidOperationException($"Expected current_number {expectedCurrentNumber}, found {currentNumber}.");
        }
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

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.Error}");
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(message);
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected error code {expectedCode}, found {result.ErrorDetail.Code}.");
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(message);
        }
    }
}

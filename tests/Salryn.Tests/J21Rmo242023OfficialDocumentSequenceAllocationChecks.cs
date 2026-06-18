using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentSequenceAllocationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string serviceText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "OfficialDocumentSequenceAllocationService.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_3B_RMO_OFFICIAL_SEQUENCE_ALLOCATION_SERVICE.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(serviceText, "// Kirch strict dev guide:", "Official allocation service must include strict development guide comments.");
        RequireContains(serviceText, "J21-3B owns official sequence allocation only.", "Official allocation service must explain J21-3B ownership.");
        RequireContains(serviceText, "It does not create sales, print receipts, render receipt/invoice text, write snapshots", "Official allocation service must block output and checkout scope creep.");
        RequireContains(serviceText, "HasAccreditedActiveMachineProfile", "Official allocation service must gate through machine profile readiness.");
        RequireContains(serviceText, "official_output_enabled = 1", "Official allocation service must require official output gate.");
        RequireContains(serviceText, "compliance_mode = 'ACCREDITED'", "Official allocation service must require accredited compliance mode.");
        RequireContains(serviceText, "current_number = $nextNumber", "Official allocation service must advance current_number.");
        RequireContains(serviceText, "current_number = $currentNumber", "Official allocation service must guard against concurrent sequence movement.");
        RequireContains(serviceText, "ReceiptNumber.Create", "Official allocation service must use deterministic padded receipt number formatting.");
        RequireContains(matrixText, "## J21-3B Official Receipt/Invoice Sequence Allocation Service", "Compliance matrix must include J21-3B allocation service section.");
        RequireContains(handoffText, "Status: allocation service only.", "J21-3B handoff must preserve allocation-only scope.");
        RequireContains(handoffText, "No checkout flow wiring.", "J21-3B handoff must block checkout wiring.");
        RequireContains(handoffText, "No receipt printing.", "J21-3B handoff must block printing.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentSequenceAllocationChecks.Run();", "Core smoke must run J21-3B official allocation checks.");

        RunAllocationRuntimeChecks();
    }

    private static void RunAllocationRuntimeChecks()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-3B-" + Guid.NewGuid().ToString("N"));

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

            using SqliteConnection connection = factory.CreateConnection();
            connection.Open();

            const string readyProfileId = "11111111-1111-1111-1111-111111111111";
            const string notReadyProfileId = "22222222-2222-2222-2222-222222222222";

            InsertMachineProfile(
                connection,
                readyProfileId,
                isActive: 1,
                complianceMode: "ACCREDITED",
                officialOutputEnabled: 1);

            InsertMachineProfile(
                connection,
                notReadyProfileId,
                isActive: 0,
                complianceMode: "NOT_ACCREDITED",
                officialOutputEnabled: 0);

            InsertSequence(
                connection,
                id: "33333333-3333-3333-3333-333333333333",
                profileId: readyProfileId,
                documentKind: "OFFICIAL_RECEIPT",
                prefix: "OR",
                year: 2026,
                startNumber: 1,
                currentNumber: 0,
                endNumber: 2,
                paddingDigits: 6,
                isActive: 1,
                isExhausted: 0);

            InsertSequence(
                connection,
                id: "44444444-4444-4444-4444-444444444444",
                profileId: notReadyProfileId,
                documentKind: "OFFICIAL_RECEIPT",
                prefix: "OR",
                year: 2026,
                startNumber: 1,
                currentNumber: 0,
                endNumber: 99,
                paddingDigits: 6,
                isActive: 1,
                isExhausted: 0);

            OfficialDocumentSequenceAllocationService service = new(factory);

            Result<OfficialDocumentSequenceAllocation> firstAllocation =
                service.AllocateNextAsync(new OfficialDocumentSequenceAllocationRequest(
                    Guid.Parse(readyProfileId),
                    "OFFICIAL_RECEIPT",
                    "LOCAL_SINGLE_STORE",
                    2026)).GetAwaiter().GetResult();

            RequireSuccess(firstAllocation, "First official sequence allocation should succeed.");
            RequireEqual("OR-2026-000001", firstAllocation.Value.FormattedDocumentNumber, "First allocation must be deterministic and padded.");
            RequireEqual(1L, firstAllocation.Value.Number, "First allocation must use next number 1.");
            RequireCurrentNumber(connection, "33333333-3333-3333-3333-333333333333", 1);
            RequireSequenceState(connection, "33333333-3333-3333-3333-333333333333", isActive: 1, isExhausted: 0);

            Result<OfficialDocumentSequenceAllocation> secondAllocation =
                service.AllocateNextAsync(new OfficialDocumentSequenceAllocationRequest(
                    Guid.Parse(readyProfileId),
                    "official_receipt",
                    "local_single_store",
                    2026)).GetAwaiter().GetResult();

            RequireSuccess(secondAllocation, "Second official sequence allocation should succeed.");
            RequireEqual("OR-2026-000002", secondAllocation.Value.FormattedDocumentNumber, "Second allocation must be deterministic and padded.");
            RequireEqual(2L, secondAllocation.Value.Number, "Second allocation must use next number 2.");
            RequireCurrentNumber(connection, "33333333-3333-3333-3333-333333333333", 2);
            RequireSequenceState(connection, "33333333-3333-3333-3333-333333333333", isActive: 0, isExhausted: 1);

            Result<OfficialDocumentSequenceAllocation> exhaustedAllocation =
                service.AllocateNextAsync(new OfficialDocumentSequenceAllocationRequest(
                    Guid.Parse(readyProfileId),
                    "OFFICIAL_RECEIPT",
                    "LOCAL_SINGLE_STORE",
                    2026)).GetAwaiter().GetResult();

            RequireFailureCode(
                exhaustedAllocation,
                "OfficialSequence.ActiveSequenceNotFound",
                "Allocation must stop after the range is exhausted and deactivated.");

            Result<OfficialDocumentSequenceAllocation> notReadyAllocation =
                service.AllocateNextAsync(new OfficialDocumentSequenceAllocationRequest(
                    Guid.Parse(notReadyProfileId),
                    "OFFICIAL_RECEIPT",
                    "LOCAL_SINGLE_STORE",
                    2026)).GetAwaiter().GetResult();

            RequireFailureCode(
                notReadyAllocation,
                "OfficialSequence.MachineProfileNotReady",
                "Allocation must be blocked when machine profile is not accredited or official-output enabled.");

            RequireCurrentNumber(connection, "44444444-4444-4444-4444-444444444444", 0);

            Result<OfficialDocumentSequenceAllocation> invalidKind =
                service.AllocateNextAsync(new OfficialDocumentSequenceAllocationRequest(
                    Guid.Parse(readyProfileId),
                    "SALES_INVOICE",
                    "LOCAL_SINGLE_STORE",
                    2026)).GetAwaiter().GetResult();

            RequireFailureCode(
                invalidKind,
                "OfficialSequence.InvalidDocumentKind",
                "Allocation must reject unsupported document kind values.");
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

    private static void InsertMachineProfile(
        SqliteConnection connection,
        string profileId,
        int isActive,
        string complianceMode,
        int officialOutputEnabled)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id,
                registered_taxpayer_name,
                registered_address,
                taxpayer_identification_number,
                branch_code,
                machine_serial_number,
                software_version,
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                $id,
                $registeredTaxpayerName,
                $registeredAddress,
                $tin,
                $branchCode,
                $machineSerialNumber,
                $softwareVersion,
                $complianceMode,
                $officialOutputEnabled,
                $isActive,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$registeredTaxpayerName", "Kirch Test Taxpayer");
        command.Parameters.AddWithValue("$registeredAddress", "Test Registered Address");
        command.Parameters.AddWithValue("$tin", "000-000-000-000");
        command.Parameters.AddWithValue("$branchCode", "00000");
        command.Parameters.AddWithValue("$machineSerialNumber", "TEST-SERIAL-" + profileId[..8]);
        command.Parameters.AddWithValue("$softwareVersion", "TEST");
        command.Parameters.AddWithValue("$complianceMode", complianceMode);
        command.Parameters.AddWithValue("$officialOutputEnabled", officialOutputEnabled);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(
        SqliteConnection connection,
        string id,
        string profileId,
        string documentKind,
        string prefix,
        int year,
        long startNumber,
        long currentNumber,
        long? endNumber,
        int paddingDigits,
        int isActive,
        int isExhausted)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_official_document_sequences (
                id,
                bir_machine_profile_id,
                document_kind,
                sequence_scope_code,
                prefix,
                year,
                start_number,
                current_number,
                end_number,
                padding_digits,
                is_active,
                is_exhausted,
                created_at
            )
            VALUES (
                $id,
                $profileId,
                $documentKind,
                'LOCAL_SINGLE_STORE',
                $prefix,
                $year,
                $startNumber,
                $currentNumber,
                $endNumber,
                $paddingDigits,
                $isActive,
                $isExhausted,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$documentKind", documentKind);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$startNumber", startNumber);
        command.Parameters.AddWithValue("$currentNumber", currentNumber);
        command.Parameters.AddWithValue("$endNumber", endNumber.HasValue ? endNumber.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$paddingDigits", paddingDigits);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$isExhausted", isExhausted);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void RequireCurrentNumber(SqliteConnection connection, string sequenceId, long expectedCurrentNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_number
            FROM bir_official_document_sequences
            WHERE id = $sequenceId;
            """;
        command.Parameters.AddWithValue("$sequenceId", sequenceId);

        long currentNumber = Convert.ToInt64(command.ExecuteScalar());

        if (currentNumber != expectedCurrentNumber)
        {
            throw new InvalidOperationException($"Expected current_number {expectedCurrentNumber}, found {currentNumber}.");
        }
    }

    private static void RequireSequenceState(
        SqliteConnection connection,
        string sequenceId,
        int isActive,
        int isExhausted)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_active, is_exhausted
            FROM bir_official_document_sequences
            WHERE id = $sequenceId;
            """;
        command.Parameters.AddWithValue("$sequenceId", sequenceId);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Expected sequence row was not found.");
        }

        int actualIsActive = reader.GetInt32(0);
        int actualIsExhausted = reader.GetInt32(1);

        if (actualIsActive != isActive || actualIsExhausted != isExhausted)
        {
            throw new InvalidOperationException($"Expected sequence state active={isActive}/exhausted={isExhausted}, found active={actualIsActive}/exhausted={actualIsExhausted}.");
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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
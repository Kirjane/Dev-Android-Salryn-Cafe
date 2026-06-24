namespace Salryn.Tests;

public static class J21Rmo242023SharedSqliteTransactionBoundaryContractChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Transactions",
            "SqliteSharedTransactionBoundaryContracts.cs");

        string sequenceServiceText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "OfficialDocumentSequenceAllocationService.cs");

        string appendSealText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_APPEND_BOUNDARY_SEAL.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5N_SHARED_SQLITE_TRANSACTION_BOUNDARY_CONTRACT.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(contractText, "namespace Salryn.Infrastructure.Data.Transactions;", "J21-5N contract must stay in Infrastructure.");
        RequireContains(contractText, "using Microsoft.Data.Sqlite;", "J21-5N must explicitly model SQLite transaction context.");
        RequireContains(contractText, "SqliteSharedTransactionContext", "J21-5N must define shared transaction context.");
        RequireContains(contractText, "SqliteConnection Connection", "J21-5N context must carry the open connection.");
        RequireContains(contractText, "SqliteTransaction Transaction", "J21-5N context must carry the active transaction.");
        RequireContains(contractText, "SqliteSharedTransactionWork<T>", "J21-5N must define transaction work delegate.");
        RequireContains(contractText, "ISqliteSharedTransactionBoundary", "J21-5N must define transaction boundary contract.");
        RequireContains(contractText, "ISqliteSharedTransactionParticipant<TRequest, TResult>", "J21-5N must define participant contract.");
        RequireContains(contractText, "Result<T>", "J21-5N must preserve Result-based error flow.");

        RequireDoesNotContain(contractText, "BeginTransaction", "J21-5N must not implement transaction start.");
        RequireDoesNotContain(contractText, "Commit", "J21-5N must not implement transaction commit.");
        RequireDoesNotContain(contractText, "Rollback", "J21-5N must not implement transaction rollback.");
        RequireDoesNotContain(contractText, "ExecuteNonQuery", "J21-5N must not write SQL.");
        RequireDoesNotContain(contractText, "INSERT INTO", "J21-5N must not insert data.");
        RequireDoesNotContain(contractText, "UPDATE ", "J21-5N must not update data.");
        RequireDoesNotContain(contractText, "CREATE TABLE", "J21-5N must not define schema.");
        RequireDoesNotContain(contractText, "CREATE TRIGGER", "J21-5N must not define hardening.");
        RequireDoesNotContain(contractText, "CompleteSale", "J21-5N must not wire checkout.");
        RequireDoesNotContain(contractText, "Crystal", "J21-5N must not wire Crystal Reports.");
        RequireDoesNotContain(contractText, "PrintDocument", "J21-5N must not wire printing.");

        RequireContains(sequenceServiceText, "SqliteSharedTransactionBoundary", "Audit must prove sequence service now uses the shared transaction boundary.");
        RequireContains(sequenceServiceText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "Audit must prove sequence service now delegates allocation behavior to the shared participant.");
        RequireDoesNotContain(sequenceServiceText, "BeginTransaction", "Audit must prove sequence service no longer owns its own transaction.");
        RequireDoesNotContain(sequenceServiceText, ".Commit(", "Audit must prove sequence service no longer commits independently.");
        RequireDoesNotContain(sequenceServiceText, ".Rollback(", "Audit must prove sequence service no longer rolls back independently.");
        RequireContains(appendSealText, "Infrastructure persistence must be a separate phase.", "J21-5N must remain grounded in sealed append boundary.");
        RequireContains(matrixText, "## J21-5N Shared SQLite Transaction Boundary Contract", "Compliance matrix must include J21-5N.");
        RequireContains(handoffText, "Status: shared transaction contract only.", "J21-5N handoff must be contract-only.");
        RequireContains(handoffText, "No SQLite adapter implementation.", "J21-5N handoff must block adapter implementation.");
        RequireContains(coreSmokeText, "J21Rmo242023SharedSqliteTransactionBoundaryContractChecks.Run();", "CoreSmoke must run J21-5N.");
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
}

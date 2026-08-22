using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023SharedSqliteTransactionBoundaryImplementationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string implementationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Transactions",
            "SqliteSharedTransactionBoundary.cs");

        string contractText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Transactions",
            "SqliteSharedTransactionBoundaryContracts.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5O_SHARED_SQLITE_TRANSACTION_BOUNDARY_IMPLEMENTATION.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(implementationText, "SqliteSharedTransactionBoundary", "J21-5O must implement shared transaction boundary.");
        RequireContains(implementationText, "ISqliteSharedTransactionBoundary", "J21-5O implementation must satisfy boundary contract.");
        RequireContains(implementationText, "CreateOpenConnection()", "J21-5O must use existing SQLite connection pattern.");
        RequireContains(implementationText, "BeginTransaction()", "J21-5O must own transaction start.");
        RequireContains(implementationText, "transaction.Commit();", "J21-5O must commit successful work.");
        RequireContains(implementationText, "TryRollback(transaction);", "J21-5O must rollback failed work.");
        RequireContains(implementationText, "SqliteSharedTransactionContext context = new(connection, transaction);", "J21-5O must pass shared context to work.");
        RequireContains(contractText, "ISqliteSharedTransactionBoundary", "J21-5O must preserve J21-5N contract.");

        RequireDoesNotContain(implementationText, "OfficialDocumentSequenceAllocationService", "J21-5O must not rewrite sequence allocation.");
        RequireDoesNotContain(implementationText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5O must not implement journal append adapter.");
        RequireDoesNotContain(implementationText, "INSERT INTO bir_electronic_journal", "J21-5O must not write journal tables.");
        RequireDoesNotContain(implementationText, "UPDATE bir_official_document_sequences", "J21-5O must not allocate sequence numbers.");
        RequireDoesNotContain(implementationText, "CompleteSale", "J21-5O must not wire checkout.");
        RequireDoesNotContain(implementationText, "Crystal", "J21-5O must not wire Crystal Reports.");
        RequireDoesNotContain(implementationText, "PrintDocument", "J21-5O must not wire printing.");

        RequireContains(matrixText, "## J21-5O Shared SQLite Transaction Boundary Implementation", "Compliance matrix must include J21-5O.");
        RequireContains(handoffText, "Status: shared transaction implementation only.", "J21-5O handoff must be implementation-only.");
        RequireContains(handoffText, "No sequence allocation participant.", "J21-5O handoff must block sequence participant work.");
        RequireContains(coreSmokeText, "J21Rmo242023SharedSqliteTransactionBoundaryImplementationChecks.Run();", "CoreSmoke must run J21-5O.");

        RunTransactionBehaviorChecks();
    }

    private static void RunTransactionBehaviorChecks()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-5O-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);

            using (SqliteConnection connection = factory.CreateConnection())
            {
                connection.Open();
                SqlitePragma.Apply(connection);
                CreateProbeTable(connection);
            }

            SqliteSharedTransactionBoundary boundary = new(factory);

            Result<int> missingWork = boundary.ExecuteAsync<int>(null!).GetAwaiter().GetResult();
            RequireFailureCode(
                missingWork,
                "SqliteSharedTransaction.WorkRequired",
                "Missing shared transaction work must fail.");

            Result<int> success = boundary.ExecuteAsync<int>((context, cancellationToken) =>
            {
                InsertProbe(context.Connection, context.Transaction, "committed");
                return Task.FromResult(Result<int>.Success(1));
            }).GetAwaiter().GetResult();

            RequireSuccess(success, "Successful shared transaction work must succeed.");
            RequireEqual(1, CountProbeRows(factory), "Successful shared transaction work must commit.");

            Result<int> failure = boundary.ExecuteAsync<int>((context, cancellationToken) =>
            {
                InsertProbe(context.Connection, context.Transaction, "rolled-back-result");

                return Task.FromResult(Result<int>.Fail(Error.Validation(
                    "J21-5O.IntentionalFailure",
                    "Intentional rollback check.")));
            }).GetAwaiter().GetResult();

            RequireFailureCode(
                failure,
                "J21-5O.IntentionalFailure",
                "Failed shared transaction work must preserve failure code.");

            RequireEqual(1, CountProbeRows(factory), "Failed Result work must rollback inserted rows.");

            Result<int> thrown = boundary.ExecuteAsync<int>((context, cancellationToken) =>
            {
                InsertProbe(context.Connection, context.Transaction, "rolled-back-exception");
                throw new InvalidOperationException("Intentional shared transaction exception.");
            }).GetAwaiter().GetResult();

            RequireFailureCode(
                thrown,
                "SqliteSharedTransaction.WorkFailed",
                "Thrown shared transaction work must return failure.");

            RequireEqual(1, CountProbeRows(factory), "Thrown work must rollback inserted rows.");
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

    private static void CreateProbeTable(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE j21_5o_probe (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                label TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertProbe(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string label)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO j21_5o_probe (label)
            VALUES ($label);
            """;
        command.Parameters.AddWithValue("$label", label);
        command.ExecuteNonQuery();
    }

    private static int CountProbeRows(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        SqlitePragma.Apply(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM j21_5o_probe;";

        return Convert.ToInt32(command.ExecuteScalar());
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
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
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

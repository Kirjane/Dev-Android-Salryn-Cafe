using Microsoft.Data.Sqlite;
using Salryn.Core.ValueObjects;

namespace Salryn.Mobile.Foundation.P0;

public sealed record P0StoreCounts(long BusinessRows, long EvidenceRows, long OutboxRows);

public sealed record P0LocalWriteResult(
    bool Committed,
    Guid BusinessId,
    Guid OperationId);

public sealed record P0OutboxItem(
    Guid OperationId,
    Guid BusinessId,
    string Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset? AcknowledgedAtUtc);

public sealed class P0LocalStore
{
    public const string Pending = "Pending";
    public const string Acknowledged = "Acknowledged";

    private readonly string _databasePath;

    public P0LocalStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A P0 database path is required.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("P0 database path has no parent directory."));

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS p0_business (
                id TEXT PRIMARY KEY,
                amount_cents INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS p0_evidence (
                id TEXT PRIMARY KEY,
                business_id TEXT NOT NULL UNIQUE,
                evidence_text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (business_id) REFERENCES p0_business(id)
            );

            CREATE TABLE IF NOT EXISTS p0_outbox (
                operation_id TEXT PRIMARY KEY,
                business_id TEXT NOT NULL UNIQUE,
                status TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                created_at_utc TEXT NOT NULL,
                acked_at_utc TEXT NULL,
                FOREIGN KEY (business_id) REFERENCES p0_business(id),
                CHECK (status IN ('Pending', 'Acknowledged'))
            );

            CREATE INDEX IF NOT EXISTS idx_p0_outbox_status
                ON p0_outbox(status, created_at_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<P0LocalWriteResult> WriteProofOperationAsync(
        Guid businessId,
        Money amount,
        bool forceFailure,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty)
        {
            throw new ArgumentException("A business ID is required.", nameof(businessId));
        }

        Guid operationId = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO p0_business (id, amount_cents, created_at_utc)
                VALUES ($id, $amount, $now);
                """,
                cancellationToken,
                ("$id", businessId.ToString("D")),
                ("$amount", amount.Cents),
                ("$now", now)).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO p0_evidence (id, business_id, evidence_text, created_at_utc)
                VALUES ($id, $business, $evidence, $now);
                """,
                cancellationToken,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$business", businessId.ToString("D")),
                ("$evidence", "P0 atomic evidence for business operation."),
                ("$now", now)).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO p0_outbox
                    (operation_id, business_id, status, attempt_count, last_error, created_at_utc, acked_at_utc)
                VALUES
                    ($operation, $business, 'Pending', 0, NULL, $now, NULL);
                """,
                cancellationToken,
                ("$operation", operationId.ToString("D")),
                ("$business", businessId.ToString("D")),
                ("$now", now)).ConfigureAwait(false);

            if (forceFailure)
            {
                throw new P0ForcedRollbackException();
            }

            transaction.Commit();
            return new P0LocalWriteResult(true, businessId, operationId);
        }
        catch (P0ForcedRollbackException)
        {
            transaction.Rollback();
            return new P0LocalWriteResult(false, businessId, operationId);
        }
    }

    public async Task<P0StoreCounts> ReadCountsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        long business = await CountAsync(connection, "p0_business", cancellationToken).ConfigureAwait(false);
        long evidence = await CountAsync(connection, "p0_evidence", cancellationToken).ConfigureAwait(false);
        long outbox = await CountAsync(connection, "p0_outbox", cancellationToken).ConfigureAwait(false);

        return new P0StoreCounts(business, evidence, outbox);
    }

    public async Task<IReadOnlyList<P0OutboxItem>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        List<P0OutboxItem> items = new();

        await using (SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT operation_id, business_id, status, attempt_count, last_error, acked_at_utc
                FROM p0_outbox
                WHERE status = 'Pending'
                ORDER BY created_at_utc, operation_id;
                """;

            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadOutbox(reader));
            }
        }

        // No connection or SQLite transaction remains alive when transport code receives these rows.
        return items;
    }

    public async Task<P0OutboxItem?> FindOutboxAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT operation_id, business_id, status, attempt_count, last_error, acked_at_utc
            FROM p0_outbox
            WHERE operation_id = $operation;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadOutbox(reader)
            : null;
    }

    public async Task RecordAttemptFailureAsync(
        Guid operationId,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE p0_outbox
            SET attempt_count = attempt_count + 1,
                last_error = $error
            WHERE operation_id = $operation
              AND status = 'Pending';
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$error", string.IsNullOrWhiteSpace(error) ? "Transport failure." : error.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkAcknowledgedAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE p0_outbox
            SET status = 'Acknowledged',
                attempt_count = attempt_count + 1,
                last_error = NULL,
                acked_at_utc = $acked
            WHERE operation_id = $operation
              AND status = 'Pending';
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$acked", DateTimeOffset.UtcNow.ToString("O"));

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value);
    }

    private static P0OutboxItem ReadOutbox(SqliteDataReader reader)
    {
        DateTimeOffset? ackedAt = reader.IsDBNull(5)
            ? null
            : DateTimeOffset.Parse(reader.GetString(5));

        return new P0OutboxItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ackedAt);
    }

    private sealed class P0ForcedRollbackException : Exception;
}

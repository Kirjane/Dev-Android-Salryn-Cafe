using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteTerminalRegistryRepository : SqliteRepositoryBase
{
    public SqliteTerminalRegistryRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<IReadOnlyList<SalrynTerminalRegistration>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    terminal_id,
                    store_id,
                    host_id,
                    terminal_name,
                    terminal_role,
                    terminal_status,
                    registered_at_utc,
                    last_seen_at_utc,
                    disabled_at_utc,
                    revoked_at_utc,
                    created_by_user_id,
                    notes
                FROM salryn_terminal_registrations
                ORDER BY terminal_name COLLATE NOCASE;
                """;

            List<SalrynTerminalRegistration> items = new();

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return Task.FromResult(Result<IReadOnlyList<SalrynTerminalRegistration>>.Success(items));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<IReadOnlyList<SalrynTerminalRegistration>>.Fail(Error.Unexpected(
                "TerminalRegistry.ListFailed",
                $"Failed to list terminal registrations: {ex.Message}")));
        }
    }

    public Task<Result<SalrynTerminalRegistration?>> GetByIdAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    terminal_id,
                    store_id,
                    host_id,
                    terminal_name,
                    terminal_role,
                    terminal_status,
                    registered_at_utc,
                    last_seen_at_utc,
                    disabled_at_utc,
                    revoked_at_utc,
                    created_by_user_id,
                    notes
                FROM salryn_terminal_registrations
                WHERE terminal_id = $terminalId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$terminalId", terminalId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<SalrynTerminalRegistration?>.Success(null));
            }

            return Task.FromResult(Result<SalrynTerminalRegistration?>.Success(Map(reader)));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<SalrynTerminalRegistration?>.Fail(Error.Unexpected(
                "TerminalRegistry.ReadFailed",
                $"Failed to read terminal registration: {ex.Message}")));
        }
    }

    public Task<Result<SalrynTerminalRegistration>> UpsertAsync(
        SalrynTerminalRegistration registration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string now = DateTimeOffset.UtcNow.ToString("O");

            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO salryn_terminal_registrations (
                    terminal_id,
                    store_id,
                    host_id,
                    terminal_name,
                    terminal_role,
                    terminal_status,
                    registered_at_utc,
                    last_seen_at_utc,
                    disabled_at_utc,
                    revoked_at_utc,
                    created_by_user_id,
                    notes,
                    created_at,
                    updated_at)
                VALUES (
                    $terminalId,
                    $storeId,
                    $hostId,
                    $terminalName,
                    $terminalRole,
                    $terminalStatus,
                    $registeredAtUtc,
                    $lastSeenAtUtc,
                    $disabledAtUtc,
                    $revokedAtUtc,
                    $createdByUserId,
                    $notes,
                    $createdAt,
                    $updatedAt)
                ON CONFLICT(terminal_id) DO UPDATE SET
                    store_id = excluded.store_id,
                    host_id = excluded.host_id,
                    terminal_name = excluded.terminal_name,
                    terminal_role = excluded.terminal_role,
                    terminal_status = excluded.terminal_status,
                    registered_at_utc = excluded.registered_at_utc,
                    last_seen_at_utc = excluded.last_seen_at_utc,
                    disabled_at_utc = excluded.disabled_at_utc,
                    revoked_at_utc = excluded.revoked_at_utc,
                    created_by_user_id = excluded.created_by_user_id,
                    notes = excluded.notes,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$terminalId", registration.TerminalId.ToString("D"));
            command.Parameters.AddWithValue("$storeId", registration.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$hostId", registration.HostId.ToString("D"));
            command.Parameters.AddWithValue("$terminalName", registration.TerminalName);
            command.Parameters.AddWithValue("$terminalRole", (int)registration.TerminalRole);
            command.Parameters.AddWithValue("$terminalStatus", (int)registration.Status);
            command.Parameters.AddWithValue("$registeredAtUtc", registration.RegisteredAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$lastSeenAtUtc", registration.LastSeenAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$disabledAtUtc", registration.DisabledAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$revokedAtUtc", registration.RevokedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdByUserId", registration.CreatedByUserId.ToString("D"));
            command.Parameters.AddWithValue("$notes", registration.Notes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);

            command.ExecuteNonQuery();
            return Task.FromResult(Result<SalrynTerminalRegistration>.Success(registration));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<SalrynTerminalRegistration>.Fail(Error.Unexpected(
                "TerminalRegistry.UpsertFailed",
                $"Failed to upsert terminal registration: {ex.Message}")));
        }
    }

    private static SalrynTerminalRegistration Map(SqliteDataReader reader)
    {
        return new SalrynTerminalRegistration(
            TerminalId: Guid.Parse(reader.GetString(0)),
            StoreId: Guid.Parse(reader.GetString(1)),
            HostId: Guid.Parse(reader.GetString(2)),
            TerminalName: reader.GetString(3),
            TerminalRole: (TerminalRegistrationRole)reader.GetInt32(4),
            Status: (TerminalRegistrationStatus)reader.GetInt32(5),
            RegisteredAtUtc: DateTimeOffset.Parse(reader.GetString(6)),
            LastSeenAtUtc: reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            DisabledAtUtc: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            RevokedAtUtc: reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            CreatedByUserId: Guid.Parse(reader.GetString(10)),
            Notes: reader.IsDBNull(11) ? null : reader.GetString(11));
    }
}

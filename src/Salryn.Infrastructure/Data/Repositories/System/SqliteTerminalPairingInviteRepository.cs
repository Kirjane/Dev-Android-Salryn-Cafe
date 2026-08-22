using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteTerminalPairingInviteRepository : SqliteRepositoryBase
{
    public SqliteTerminalPairingInviteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<IReadOnlyList<SalrynTerminalPairingInvite>>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    pairing_id,
                    store_id,
                    host_id,
                    target_terminal_name,
                    target_terminal_role,
                    pairing_status,
                    pairing_code_hash,
                    created_at_utc,
                    expires_at_utc,
                    consumed_at_utc,
                    revoked_at_utc,
                    created_by_user_id
                FROM salryn_terminal_pairing_invites
                WHERE pairing_status = $issuedStatus
                ORDER BY created_at_utc DESC;
                """;
            command.Parameters.AddWithValue("$issuedStatus", (int)PairingInviteStatus.Issued);

            List<SalrynTerminalPairingInvite> items = new();

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return Task.FromResult(Result<IReadOnlyList<SalrynTerminalPairingInvite>>.Success(items));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<IReadOnlyList<SalrynTerminalPairingInvite>>.Fail(Error.Unexpected(
                "TerminalPairing.ListFailed",
                $"Failed to list pairing invites: {ex.Message}")));
        }
    }

    public Task<Result<SalrynTerminalPairingInvite?>> GetByIdAsync(
        Guid pairingId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SelectSql + " WHERE pairing_id = $pairingId LIMIT 1;";
            command.Parameters.AddWithValue("$pairingId", pairingId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Success(null));
            }

            return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Success(Map(reader)));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Fail(Error.Unexpected(
                "TerminalPairing.ReadFailed",
                $"Failed to read pairing invite: {ex.Message}")));
        }
    }

    public Task<Result<SalrynTerminalPairingInvite?>> GetByCodeHashAsync(
        string pairingCodeHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SelectSql + " WHERE pairing_code_hash = $pairingCodeHash LIMIT 1;";
            command.Parameters.AddWithValue("$pairingCodeHash", pairingCodeHash);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Success(null));
            }

            return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Success(Map(reader)));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<SalrynTerminalPairingInvite?>.Fail(Error.Unexpected(
                "TerminalPairing.ReadByHashFailed",
                $"Failed to read pairing invite by hash: {ex.Message}")));
        }
    }

    public Task<Result<SalrynTerminalPairingInvite>> UpsertAsync(
        SalrynTerminalPairingInvite invite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string now = DateTimeOffset.UtcNow.ToString("O");

            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO salryn_terminal_pairing_invites (
                    pairing_id,
                    store_id,
                    host_id,
                    target_terminal_name,
                    target_terminal_role,
                    pairing_status,
                    pairing_code_hash,
                    created_at_utc,
                    expires_at_utc,
                    consumed_at_utc,
                    revoked_at_utc,
                    created_by_user_id,
                    created_at,
                    updated_at)
                VALUES (
                    $pairingId,
                    $storeId,
                    $hostId,
                    $targetTerminalName,
                    $targetTerminalRole,
                    $pairingStatus,
                    $pairingCodeHash,
                    $createdAtUtc,
                    $expiresAtUtc,
                    $consumedAtUtc,
                    $revokedAtUtc,
                    $createdByUserId,
                    $createdAt,
                    $updatedAt)
                ON CONFLICT(pairing_id) DO UPDATE SET
                    store_id = excluded.store_id,
                    host_id = excluded.host_id,
                    target_terminal_name = excluded.target_terminal_name,
                    target_terminal_role = excluded.target_terminal_role,
                    pairing_status = excluded.pairing_status,
                    pairing_code_hash = excluded.pairing_code_hash,
                    created_at_utc = excluded.created_at_utc,
                    expires_at_utc = excluded.expires_at_utc,
                    consumed_at_utc = excluded.consumed_at_utc,
                    revoked_at_utc = excluded.revoked_at_utc,
                    created_by_user_id = excluded.created_by_user_id,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$pairingId", invite.PairingId.ToString("D"));
            command.Parameters.AddWithValue("$storeId", invite.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$hostId", invite.HostId.ToString("D"));
            command.Parameters.AddWithValue("$targetTerminalName", invite.TargetTerminalName);
            command.Parameters.AddWithValue("$targetTerminalRole", (int)invite.TargetTerminalRole);
            command.Parameters.AddWithValue("$pairingStatus", (int)invite.Status);
            command.Parameters.AddWithValue("$pairingCodeHash", invite.PairingCodeHash);
            command.Parameters.AddWithValue("$createdAtUtc", invite.CreatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$expiresAtUtc", invite.ExpiresAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$consumedAtUtc", invite.ConsumedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$revokedAtUtc", invite.RevokedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdByUserId", invite.CreatedByUserId.ToString("D"));
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);

            command.ExecuteNonQuery();
            return Task.FromResult(Result<SalrynTerminalPairingInvite>.Success(invite));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<SalrynTerminalPairingInvite>.Fail(Error.Unexpected(
                "TerminalPairing.UpsertFailed",
                $"Failed to upsert pairing invite: {ex.Message}")));
        }
    }

    private const string SelectSql = """
        SELECT
            pairing_id,
            store_id,
            host_id,
            target_terminal_name,
            target_terminal_role,
            pairing_status,
            pairing_code_hash,
            created_at_utc,
            expires_at_utc,
            consumed_at_utc,
            revoked_at_utc,
            created_by_user_id
        FROM salryn_terminal_pairing_invites
        """;

    private static SalrynTerminalPairingInvite Map(SqliteDataReader reader)
    {
        return new SalrynTerminalPairingInvite(
            PairingId: Guid.Parse(reader.GetString(0)),
            StoreId: Guid.Parse(reader.GetString(1)),
            HostId: Guid.Parse(reader.GetString(2)),
            TargetTerminalName: reader.GetString(3),
            TargetTerminalRole: (TerminalRegistrationRole)reader.GetInt32(4),
            Status: (PairingInviteStatus)reader.GetInt32(5),
            PairingCodeHash: reader.GetString(6),
            CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(7)),
            ExpiresAtUtc: DateTimeOffset.Parse(reader.GetString(8)),
            ConsumedAtUtc: reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            RevokedAtUtc: reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            CreatedByUserId: Guid.Parse(reader.GetString(11)));
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteTerminalCredentialRepository : SqliteRepositoryBase
{
    public SqliteTerminalCredentialRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<RemoteTerminalCredential?>> GetCredentialByTerminalIdAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = CredentialSelectSql + " WHERE terminal_id = $terminalId LIMIT 1;";
            command.Parameters.AddWithValue("$terminalId", terminalId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            return Task.FromResult(Result<RemoteTerminalCredential?>.Success(reader.Read() ? MapCredential(reader) : null));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<RemoteTerminalCredential?>.Fail(Error.Unexpected(
                "TerminalCredential.ReadFailed",
                $"Failed to read terminal credential: {ex.Message}")));
        }
    }

    public Task<Result<RemoteTerminalCredential>> UpsertCredentialAsync(
        RemoteTerminalCredential credential,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string now = DateTimeOffset.UtcNow.ToString("O");
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO salryn_terminal_credentials (
                    terminal_id,
                    store_id,
                    host_id,
                    credential_hash,
                    issued_at_utc,
                    last_used_at_utc,
                    revoked_at_utc,
                    created_from_pairing_id,
                    created_by_user_id,
                    created_at,
                    updated_at)
                VALUES (
                    $terminalId,
                    $storeId,
                    $hostId,
                    $credentialHash,
                    $issuedAtUtc,
                    $lastUsedAtUtc,
                    $revokedAtUtc,
                    $createdFromPairingId,
                    $createdByUserId,
                    $createdAt,
                    $updatedAt)
                ON CONFLICT(terminal_id) DO UPDATE SET
                    store_id = excluded.store_id,
                    host_id = excluded.host_id,
                    credential_hash = excluded.credential_hash,
                    issued_at_utc = excluded.issued_at_utc,
                    last_used_at_utc = excluded.last_used_at_utc,
                    revoked_at_utc = excluded.revoked_at_utc,
                    created_from_pairing_id = excluded.created_from_pairing_id,
                    created_by_user_id = excluded.created_by_user_id,
                    updated_at = excluded.updated_at;
                """;

            command.Parameters.AddWithValue("$terminalId", credential.TerminalId.ToString("D"));
            command.Parameters.AddWithValue("$storeId", credential.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$hostId", credential.HostId.ToString("D"));
            command.Parameters.AddWithValue("$credentialHash", credential.CredentialHash);
            command.Parameters.AddWithValue("$issuedAtUtc", credential.IssuedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$lastUsedAtUtc", credential.LastUsedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$revokedAtUtc", credential.RevokedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdFromPairingId", credential.CreatedFromPairingId?.ToString("D") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdByUserId", credential.CreatedByUserId?.ToString("D") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();

            return Task.FromResult(Result<RemoteTerminalCredential>.Success(credential));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<RemoteTerminalCredential>.Fail(Error.Unexpected(
                "TerminalCredential.UpsertFailed",
                $"Failed to persist terminal credential: {ex.Message}")));
        }
    }

    public Task<Result> UpdateCredentialLastUsedAsync(
        Guid terminalId,
        DateTimeOffset lastUsedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE salryn_terminal_credentials
                SET last_used_at_utc = $lastUsedAtUtc,
                    updated_at = $updatedAt
                WHERE terminal_id = $terminalId;
                """;
            command.Parameters.AddWithValue("$terminalId", terminalId.ToString("D"));
            command.Parameters.AddWithValue("$lastUsedAtUtc", lastUsedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result.Fail(Error.Unexpected(
                "TerminalCredential.LastUsedUpdateFailed",
                $"Failed to update terminal credential use: {ex.Message}")));
        }
    }

    public Task<Result> InsertHandshakeSessionAsync(
        RemoteClientHandshakeSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string now = DateTimeOffset.UtcNow.ToString("O");
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO salryn_remote_client_handshakes (
                    handshake_id,
                    terminal_id,
                    store_id,
                    host_id,
                    origin_address,
                    client_machine_name,
                    client_fingerprint,
                    created_at_utc,
                    expires_at_utc,
                    handshake_status,
                    created_at,
                    updated_at)
                VALUES (
                    $handshakeId,
                    $terminalId,
                    $storeId,
                    $hostId,
                    $originAddress,
                    $clientMachineName,
                    $clientFingerprint,
                    $createdAtUtc,
                    $expiresAtUtc,
                    $handshakeStatus,
                    $createdAt,
                    $updatedAt);
                """;
            command.Parameters.AddWithValue("$handshakeId", session.HandshakeId.ToString("D"));
            command.Parameters.AddWithValue("$terminalId", session.TerminalId.ToString("D"));
            command.Parameters.AddWithValue("$storeId", session.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$hostId", session.HostId.ToString("D"));
            command.Parameters.AddWithValue("$originAddress", session.OriginAddress);
            command.Parameters.AddWithValue("$clientMachineName", string.IsNullOrWhiteSpace(session.ClientMachineName) ? DBNull.Value : session.ClientMachineName.Trim());
            command.Parameters.AddWithValue("$clientFingerprint", string.IsNullOrWhiteSpace(session.ClientFingerprint) ? DBNull.Value : session.ClientFingerprint.Trim());
            command.Parameters.AddWithValue("$createdAtUtc", session.CreatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$expiresAtUtc", session.ExpiresAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$handshakeStatus", (int)session.Status);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();
            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result.Fail(Error.Unexpected(
                "RemoteHandshake.InsertFailed",
                $"Failed to persist remote handshake: {ex.Message}")));
        }
    }

    public Task<Result<RemoteClientHandshakeSession?>> GetActiveHandshakeSessionAsync(
        Guid terminalId,
        Guid handshakeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    handshake_id,
                    terminal_id,
                    store_id,
                    host_id,
                    origin_address,
                    client_machine_name,
                    client_fingerprint,
                    created_at_utc,
                    expires_at_utc,
                    handshake_status
                FROM salryn_remote_client_handshakes
                WHERE handshake_id = $handshakeId
                  AND terminal_id = $terminalId
                  AND handshake_status = $succeededStatus
                  AND expires_at_utc > $nowUtc
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$handshakeId", handshakeId.ToString("D"));
            command.Parameters.AddWithValue("$terminalId", terminalId.ToString("D"));
            command.Parameters.AddWithValue("$succeededStatus", (int)RemoteClientHandshakeStatus.Succeeded);
            command.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("O"));

            using SqliteDataReader reader = command.ExecuteReader();
            return Task.FromResult(Result<RemoteClientHandshakeSession?>.Success(reader.Read() ? MapSession(reader) : null));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<RemoteClientHandshakeSession?>.Fail(Error.Unexpected(
                "RemoteHandshake.ReadFailed",
                $"Failed to read remote handshake: {ex.Message}")));
        }
    }

    private const string CredentialSelectSql = """
        SELECT
            terminal_id,
            store_id,
            host_id,
            credential_hash,
            issued_at_utc,
            last_used_at_utc,
            revoked_at_utc,
            created_from_pairing_id,
            created_by_user_id
        FROM salryn_terminal_credentials
        """;

    private static RemoteTerminalCredential MapCredential(SqliteDataReader reader)
    {
        return new RemoteTerminalCredential(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)));
    }

    private static RemoteClientHandshakeSession MapSession(SqliteDataReader reader)
    {
        return new RemoteClientHandshakeSession(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)),
            (RemoteClientHandshakeStatus)reader.GetInt32(9));
    }
}

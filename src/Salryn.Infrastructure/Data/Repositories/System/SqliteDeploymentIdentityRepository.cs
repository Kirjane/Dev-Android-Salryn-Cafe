using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Migrations;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteDeploymentIdentityRepository : SqliteRepositoryBase
{
    public SqliteDeploymentIdentityRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<DeploymentIdentitySnapshot?>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    store_id,
                    store_name,
                    host_id,
                    installation_role,
                    host_display_name,
                    is_database_authority,
                    is_official_sequence_authority,
                    is_journal_authority,
                    is_backup_authority,
                    is_client_only,
                    lan_transport_enabled,
                    terminal_id,
                    terminal_name,
                    terminal_role,
                    is_local_terminal,
                    terminal_registered_at,
                    host_address,
                    host_port,
                    pairing_credential_label,
                    host_fingerprint_placeholder,
                    connection_status,
                    created_at,
                    updated_at
                FROM deployment_identity
                WHERE id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", DeploymentIdentitySchemaMigration.DeploymentIdentityRowId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return Task.FromResult(Result<DeploymentIdentitySnapshot?>.Success(null));
            }

            DeploymentIdentitySnapshot snapshot = Map(reader);
            return Task.FromResult(Result<DeploymentIdentitySnapshot?>.Success(snapshot));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<DeploymentIdentitySnapshot?>.Fail(Error.Unexpected(
                "DeploymentIdentity.ReadFailed",
                $"Failed to read deployment identity: {ex.Message}")));
        }
    }

    public Task<Result<DeploymentIdentitySnapshot>> UpsertAsync(
        DeploymentIdentitySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO deployment_identity (
                    id,
                    store_id,
                    store_name,
                    host_id,
                    installation_role,
                    host_display_name,
                    is_database_authority,
                    is_official_sequence_authority,
                    is_journal_authority,
                    is_backup_authority,
                    is_client_only,
                    lan_transport_enabled,
                    terminal_id,
                    terminal_name,
                    terminal_role,
                    is_local_terminal,
                    terminal_registered_at,
                    host_address,
                    host_port,
                    pairing_credential_label,
                    host_fingerprint_placeholder,
                    connection_status,
                    created_at,
                    updated_at)
                VALUES (
                    $id,
                    $storeId,
                    $storeName,
                    $hostId,
                    $installationRole,
                    $hostDisplayName,
                    $isDatabaseAuthority,
                    $isOfficialSequenceAuthority,
                    $isJournalAuthority,
                    $isBackupAuthority,
                    $isClientOnly,
                    $lanTransportEnabled,
                    $terminalId,
                    $terminalName,
                    $terminalRole,
                    $isLocalTerminal,
                    $terminalRegisteredAt,
                    $hostAddress,
                    $hostPort,
                    $pairingCredentialLabel,
                    $hostFingerprintPlaceholder,
                    $connectionStatus,
                    $createdAt,
                    $updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    store_id = excluded.store_id,
                    store_name = excluded.store_name,
                    host_id = excluded.host_id,
                    installation_role = excluded.installation_role,
                    host_display_name = excluded.host_display_name,
                    is_database_authority = excluded.is_database_authority,
                    is_official_sequence_authority = excluded.is_official_sequence_authority,
                    is_journal_authority = excluded.is_journal_authority,
                    is_backup_authority = excluded.is_backup_authority,
                    is_client_only = excluded.is_client_only,
                    lan_transport_enabled = excluded.lan_transport_enabled,
                    terminal_id = excluded.terminal_id,
                    terminal_name = excluded.terminal_name,
                    terminal_role = excluded.terminal_role,
                    is_local_terminal = excluded.is_local_terminal,
                    terminal_registered_at = excluded.terminal_registered_at,
                    host_address = excluded.host_address,
                    host_port = excluded.host_port,
                    pairing_credential_label = excluded.pairing_credential_label,
                    host_fingerprint_placeholder = excluded.host_fingerprint_placeholder,
                    connection_status = excluded.connection_status,
                    updated_at = excluded.updated_at;
                """;

            command.Parameters.AddWithValue("$id", DeploymentIdentitySchemaMigration.DeploymentIdentityRowId);
            command.Parameters.AddWithValue("$storeId", snapshot.Store.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$storeName", snapshot.Store.StoreName);
            command.Parameters.AddWithValue("$hostId", snapshot.Host.HostId.ToString("D"));
            command.Parameters.AddWithValue("$installationRole", (int)snapshot.Host.InstallationRole);
            command.Parameters.AddWithValue("$hostDisplayName", snapshot.Host.HostDisplayName);
            command.Parameters.AddWithValue("$isDatabaseAuthority", snapshot.Host.IsDatabaseAuthority ? 1 : 0);
            command.Parameters.AddWithValue("$isOfficialSequenceAuthority", snapshot.Host.IsOfficialSequenceAuthority ? 1 : 0);
            command.Parameters.AddWithValue("$isJournalAuthority", snapshot.Host.IsJournalAuthority ? 1 : 0);
            command.Parameters.AddWithValue("$isBackupAuthority", snapshot.Host.IsBackupAuthority ? 1 : 0);
            command.Parameters.AddWithValue("$isClientOnly", snapshot.Authority.IsClientOnly ? 1 : 0);
            command.Parameters.AddWithValue("$lanTransportEnabled", snapshot.Authority.LanTransportEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$terminalId", snapshot.Terminal.TerminalId.ToString("D"));
            command.Parameters.AddWithValue("$terminalName", snapshot.Terminal.TerminalName);
            command.Parameters.AddWithValue("$terminalRole", (int)snapshot.Terminal.TerminalRole);
            command.Parameters.AddWithValue("$isLocalTerminal", snapshot.Terminal.IsLocalTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$terminalRegisteredAt", snapshot.Terminal.RegisteredAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$hostAddress", (object?)snapshot.ClientConnection.HostAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("$hostPort", (object?)snapshot.ClientConnection.HostPort ?? DBNull.Value);
            command.Parameters.AddWithValue("$pairingCredentialLabel", (object?)snapshot.ClientConnection.PairingCredentialLabel ?? DBNull.Value);
            command.Parameters.AddWithValue("$hostFingerprintPlaceholder", (object?)snapshot.ClientConnection.HostFingerprintPlaceholder ?? DBNull.Value);
            command.Parameters.AddWithValue("$connectionStatus", (int)snapshot.ClientConnection.ConnectionStatus);
            command.Parameters.AddWithValue("$createdAt", snapshot.Store.CreatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", snapshot.UpdatedAtUtc.ToString("O"));

            command.ExecuteNonQuery();
            transaction.Commit();

            return Task.FromResult(Result<DeploymentIdentitySnapshot>.Success(snapshot));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<DeploymentIdentitySnapshot>.Fail(Error.Unexpected(
                "DeploymentIdentity.WriteFailed",
                $"Failed to persist deployment identity: {ex.Message}")));
        }
    }

    private static DeploymentIdentitySnapshot Map(SqliteDataReader reader)
    {
        InstallationRole installationRole = (InstallationRole)reader.GetInt32(3);
        MachineAuthority authority = new(
            IsDatabaseAuthority: reader.GetInt32(5) == 1,
            IsOfficialSequenceAuthority: reader.GetInt32(6) == 1,
            IsJournalAuthority: reader.GetInt32(7) == 1,
            IsBackupAuthority: reader.GetInt32(8) == 1,
            IsClientOnly: reader.GetInt32(9) == 1,
            LanTransportEnabled: reader.GetInt32(10) == 1);

        DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(21));
        DateTimeOffset updatedAt = DateTimeOffset.Parse(reader.GetString(22));

        return new DeploymentIdentitySnapshot(
            Store: new DeploymentStoreIdentity(
                StoreId: Guid.Parse(reader.GetString(0)),
                StoreName: reader.GetString(1),
                CreatedAtUtc: createdAt,
                UpdatedAtUtc: updatedAt),
            Host: new DeploymentHostIdentity(
                HostId: Guid.Parse(reader.GetString(2)),
                InstallationRole: installationRole,
                HostDisplayName: reader.GetString(4),
                IsDatabaseAuthority: authority.IsDatabaseAuthority,
                IsOfficialSequenceAuthority: authority.IsOfficialSequenceAuthority,
                IsJournalAuthority: authority.IsJournalAuthority,
                IsBackupAuthority: authority.IsBackupAuthority),
            Terminal: new DeploymentTerminalIdentity(
                TerminalId: Guid.Parse(reader.GetString(11)),
                TerminalName: reader.GetString(12),
                TerminalRole: (InstallationRole)reader.GetInt32(13),
                IsLocalTerminal: reader.GetInt32(14) == 1,
                RegisteredAtUtc: DateTimeOffset.Parse(reader.GetString(15))),
            ClientConnection: new ClientHostConnectionConfig(
                HostAddress: reader.IsDBNull(16) ? null : reader.GetString(16),
                HostPort: reader.IsDBNull(17) ? null : reader.GetInt32(17),
                PairingCredentialLabel: reader.IsDBNull(18) ? null : reader.GetString(18),
                HostFingerprintPlaceholder: reader.IsDBNull(19) ? null : reader.GetString(19),
                ConnectionStatus: (ClientHostConnectionStatus)reader.GetInt32(20)),
            Authority: authority,
            IsConfigured: true,
            UpdatedAtUtc: updatedAt);
    }
}

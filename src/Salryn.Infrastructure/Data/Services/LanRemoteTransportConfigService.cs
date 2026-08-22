using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Services;

public sealed class LanRemoteTransportConfigService : SqliteRepositoryBase, ILanRemoteTransportConfigService
{
    public LanRemoteTransportConfigService(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<LanRemoteTransportConfig>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT remote_transport_enabled, bind_mode, bind_address, port,
                       require_paired_terminal, require_active_terminal, reject_unknown_terminals,
                       allow_official_commands_remotely, allow_admin_commands_remotely,
                       updated_at_utc, updated_by_user_id
                FROM salryn_lan_transport_config
                WHERE id = 'default'
                LIMIT 1;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<LanRemoteTransportConfig>.Success(LanRemoteTransportConfig.CreateDefault()));
            }

            string? updatedByUserId = reader.IsDBNull(10) ? null : reader.GetString(10);
            LanRemoteTransportConfig config = new(
                RemoteTransportEnabled: reader.GetInt32(0) == 1,
                BindMode: (LanRemoteTransportBindMode)reader.GetInt32(1),
                BindAddress: reader.GetString(2),
                Port: reader.GetInt32(3),
                RequirePairedTerminal: reader.GetInt32(4) == 1,
                RequireActiveTerminal: reader.GetInt32(5) == 1,
                RejectUnknownTerminals: reader.GetInt32(6) == 1,
                AllowOfficialCommandsRemotely: reader.GetInt32(7) == 1,
                AllowAdminCommandsRemotely: reader.GetInt32(8) == 1,
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(9)),
                UpdatedByUserId: string.IsNullOrWhiteSpace(updatedByUserId) ? null : Guid.Parse(updatedByUserId));

            return Task.FromResult(Result<LanRemoteTransportConfig>.Success(config));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<LanRemoteTransportConfig>.Fail(Error.Unexpected(
                "LanTransportConfig.ReadFailed",
                "LAN transport configuration could not be read: " + ex.Message)));
        }
    }

    public Task<Result<LanRemoteTransportConfig>> SaveAsync(
        LanRemoteTransportConfigUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LanRemoteTransportConfig config = new(
            RemoteTransportEnabled: request.RemoteTransportEnabled,
            BindMode: request.BindMode,
            BindAddress: request.BindAddress.Trim(),
            Port: request.Port,
            RequirePairedTerminal: request.RequirePairedTerminal,
            RequireActiveTerminal: request.RequireActiveTerminal,
            RejectUnknownTerminals: request.RejectUnknownTerminals,
            AllowOfficialCommandsRemotely: request.AllowOfficialCommandsRemotely,
            AllowAdminCommandsRemotely: request.AllowAdminCommandsRemotely,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedByUserId: request.UpdatedByUserId);

        Result validation = SalrynLanRemoteTransportGate.ValidateConfig(config);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result<LanRemoteTransportConfig>.Fail(validation.ErrorDetail));
        }

        return SaveValidatedAsync(config, cancellationToken);
    }

    public Task<Result<LanRemoteTransportConfig>> DisableAsync(
        Guid? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        LanRemoteTransportConfig config = LanRemoteTransportConfig.CreateDefault() with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedByUserId = updatedByUserId
        };

        return SaveValidatedAsync(config, cancellationToken);
    }

    private Task<Result<LanRemoteTransportConfig>> SaveValidatedAsync(
        LanRemoteTransportConfig config,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO salryn_lan_transport_config (
                    id, remote_transport_enabled, bind_mode, bind_address, port,
                    require_paired_terminal, require_active_terminal, reject_unknown_terminals,
                    allow_official_commands_remotely, allow_admin_commands_remotely,
                    updated_at_utc, updated_by_user_id)
                VALUES ('default', $enabled, $bindMode, $bindAddress, $port,
                    $requirePaired, $requireActive, $rejectUnknown,
                    $allowOfficial, $allowAdmin, $updatedAt, $updatedBy)
                ON CONFLICT(id) DO UPDATE SET
                    remote_transport_enabled = excluded.remote_transport_enabled,
                    bind_mode = excluded.bind_mode,
                    bind_address = excluded.bind_address,
                    port = excluded.port,
                    require_paired_terminal = excluded.require_paired_terminal,
                    require_active_terminal = excluded.require_active_terminal,
                    reject_unknown_terminals = excluded.reject_unknown_terminals,
                    allow_official_commands_remotely = excluded.allow_official_commands_remotely,
                    allow_admin_commands_remotely = excluded.allow_admin_commands_remotely,
                    updated_at_utc = excluded.updated_at_utc,
                    updated_by_user_id = excluded.updated_by_user_id;
                """;
            command.Parameters.AddWithValue("$enabled", config.RemoteTransportEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$bindMode", (int)config.BindMode);
            command.Parameters.AddWithValue("$bindAddress", config.BindAddress);
            command.Parameters.AddWithValue("$port", config.Port);
            command.Parameters.AddWithValue("$requirePaired", config.RequirePairedTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$requireActive", config.RequireActiveTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$rejectUnknown", config.RejectUnknownTerminals ? 1 : 0);
            command.Parameters.AddWithValue("$allowOfficial", config.AllowOfficialCommandsRemotely ? 1 : 0);
            command.Parameters.AddWithValue("$allowAdmin", config.AllowAdminCommandsRemotely ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", config.UpdatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedBy", config.UpdatedByUserId?.ToString("D") ?? (object)DBNull.Value);
            command.ExecuteNonQuery();

            return Task.FromResult(Result<LanRemoteTransportConfig>.Success(config));
        }
        catch (SqliteException ex)
        {
            return Task.FromResult(Result<LanRemoteTransportConfig>.Fail(Error.Unexpected(
                "LanTransportConfig.SaveFailed",
                "LAN transport configuration could not be saved: " + ex.Message)));
        }
    }
}

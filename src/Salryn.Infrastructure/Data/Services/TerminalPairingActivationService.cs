using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Infrastructure.Data.Services;

public sealed class TerminalPairingActivationService : ITerminalPairingActivationService
{
    private readonly ITerminalPairingPreparationService _pairingPreparation;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteTerminalRegistryRepository _terminalRegistry;
    private readonly SqliteTerminalCredentialRepository _credentials;
    private readonly ILanRemoteTransportConfigService _remoteConfig;

    public TerminalPairingActivationService(
        ITerminalPairingPreparationService pairingPreparation,
        SqliteConnectionFactory connectionFactory,
        SqliteTerminalRegistryRepository terminalRegistry,
        SqliteTerminalCredentialRepository credentials,
        ILanRemoteTransportConfigService remoteConfig)
    {
        _pairingPreparation = pairingPreparation ?? throw new ArgumentNullException(nameof(pairingPreparation));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _terminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _remoteConfig = remoteConfig ?? throw new ArgumentNullException(nameof(remoteConfig));
    }

    public async Task<Result<TerminalPairingActivationResult>> ActivatePairingAsync(
        TerminalPairingActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result requestValidation = ValidateRequest(request);
        if (requestValidation.IsFailure)
        {
            return ActivationFailure(requestValidation.ErrorDetail);
        }

        Result remoteConfigValidation = await RequireRemoteActivationEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (remoteConfigValidation.IsFailure)
        {
            return ActivationFailure(remoteConfigValidation.ErrorDetail);
        }

        Result<SalrynTerminalPairingInvite> inviteResult = await _pairingPreparation
            .ValidatePairingCodeAsync(request.RawPairingCode, cancellationToken)
            .ConfigureAwait(false);

        if (inviteResult.IsFailure)
        {
            return ActivationFailure(inviteResult.ErrorDetail);
        }

        SalrynTerminalPairingInvite invite = inviteResult.Value;
        if (invite.PairingId != request.PairingId ||
            invite.StoreId != request.StoreId ||
            invite.HostId != request.HostId)
        {
            return ActivationFailure(Error.Forbidden(
                "TerminalPairingActivation.InviteScopeMismatch",
                "Pairing invite does not match the requested store or host."));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SalrynTerminalRegistration terminal = new(
            TerminalId: Guid.NewGuid(),
            StoreId: invite.StoreId,
            HostId: invite.HostId,
            TerminalName: string.IsNullOrWhiteSpace(request.RequestedTerminalName)
                ? invite.TargetTerminalName
                : request.RequestedTerminalName.Trim(),
            TerminalRole: request.RequestedTerminalRole,
            Status: TerminalRegistrationStatus.Active,
            RegisteredAtUtc: now,
            LastSeenAtUtc: now,
            DisabledAtUtc: null,
            RevokedAtUtc: null,
            CreatedByUserId: invite.CreatedByUserId,
            Notes: BuildNotes(request));

        string rawCredential = TerminalCredentialSecretHasher.GenerateTerminalCredential();
        string credentialHash = TerminalCredentialSecretHasher.HashTerminalCredential(rawCredential);

        RemoteTerminalCredential credential = new(
            TerminalId: terminal.TerminalId,
            StoreId: terminal.StoreId,
            HostId: terminal.HostId,
            CredentialHash: credentialHash,
            IssuedAtUtc: now,
            LastUsedAtUtc: null,
            RevokedAtUtc: null,
            CreatedFromPairingId: invite.PairingId,
            CreatedByUserId: invite.CreatedByUserId);

        Result atomicActivation = await SaveActivationAtomicallyAsync(
                invite,
                terminal,
                credential,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        if (atomicActivation.IsFailure)
        {
            return ActivationFailure(atomicActivation.ErrorDetail);
        }

        return Result<TerminalPairingActivationResult>.Success(
            TerminalPairingActivationResult.Success(terminal, rawCredential));
    }

    private async Task<Result> SaveActivationAtomicallyAsync(
        SalrynTerminalPairingInvite invite,
        SalrynTerminalRegistration terminal,
        RemoteTerminalCredential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            SqlitePragma.Apply(connection);

            using SqliteTransaction transaction = connection.BeginTransaction();

            int consumedRows = ExecuteNonQuery(connection, transaction, """
                UPDATE salryn_terminal_pairing_invites
                SET pairing_status = $consumedStatus,
                    consumed_at_utc = $consumedAtUtc,
                    updated_at = $updatedAt
                WHERE pairing_id = $pairingId
                  AND pairing_status = $issuedStatus;
                """,
                new Dictionary<string, object?>
                {
                    ["$consumedStatus"] = (int)PairingInviteStatus.Consumed,
                    ["$consumedAtUtc"] = now.ToString("O"),
                    ["$updatedAt"] = now.ToString("O"),
                    ["$pairingId"] = invite.PairingId.ToString("D"),
                    ["$issuedStatus"] = (int)PairingInviteStatus.Issued
                });

            if (consumedRows != 1)
            {
                transaction.Rollback();
                return Result.Fail(Error.Conflict(
                    "TerminalPairingActivation.InviteAlreadyConsumed",
                    "Pairing invite could not be consumed for activation."));
            }

            ExecuteNonQuery(connection, transaction, """
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
                    NULL,
                    NULL,
                    $createdByUserId,
                    $notes,
                    $createdAt,
                    $updatedAt);
                """,
                new Dictionary<string, object?>
                {
                    ["$terminalId"] = terminal.TerminalId.ToString("D"),
                    ["$storeId"] = terminal.StoreId.ToString("D"),
                    ["$hostId"] = terminal.HostId.ToString("D"),
                    ["$terminalName"] = terminal.TerminalName,
                    ["$terminalRole"] = (int)terminal.TerminalRole,
                    ["$terminalStatus"] = (int)terminal.Status,
                    ["$registeredAtUtc"] = terminal.RegisteredAtUtc.ToString("O"),
                    ["$lastSeenAtUtc"] = terminal.LastSeenAtUtc?.ToString("O") ?? (object)DBNull.Value,
                    ["$createdByUserId"] = terminal.CreatedByUserId.ToString("D"),
                    ["$notes"] = terminal.Notes ?? (object)DBNull.Value,
                    ["$createdAt"] = now.ToString("O"),
                    ["$updatedAt"] = now.ToString("O")
                });

            ExecuteNonQuery(connection, transaction, """
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
                    NULL,
                    NULL,
                    $createdFromPairingId,
                    $createdByUserId,
                    $createdAt,
                    $updatedAt);
                """,
                new Dictionary<string, object?>
                {
                    ["$terminalId"] = credential.TerminalId.ToString("D"),
                    ["$storeId"] = credential.StoreId.ToString("D"),
                    ["$hostId"] = credential.HostId.ToString("D"),
                    ["$credentialHash"] = credential.CredentialHash,
                    ["$issuedAtUtc"] = credential.IssuedAtUtc.ToString("O"),
                    ["$createdFromPairingId"] = credential.CreatedFromPairingId?.ToString("D") ?? (object)DBNull.Value,
                    ["$createdByUserId"] = credential.CreatedByUserId?.ToString("D") ?? (object)DBNull.Value,
                    ["$createdAt"] = now.ToString("O"),
                    ["$updatedAt"] = now.ToString("O")
                });

            transaction.Commit();
            return Result.Success();
        }
        catch (SqliteException ex)
        {
            return Result.Fail(Error.Unexpected(
                "TerminalPairingActivation.AtomicSaveFailed",
                "Pairing activation could not be persisted: " + ex.Message));
        }
    }

    private static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (KeyValuePair<string, object?> parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private async Task<Result> RequireRemoteActivationEnabledAsync(CancellationToken cancellationToken)
    {
        Result<LanRemoteTransportConfig> configResult = await _remoteConfig
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configResult.IsFailure)
        {
            return Result.Fail(configResult.ErrorDetail);
        }

        LanRemoteTransportConfig config = configResult.Value;
        Result configValidation = SalrynLanRemoteTransportGate.ValidateConfig(config);
        if (configValidation.IsFailure)
        {
            return configValidation;
        }

        if (!config.RemoteTransportEnabled)
        {
            return Result.Fail(Error.Forbidden(
                "TerminalPairingActivation.RemoteDisabled",
                "Remote transport must be explicitly enabled before pairing activation."));
        }

        return Result.Success();
    }

    private static Result ValidateRequest(TerminalPairingActivationRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "TerminalPairingActivation.RequestRequired",
                "Pairing activation request is required."));
        }

        if (request.PairingId == Guid.Empty || request.StoreId == Guid.Empty || request.HostId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "TerminalPairingActivation.ScopeRequired",
                "Pairing, store, and host IDs are required."));
        }

        if (string.IsNullOrWhiteSpace(request.RawPairingCode))
        {
            return Result.Fail(Error.Validation(
                "TerminalPairingActivation.CodeRequired",
                "Raw pairing code is required for activation and is never persisted."));
        }

        if (string.IsNullOrWhiteSpace(request.OriginAddress))
        {
            return Result.Fail(Error.Validation(
                "TerminalPairingActivation.OriginRequired",
                "Pairing activation origin address is required."));
        }

        if (request.RequestedAtUtc == default)
        {
            return Result.Fail(Error.Validation(
                "TerminalPairingActivation.TimestampRequired",
                "Pairing activation timestamp is required."));
        }

        return Result.Success();
    }

    private static string BuildNotes(TerminalPairingActivationRequest request)
    {
        string machine = string.IsNullOrWhiteSpace(request.ClientMachineName)
            ? "unknown-machine"
            : request.ClientMachineName.Trim();

        string activation = "Activated by LAN-M6 remote pairing handshake from " + machine + ".";
        if (string.IsNullOrWhiteSpace(request.TerminalLicenseId)
            || string.IsNullOrWhiteSpace(request.LicenseTerminalId)
            || string.IsNullOrWhiteSpace(request.LicenseMachineCode))
        {
            return activation;
        }

        return TerminalLicenseTrustMetadata.Build(
            request.TerminalLicenseId,
            request.LicenseTerminalId,
            request.LicenseMachineCode,
            request.TerminalLicenseExpiresAt) + " " + activation;
    }

    private static Result<TerminalPairingActivationResult> ActivationFailure(Error error) =>
        Result<TerminalPairingActivationResult>.Success(
            TerminalPairingActivationResult.Failure(error.Code, error.Message));
}

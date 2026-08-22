using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Infrastructure.Data.Services;

public sealed class RemoteClientHandshakeService : IRemoteClientHandshakeService
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PairedTerminalRefreshLifetime = TimeSpan.FromDays(7);
    private static readonly RemoteClientAllowedCategory[] SafeRemoteCategories =
    {
        RemoteClientAllowedCategory.StatusRead,
        RemoteClientAllowedCategory.ProductRead,
        RemoteClientAllowedCategory.DashboardRead,
        RemoteClientAllowedCategory.TerminalStatusRead
    };

    private readonly SqliteTerminalRegistryRepository _terminalRegistry;
    private readonly SqliteTerminalCredentialRepository _credentials;
    private readonly ILanRemoteTransportConfigService _remoteConfig;

    public RemoteClientHandshakeService(
        SqliteTerminalRegistryRepository terminalRegistry,
        SqliteTerminalCredentialRepository credentials,
        ILanRemoteTransportConfigService remoteConfig)
    {
        _terminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _remoteConfig = remoteConfig ?? throw new ArgumentNullException(nameof(remoteConfig));
    }

    public async Task<Result<RemoteClientHandshakeResult>> HandshakeAsync(
        RemoteClientHandshakeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result requestValidation = ValidateRequest(request);
        if (requestValidation.IsFailure)
        {
            return HandshakeFailure(request?.TerminalId ?? Guid.Empty, null, requestValidation.ErrorDetail);
        }

        Result remoteConfigValidation = await RequireRemoteHandshakeEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (remoteConfigValidation.IsFailure)
        {
            return HandshakeFailure(request.TerminalId, null, remoteConfigValidation.ErrorDetail);
        }

        Result<SalrynTerminalRegistration?> terminalResult = await _terminalRegistry
            .GetByIdAsync(request.TerminalId, cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure)
        {
            return HandshakeFailure(request.TerminalId, null, terminalResult.ErrorDetail);
        }

        SalrynTerminalRegistration? terminal = terminalResult.Value;
        if (terminal is null)
        {
            return HandshakeFailure(request.TerminalId, null, Error.NotFound(
                "RemoteHandshake.TerminalUnknown",
                "Remote terminal is not registered."));
        }

        if (terminal.StoreId != request.StoreId || terminal.HostId != request.HostId)
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, Error.Forbidden(
                "RemoteHandshake.TerminalScopeMismatch",
                "Remote terminal does not belong to the requested store or host."));
        }

        if (terminal.Status != TerminalRegistrationStatus.Active)
        {
            string code = terminal.Status switch
            {
                TerminalRegistrationStatus.PendingPairing => "RemoteHandshake.PendingTerminalRejected",
                TerminalRegistrationStatus.Disabled => "RemoteHandshake.DisabledTerminalRejected",
                TerminalRegistrationStatus.Revoked => "RemoteHandshake.RevokedTerminalRejected",
                _ => "RemoteHandshake.TerminalNotActive"
            };

            return HandshakeFailure(request.TerminalId, terminal.Status, Error.Forbidden(
                code,
                "Remote terminal must be active before handshake."));
        }

        Result<RemoteTerminalCredential?> credentialResult = await _credentials
            .GetCredentialByTerminalIdAsync(request.TerminalId, cancellationToken)
            .ConfigureAwait(false);

        if (credentialResult.IsFailure)
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, credentialResult.ErrorDetail);
        }

        RemoteTerminalCredential? credential = credentialResult.Value;
        if (credential is null || credential.RevokedAtUtc is not null)
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, Error.Forbidden(
                "RemoteHandshake.CredentialMissing",
                "Remote terminal credential is missing or revoked."));
        }

        string suppliedHash = TerminalCredentialSecretHasher.HashTerminalCredential(request.RawTerminalCredential);
        if (!string.Equals(suppliedHash, credential.CredentialHash, StringComparison.Ordinal))
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, Error.Forbidden(
                "RemoteHandshake.InvalidCredential",
                "Remote terminal credential is invalid."));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        RemoteClientHandshakeSession session = new(
            HandshakeId: Guid.NewGuid(),
            TerminalId: terminal.TerminalId,
            StoreId: terminal.StoreId,
            HostId: terminal.HostId,
            OriginAddress: request.OriginAddress.Trim(),
            ClientMachineName: TrimOrNull(request.ClientMachineName),
            ClientFingerprint: TrimOrNull(request.ClientFingerprint),
            CreatedAtUtc: now,
            ExpiresAtUtc: now.Add(DefaultSessionLifetime),
            Status: RemoteClientHandshakeStatus.Succeeded);

        Result insertHandshake = await _credentials
            .InsertHandshakeSessionAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (insertHandshake.IsFailure)
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, insertHandshake.ErrorDetail);
        }

        Result updateCredential = await _credentials
            .UpdateCredentialLastUsedAsync(terminal.TerminalId, now, cancellationToken)
            .ConfigureAwait(false);
        if (updateCredential.IsFailure)
        {
            return HandshakeFailure(request.TerminalId, terminal.Status, updateCredential.ErrorDetail);
        }

        SalrynTerminalRegistration seen = terminal with { LastSeenAtUtc = now };
        await _terminalRegistry.UpsertAsync(seen, cancellationToken).ConfigureAwait(false);

        return Result<RemoteClientHandshakeResult>.Success(
            RemoteClientHandshakeResult.Success(
                terminal.TerminalId,
                terminal.Status,
                session.HandshakeId,
                session.ExpiresAtUtc,
                SafeRemoteCategories));
    }

    public async Task<Result> ValidateHandshakeSessionAsync(
        Guid terminalId,
        Guid handshakeSessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (terminalId == Guid.Empty || handshakeSessionId == Guid.Empty)
        {
            return Result.Fail(Error.Forbidden(
                "RemoteHandshake.SessionRequired",
                "A successful remote handshake session is required."));
        }

        Result<RemoteClientHandshakeSession?> session = await _credentials
            .GetActiveHandshakeSessionAsync(terminalId, handshakeSessionId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (session.IsFailure)
        {
            return Result.Fail(session.ErrorDetail);
        }

        if (session.Value is null)
        {
            return Result.Fail(Error.Forbidden(
                "RemoteHandshake.SessionInvalid",
                "Remote handshake session is invalid or expired."));
        }

        return Result.Success();
    }

    public async Task<Result<RemoteClientHandshakeResult>> RefreshTrustedTerminalHandshakeAsync(
        Guid terminalId,
        string originAddress,
        string? clientMachineName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (terminalId == Guid.Empty)
        {
            return Result<RemoteClientHandshakeResult>.Fail(Error.Validation(
                "RemoteHandshake.TerminalRequired",
                "Terminal id is required to refresh a trusted remote handshake."));
        }

        if (string.IsNullOrWhiteSpace(originAddress))
        {
            return Result<RemoteClientHandshakeResult>.Fail(Error.Validation(
                "RemoteHandshake.OriginRequired",
                "Remote handshake origin address is required."));
        }

        Result remoteConfigValidation = await RequireRemoteHandshakeEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (remoteConfigValidation.IsFailure)
        {
            return HandshakeFailure(terminalId, null, remoteConfigValidation.ErrorDetail);
        }

        Result<SalrynTerminalRegistration?> terminalResult = await _terminalRegistry
            .GetByIdAsync(terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure)
        {
            return HandshakeFailure(terminalId, null, terminalResult.ErrorDetail);
        }

        SalrynTerminalRegistration? terminal = terminalResult.Value;
        if (terminal is null)
        {
            return HandshakeFailure(terminalId, null, Error.NotFound(
                "RemoteHandshake.TerminalUnknown",
                "Remote terminal is not registered."));
        }

        if (terminal.Status != TerminalRegistrationStatus.Active)
        {
            string code = terminal.Status switch
            {
                TerminalRegistrationStatus.Revoked => "RemoteHandshake.RevokedTerminalRejected",
                TerminalRegistrationStatus.Disabled => "RemoteHandshake.DisabledTerminalRejected",
                _ => "RemoteHandshake.TerminalNotActive"
            };

            return HandshakeFailure(terminalId, terminal.Status, Error.Forbidden(
                code,
                "Trusted terminal handshake refresh requires an active terminal."));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        RemoteClientHandshakeSession session = new(
            HandshakeId: Guid.NewGuid(),
            TerminalId: terminal.TerminalId,
            StoreId: terminal.StoreId,
            HostId: terminal.HostId,
            OriginAddress: originAddress.Trim(),
            ClientMachineName: TrimOrNull(clientMachineName),
            ClientFingerprint: null,
            CreatedAtUtc: now,
            ExpiresAtUtc: now.Add(PairedTerminalRefreshLifetime),
            Status: RemoteClientHandshakeStatus.Succeeded);

        Result insertHandshake = await _credentials
            .InsertHandshakeSessionAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (insertHandshake.IsFailure)
        {
            return HandshakeFailure(terminalId, terminal.Status, insertHandshake.ErrorDetail);
        }

        SalrynTerminalRegistration seen = terminal with { LastSeenAtUtc = now };
        await _terminalRegistry.UpsertAsync(seen, cancellationToken).ConfigureAwait(false);

        return Result<RemoteClientHandshakeResult>.Success(
            RemoteClientHandshakeResult.Success(
                terminal.TerminalId,
                terminal.Status,
                session.HandshakeId,
                session.ExpiresAtUtc,
                SafeRemoteCategories));
    }

    private async Task<Result> RequireRemoteHandshakeEnabledAsync(CancellationToken cancellationToken)
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
                "RemoteHandshake.RemoteDisabled",
                "Remote transport must be explicitly enabled before remote handshake."));
        }

        return Result.Success();
    }

    private static Result ValidateRequest(RemoteClientHandshakeRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "RemoteHandshake.RequestRequired",
                "Remote client handshake request is required."));
        }

        if (request.TerminalId == Guid.Empty || request.StoreId == Guid.Empty || request.HostId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "RemoteHandshake.ScopeRequired",
                "Terminal, store, and host IDs are required."));
        }

        if (string.IsNullOrWhiteSpace(request.RawTerminalCredential))
        {
            return Result.Fail(Error.Validation(
                "RemoteHandshake.CredentialRequired",
                "Raw terminal credential is required for handshake and is never persisted."));
        }

        if (string.IsNullOrWhiteSpace(request.OriginAddress))
        {
            return Result.Fail(Error.Validation(
                "RemoteHandshake.OriginRequired",
                "Remote handshake origin address is required."));
        }

        if (request.RequestedAtUtc == default)
        {
            return Result.Fail(Error.Validation(
                "RemoteHandshake.TimestampRequired",
                "Remote handshake timestamp is required."));
        }

        return Result.Success();
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<RemoteClientHandshakeResult> HandshakeFailure(
        Guid terminalId,
        TerminalRegistrationStatus? terminalStatus,
        Error error) =>
        Result<RemoteClientHandshakeResult>.Success(
            RemoteClientHandshakeResult.Failure(terminalId, terminalStatus, error.Code, error.Message));
}

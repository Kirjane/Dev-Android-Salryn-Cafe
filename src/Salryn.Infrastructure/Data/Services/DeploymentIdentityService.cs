using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Infrastructure.Data.Services;

public sealed class DeploymentIdentityService : IDeploymentIdentityService
{
    private readonly SqliteDeploymentIdentityRepository _repository;
    private DeploymentIdentitySnapshot? _cachedSnapshot;

    public DeploymentIdentityService(SqliteDeploymentIdentityRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public DeploymentIdentitySnapshot? CurrentSnapshot => _cachedSnapshot;

    public async Task<Result<DeploymentIdentitySnapshot?>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        Result<DeploymentIdentitySnapshot?> result = await _repository.GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _cachedSnapshot = result.Value;
        }

        return result;
    }

    public async Task<Result<bool>> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        Result<DeploymentIdentitySnapshot?> result = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<bool>.Fail(result.ErrorDetail);
        }

        return Result<bool>.Success(result.Value is not null && result.Value.IsConfigured);
    }

    public Task<Result<DeploymentIdentitySnapshot>> ConfigureHostServerAsync(
        HostServerConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        return ConfigureAuthorityRoleAsync(
            InstallationRole.HostServer,
            request.StoreName,
            request.HostDisplayName,
            request.TerminalName,
            clientConnection: null,
            cancellationToken);
    }

    public Task<Result<DeploymentIdentitySnapshot>> ConfigureAllInOneAsync(
        AllInOneConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        return ConfigureAuthorityRoleAsync(
            InstallationRole.AllInOne,
            request.StoreName,
            request.HostDisplayName,
            request.TerminalName,
            clientConnection: null,
            cancellationToken);
    }

    public Task<Result<DeploymentIdentitySnapshot>> ConfigureClientTerminalAsync(
        ClientTerminalConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.HostAddress))
        {
            return Task.FromResult(Result<DeploymentIdentitySnapshot>.Fail(Error.Validation(
                "DeploymentIdentity.HostAddressRequired",
                "Client Terminal configuration requires a host IP address or hostname.")));
        }

        if (request.HostPort <= 0)
        {
            return Task.FromResult(Result<DeploymentIdentitySnapshot>.Fail(Error.Validation(
                "DeploymentIdentity.HostPortRequired",
                "Client Terminal configuration requires a valid host port.")));
        }

        if (string.IsNullOrWhiteSpace(request.PairingCredentialLabel))
        {
            return Task.FromResult(Result<DeploymentIdentitySnapshot>.Fail(Error.Validation(
                "DeploymentIdentity.PairingCredentialRequired",
                "Client Terminal configuration requires a pairing credential label placeholder.")));
        }

        ClientHostConnectionConfig connection = new(
            HostAddress: request.HostAddress.Trim(),
            HostPort: request.HostPort,
            PairingCredentialLabel: request.PairingCredentialLabel.Trim(),
            HostFingerprintPlaceholder: null,
            ConnectionStatus: ClientHostConnectionStatus.TransportNotImplemented);

        return ConfigureAuthorityRoleAsync(
            InstallationRole.ClientTerminal,
            request.StoreName,
            hostDisplayName: Environment.MachineName,
            request.TerminalName,
            connection,
            cancellationToken);
    }

    public async Task<Result<DeploymentIdentitySnapshot>> ResetToAllInOneDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        Result<DeploymentIdentitySnapshot?> currentResult = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (currentResult.IsFailure)
        {
            return Result<DeploymentIdentitySnapshot>.Fail(currentResult.ErrorDetail);
        }

        string storeName = currentResult.Value?.Store.StoreName ?? "Salryn Store";

        return await ConfigureAllInOneAsync(
            new AllInOneConfigurationRequest(
                StoreName: storeName,
                HostDisplayName: Environment.MachineName,
                TerminalName: Environment.MachineName),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<DeploymentIdentitySnapshot>> ConfigureAuthorityRoleAsync(
        InstallationRole role,
        string storeName,
        string hostDisplayName,
        string terminalName,
        ClientHostConnectionConfig? clientConnection,
        CancellationToken cancellationToken)
    {
        Result validation = ValidateNames(storeName, hostDisplayName, terminalName, role);

        if (validation.IsFailure)
        {
            return Result<DeploymentIdentitySnapshot>.Fail(validation.ErrorDetail);
        }

        Result<DeploymentIdentitySnapshot?> currentResult = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (currentResult.IsFailure)
        {
            return Result<DeploymentIdentitySnapshot>.Fail(currentResult.ErrorDetail);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeploymentIdentitySnapshot? current = currentResult.Value;
        MachineAuthority authority = InstallationRoleAuthorityRules.ResolveAuthority(role);
        ClientHostConnectionConfig connection = clientConnection ?? new ClientHostConnectionConfig(
            HostAddress: null,
            HostPort: null,
            PairingCredentialLabel: null,
            HostFingerprintPlaceholder: null,
            ConnectionStatus: InstallationRoleAuthorityRules.ResolveConnectionStatus(role, null));

        DeploymentIdentitySnapshot snapshot = new(
            Store: new DeploymentStoreIdentity(
                StoreId: current?.Store.StoreId ?? Guid.NewGuid(),
                StoreName: storeName.Trim(),
                CreatedAtUtc: current?.Store.CreatedAtUtc ?? now,
                UpdatedAtUtc: now),
            Host: new DeploymentHostIdentity(
                HostId: current?.Host.HostId ?? Guid.NewGuid(),
                InstallationRole: role,
                HostDisplayName: hostDisplayName.Trim(),
                IsDatabaseAuthority: authority.IsDatabaseAuthority,
                IsOfficialSequenceAuthority: authority.IsOfficialSequenceAuthority,
                IsJournalAuthority: authority.IsJournalAuthority,
                IsBackupAuthority: authority.IsBackupAuthority),
            Terminal: new DeploymentTerminalIdentity(
                TerminalId: current?.Terminal.TerminalId ?? Guid.NewGuid(),
                TerminalName: terminalName.Trim(),
                TerminalRole: role,
                IsLocalTerminal: role != InstallationRole.ClientTerminal,
                RegisteredAtUtc: current?.Terminal.RegisteredAtUtc ?? now),
            ClientConnection: connection,
            Authority: authority,
            IsConfigured: true,
            UpdatedAtUtc: now);

        Result<DeploymentIdentitySnapshot> saveResult = await _repository.UpsertAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);

        if (saveResult.IsSuccess)
        {
            _cachedSnapshot = saveResult.Value;
        }

        return saveResult;
    }

    private static Result ValidateNames(
        string storeName,
        string hostDisplayName,
        string terminalName,
        InstallationRole role)
    {
        if (string.IsNullOrWhiteSpace(storeName))
        {
            return Result.Fail(Error.Validation(
                "DeploymentIdentity.StoreNameRequired",
                "Store name is required."));
        }

        if (role != InstallationRole.ClientTerminal && string.IsNullOrWhiteSpace(hostDisplayName))
        {
            return Result.Fail(Error.Validation(
                "DeploymentIdentity.HostDisplayNameRequired",
                "Host display name is required."));
        }

        if (string.IsNullOrWhiteSpace(terminalName))
        {
            return Result.Fail(Error.Validation(
                "DeploymentIdentity.TerminalNameRequired",
                "Terminal name is required."));
        }

        return Result.Success();
    }
}

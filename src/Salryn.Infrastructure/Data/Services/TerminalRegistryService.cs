using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Infrastructure.Data.Services;

public sealed class TerminalRegistryService : ITerminalRegistryService
{
    private readonly SqliteTerminalRegistryRepository _repository;
    private readonly IDeploymentIdentityService _deploymentIdentity;
    private readonly SalrynStoreHostAuthorityGuard _authorityGuard;

    public TerminalRegistryService(
        SqliteTerminalRegistryRepository repository,
        IDeploymentIdentityService deploymentIdentity,
        SalrynStoreHostAuthorityGuard authorityGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _deploymentIdentity = deploymentIdentity ?? throw new ArgumentNullException(nameof(deploymentIdentity));
        _authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    }

    public async Task<Result<IReadOnlyList<SalrynTerminalRegistration>>> ListTerminalsAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireViewAccess(actor);
        if (access.IsFailure)
        {
            return Result<IReadOnlyList<SalrynTerminalRegistration>>.Fail(access.ErrorDetail);
        }

        await EnsureLocalTerminalRegisteredAsync(actor, cancellationToken).ConfigureAwait(false);
        return await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SalrynTerminalRegistration?>> GetTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireViewAccess(actor);
        if (access.IsFailure)
        {
            return Result<SalrynTerminalRegistration?>.Fail(access.ErrorDetail);
        }

        return await _repository.GetByIdAsync(terminalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SalrynTerminalRegistration>> EnsureLocalTerminalRegisteredAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(hostAuthority.ErrorDetail);
        }

        Result<DeploymentIdentitySnapshot?> identityResult = await _deploymentIdentity
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (identityResult.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(identityResult.ErrorDetail);
        }

        DeploymentIdentitySnapshot? identity = identityResult.Value;
        if (identity is null)
        {
            return Result<SalrynTerminalRegistration>.Fail(Error.Validation(
                "TerminalRegistry.DeploymentIdentityRequired",
                "Deployment identity must be configured before registering local terminal."));
        }

        Result<SalrynTerminalRegistration?> existing = await _repository
            .GetByIdAsync(identity.Terminal.TerminalId, cancellationToken)
            .ConfigureAwait(false);

        if (existing.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(existing.ErrorDetail);
        }

        if (existing.Value is not null)
        {
            return Result<SalrynTerminalRegistration>.Success(existing.Value);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SalrynTerminalRegistration registration = new(
            TerminalId: identity.Terminal.TerminalId,
            StoreId: identity.Store.StoreId,
            HostId: identity.Host.HostId,
            TerminalName: identity.Terminal.TerminalName,
            TerminalRole: MapInstallationRole(identity.Host.InstallationRole),
            Status: TerminalRegistrationStatus.Active,
            RegisteredAtUtc: identity.Terminal.RegisteredAtUtc,
            LastSeenAtUtc: now,
            DisabledAtUtc: null,
            RevokedAtUtc: null,
            CreatedByUserId: actor.ActorUserId,
            Notes: "Registered from local deployment identity.");

        return await _repository.UpsertAsync(registration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SalrynTerminalRegistration>> DisableTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireManageAccess(actor);
        if (access.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(access.ErrorDetail);
        }

        return await UpdateStatusAsync(
            terminalId,
            TerminalRegistrationStatus.Disabled,
            disabledAtUtc: DateTimeOffset.UtcNow,
            revokedAtUtc: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SalrynTerminalRegistration>> RevokeTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireManageAccess(actor);
        if (access.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(access.ErrorDetail);
        }

        return await UpdateStatusAsync(
            terminalId,
            TerminalRegistrationStatus.Revoked,
            disabledAtUtc: null,
            revokedAtUtc: DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SalrynTerminalRegistration>> UpdateTerminalDisplayAsync(
        Guid terminalId,
        string terminalName,
        string? notes,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireManageAccess(actor);
        if (access.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(access.ErrorDetail);
        }

        if (string.IsNullOrWhiteSpace(terminalName))
        {
            return Result<SalrynTerminalRegistration>.Fail(Error.Validation(
                "TerminalRegistry.TerminalNameRequired",
                "Terminal name is required."));
        }

        Result<SalrynTerminalRegistration?> current = await _repository
            .GetByIdAsync(terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (current.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(current.ErrorDetail);
        }

        if (current.Value is null)
        {
            return Result<SalrynTerminalRegistration>.Fail(Error.NotFound(
                "TerminalRegistry.TerminalNotFound",
                "Terminal registration was not found."));
        }

        SalrynTerminalRegistration updated = current.Value with
        {
            TerminalName = terminalName.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        return await _repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> IsTerminalAcceptedForHostCommandsAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        Result<SalrynTerminalRegistration?> terminal = await _repository
            .GetByIdAsync(terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (terminal.IsFailure)
        {
            return Result.Fail(terminal.ErrorDetail);
        }

        if (terminal.Value is null)
        {
            return Result.Success();
        }

        if (terminal.Value.Status != TerminalRegistrationStatus.Active)
        {
            return Result.Fail(Error.Forbidden(
                "TerminalRegistry.TerminalNotActive",
                "Terminal is disabled, revoked, or pending pairing."));
        }

        return Result.Success();
    }

    private async Task<Result<SalrynTerminalRegistration>> UpdateStatusAsync(
        Guid terminalId,
        TerminalRegistrationStatus status,
        DateTimeOffset? disabledAtUtc,
        DateTimeOffset? revokedAtUtc,
        CancellationToken cancellationToken)
    {
        Result<SalrynTerminalRegistration?> current = await _repository
            .GetByIdAsync(terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (current.IsFailure)
        {
            return Result<SalrynTerminalRegistration>.Fail(current.ErrorDetail);
        }

        if (current.Value is null)
        {
            return Result<SalrynTerminalRegistration>.Fail(Error.NotFound(
                "TerminalRegistry.TerminalNotFound",
                "Terminal registration was not found."));
        }

        SalrynTerminalRegistration updated = current.Value with
        {
            Status = status,
            DisabledAtUtc = disabledAtUtc ?? current.Value.DisabledAtUtc,
            RevokedAtUtc = revokedAtUtc ?? current.Value.RevokedAtUtc
        };

        return await _repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private Result RequireViewAccess(TerminalRegistryActorContext actor)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return hostAuthority;
        }

        if (!HasPermission(actor, SalrynPermissions.StoreTerminalView))
        {
            return Result.Fail(Error.Forbidden(
                "TerminalRegistry.ViewForbidden",
                "You do not have permission to view terminal registry."));
        }

        return Result.Success();
    }

    private Result RequireManageAccess(TerminalRegistryActorContext actor)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return hostAuthority;
        }

        if (!HasPermission(actor, SalrynPermissions.StoreTerminalManage))
        {
            return Result.Fail(Error.Forbidden(
                "TerminalRegistry.ManageForbidden",
                "You do not have permission to manage terminal registry."));
        }

        return Result.Success();
    }

    private static bool HasPermission(TerminalRegistryActorContext actor, string permission) =>
        actor.Permissions.Contains(permission, StringComparer.Ordinal);

    private static TerminalRegistrationRole MapInstallationRole(InstallationRole role) =>
        role switch
        {
            InstallationRole.HostServer => TerminalRegistrationRole.HostConsole,
            InstallationRole.AllInOne => TerminalRegistrationRole.AllInOneLocalTerminal,
            InstallationRole.ClientTerminal => TerminalRegistrationRole.CashierClient,
            _ => TerminalRegistrationRole.CashierClient
        };
}

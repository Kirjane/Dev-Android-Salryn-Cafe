using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Infrastructure.Data.Services;

public sealed class TerminalPairingPreparationService : ITerminalPairingPreparationService
{
    private static readonly TimeSpan DefaultInviteLifetime = TimeSpan.FromHours(24);

    private readonly SqliteTerminalPairingInviteRepository _repository;
    private readonly IDeploymentIdentityService _deploymentIdentity;
    private readonly SalrynStoreHostAuthorityGuard _authorityGuard;

    public TerminalPairingPreparationService(
        SqliteTerminalPairingInviteRepository repository,
        IDeploymentIdentityService deploymentIdentity,
        SalrynStoreHostAuthorityGuard authorityGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _deploymentIdentity = deploymentIdentity ?? throw new ArgumentNullException(nameof(deploymentIdentity));
        _authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    }

    public async Task<Result<PairingInviteCreationResult>> CreatePairingInviteAsync(
        string targetTerminalName,
        TerminalRegistrationRole targetTerminalRole,
        TerminalRegistryActorContext actor,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        Result access = RequirePairingIssueAccess(actor);
        if (access.IsFailure)
        {
            return Result<PairingInviteCreationResult>.Fail(access.ErrorDetail);
        }

        if (string.IsNullOrWhiteSpace(targetTerminalName))
        {
            return Result<PairingInviteCreationResult>.Fail(Error.Validation(
                "TerminalPairing.TargetNameRequired",
                "Target terminal name is required."));
        }

        Result<DeploymentIdentitySnapshot?> identityResult = await _deploymentIdentity
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (identityResult.IsFailure)
        {
            return Result<PairingInviteCreationResult>.Fail(identityResult.ErrorDetail);
        }

        DeploymentIdentitySnapshot? identity = identityResult.Value;
        if (identity is null)
        {
            return Result<PairingInviteCreationResult>.Fail(Error.Validation(
                "TerminalPairing.DeploymentIdentityRequired",
                "Deployment identity must be configured before issuing pairing invites."));
        }

        string pairingCode = TerminalPairingSecretHasher.GeneratePairingCode();
        string pairingCodeHash = TerminalPairingSecretHasher.HashPairingCode(pairingCode);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan inviteLifetime = lifetime ?? DefaultInviteLifetime;

        SalrynTerminalPairingInvite invite = new(
            PairingId: Guid.NewGuid(),
            StoreId: identity.Store.StoreId,
            HostId: identity.Host.HostId,
            TargetTerminalName: targetTerminalName.Trim(),
            TargetTerminalRole: targetTerminalRole,
            Status: PairingInviteStatus.Issued,
            PairingCodeHash: pairingCodeHash,
            CreatedAtUtc: now,
            ExpiresAtUtc: now.Add(inviteLifetime),
            ConsumedAtUtc: null,
            RevokedAtUtc: null,
            CreatedByUserId: actor.ActorUserId);

        Result<SalrynTerminalPairingInvite> saved = await _repository
            .UpsertAsync(invite, cancellationToken)
            .ConfigureAwait(false);

        if (saved.IsFailure)
        {
            return Result<PairingInviteCreationResult>.Fail(saved.ErrorDetail);
        }

        return Result<PairingInviteCreationResult>.Success(
            new PairingInviteCreationResult(saved.Value, pairingCode));
    }

    public async Task<Result<SalrynTerminalPairingInvite>> RevokePairingInviteAsync(
        Guid pairingId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequirePairingRevokeAccess(actor);
        if (access.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(access.ErrorDetail);
        }

        Result<SalrynTerminalPairingInvite?> current = await _repository
            .GetByIdAsync(pairingId, cancellationToken)
            .ConfigureAwait(false);

        if (current.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(current.ErrorDetail);
        }

        if (current.Value is null)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.NotFound(
                "TerminalPairing.InviteNotFound",
                "Pairing invite was not found."));
        }

        SalrynTerminalPairingInvite revoked = current.Value with
        {
            Status = PairingInviteStatus.Revoked,
            RevokedAtUtc = DateTimeOffset.UtcNow
        };

        return await _repository.UpsertAsync(revoked, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<SalrynTerminalPairingInvite>>> ListActiveInvitesAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result access = RequireViewAccess(actor);
        if (access.IsFailure)
        {
            return Result<IReadOnlyList<SalrynTerminalPairingInvite>>.Fail(access.ErrorDetail);
        }

        Result<IReadOnlyList<SalrynTerminalPairingInvite>> invites = await _repository
            .ListActiveAsync(cancellationToken)
            .ConfigureAwait(false);

        if (invites.IsFailure)
        {
            return invites;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SalrynTerminalPairingInvite> active = new();

        foreach (SalrynTerminalPairingInvite invite in invites.Value)
        {
            if (invite.ExpiresAtUtc <= now)
            {
                SalrynTerminalPairingInvite expired = invite with
                {
                    Status = PairingInviteStatus.Expired
                };

                await _repository.UpsertAsync(expired, cancellationToken).ConfigureAwait(false);
                continue;
            }

            active.Add(invite);
        }

        return Result<IReadOnlyList<SalrynTerminalPairingInvite>>.Success(active);
    }

    public async Task<Result<SalrynTerminalPairingInvite>> ValidatePairingCodeAsync(
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(hostAuthority.ErrorDetail);
        }

        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.Validation(
                "TerminalPairing.CodeRequired",
                "Pairing code is required."));
        }

        string hash = TerminalPairingSecretHasher.HashPairingCode(pairingCode);
        Result<SalrynTerminalPairingInvite?> invite = await _repository
            .GetByCodeHashAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (invite.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(invite.ErrorDetail);
        }

        if (invite.Value is null)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.NotFound(
                "TerminalPairing.InviteNotFound",
                "Pairing invite was not found."));
        }

        return ValidateInviteState(invite.Value);
    }

    public async Task<Result<SalrynTerminalPairingInvite>> MarkPairingConsumedAsync(
        Guid pairingId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(hostAuthority.ErrorDetail);
        }

        Result<SalrynTerminalPairingInvite?> current = await _repository
            .GetByIdAsync(pairingId, cancellationToken)
            .ConfigureAwait(false);

        if (current.IsFailure)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(current.ErrorDetail);
        }

        if (current.Value is null)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.NotFound(
                "TerminalPairing.InviteNotFound",
                "Pairing invite was not found."));
        }

        Result<SalrynTerminalPairingInvite> valid = ValidateInviteState(current.Value);
        if (valid.IsFailure)
        {
            return valid;
        }

        SalrynTerminalPairingInvite consumed = current.Value with
        {
            Status = PairingInviteStatus.Consumed,
            ConsumedAtUtc = DateTimeOffset.UtcNow
        };

        return await _repository.UpsertAsync(consumed, cancellationToken).ConfigureAwait(false);
    }

    private static Result<SalrynTerminalPairingInvite> ValidateInviteState(SalrynTerminalPairingInvite invite)
    {
        if (invite.Status == PairingInviteStatus.Consumed)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.Validation(
                "TerminalPairing.InviteConsumed",
                "Pairing invite has already been consumed."));
        }

        if (invite.Status == PairingInviteStatus.Revoked)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.Validation(
                "TerminalPairing.InviteRevoked",
                "Pairing invite has been revoked."));
        }

        if (invite.Status == PairingInviteStatus.Expired || invite.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.Validation(
                "TerminalPairing.InviteExpired",
                "Pairing invite has expired."));
        }

        if (invite.Status != PairingInviteStatus.Issued)
        {
            return Result<SalrynTerminalPairingInvite>.Fail(Error.Validation(
                "TerminalPairing.InviteInvalid",
                "Pairing invite is not valid."));
        }

        return Result<SalrynTerminalPairingInvite>.Success(invite);
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
                "TerminalPairing.ViewForbidden",
                "You do not have permission to view pairing invites."));
        }

        return Result.Success();
    }

    private Result RequirePairingIssueAccess(TerminalRegistryActorContext actor)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return hostAuthority;
        }

        if (!HasPermission(actor, SalrynPermissions.StoreTerminalPairingIssue))
        {
            return Result.Fail(Error.Forbidden(
                "TerminalPairing.IssueForbidden",
                "You do not have permission to issue pairing invites."));
        }

        return Result.Success();
    }

    private Result RequirePairingRevokeAccess(TerminalRegistryActorContext actor)
    {
        Result hostAuthority = _authorityGuard.RequireDatabaseAuthority();
        if (hostAuthority.IsFailure)
        {
            return hostAuthority;
        }

        if (!HasPermission(actor, SalrynPermissions.StoreTerminalPairingRevoke))
        {
            return Result.Fail(Error.Forbidden(
                "TerminalPairing.RevokeForbidden",
                "You do not have permission to revoke pairing invites."));
        }

        return Result.Success();
    }

    private static bool HasPermission(TerminalRegistryActorContext actor, string permission) =>
        actor.Permissions.Contains(permission, StringComparer.Ordinal);
}

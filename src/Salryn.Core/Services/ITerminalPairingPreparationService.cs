using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.Services;

public interface ITerminalPairingPreparationService
{
    Task<Result<PairingInviteCreationResult>> CreatePairingInviteAsync(
        string targetTerminalName,
        TerminalRegistrationRole targetTerminalRole,
        TerminalRegistryActorContext actor,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalPairingInvite>> RevokePairingInviteAsync(
        Guid pairingId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SalrynTerminalPairingInvite>>> ListActiveInvitesAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalPairingInvite>> ValidatePairingCodeAsync(
        string pairingCode,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalPairingInvite>> MarkPairingConsumedAsync(
        Guid pairingId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);
}

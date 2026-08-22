using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.Services;

public interface ITerminalRegistryService
{
    Task<Result<IReadOnlyList<SalrynTerminalRegistration>>> ListTerminalsAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalRegistration?>> GetTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalRegistration>> EnsureLocalTerminalRegisteredAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalRegistration>> DisableTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enable a Disabled terminal only (not Revoked). Restores Active trust without re-pairing.
    /// </summary>
    Task<Result<SalrynTerminalRegistration>> EnableTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalRegistration>> RevokeTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalrynTerminalRegistration>> UpdateTerminalDisplayAsync(
        Guid terminalId,
        string terminalName,
        string? notes,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result> IsTerminalAcceptedForHostCommandsAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default);
}

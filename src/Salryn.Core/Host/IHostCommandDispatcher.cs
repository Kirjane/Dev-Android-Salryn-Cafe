namespace Salryn.Core.Host;

public interface IHostCommandHandler
{
    string CommandName { get; }

    HostCommandAuthorityRequirements AuthorityRequirements { get; }

    bool RequiresPayload { get; }

    Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default);
}

public interface IHostCommandDispatcher
{
    IReadOnlyCollection<string> RegisteredCommands { get; }

    Task<SalrynHostCommandResult<TResponse>> DispatchAsync<TPayload, TResponse>(
        SalrynHostCommandEnvelope<TPayload> envelope,
        CancellationToken cancellationToken = default);
}

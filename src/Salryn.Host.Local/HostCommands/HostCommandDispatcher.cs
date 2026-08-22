using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Services;

namespace Salryn.Host.Local.HostCommands;

public sealed class HostCommandDispatcher : IHostCommandDispatcher
{
    private readonly Dictionary<string, IHostCommandHandler> _handlers;
    private readonly SalrynHostCommandAuthorityGuard _authorityGuard;
    private readonly Func<DeploymentIdentitySnapshot?> _identityProvider;
    private readonly ITerminalRegistryService? _terminalRegistry;

    public HostCommandDispatcher(
        IEnumerable<IHostCommandHandler> handlers,
        SalrynHostCommandAuthorityGuard authorityGuard,
        Func<DeploymentIdentitySnapshot?> identityProvider,
        ITerminalRegistryService? terminalRegistry = null)
    {
        _authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _terminalRegistry = terminalRegistry;
        _handlers = handlers.ToDictionary(handler => handler.CommandName, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> RegisteredCommands => _handlers.Keys.ToArray();

    public async Task<SalrynHostCommandResult<TResponse>> DispatchAsync<TPayload, TResponse>(
        SalrynHostCommandEnvelope<TPayload> envelope,
        CancellationToken cancellationToken = default)
    {
        SalrynHostCommandEnvelope<object?> normalized = new(
            envelope.CommandName,
            envelope.RequestId,
            envelope.StoreId,
            envelope.HostId,
            envelope.TerminalId,
            envelope.UserId,
            envelope.SessionId,
            envelope.TimestampUtc,
            envelope.SourceMachineRole,
            envelope.InstallationRole,
            envelope.Payload);

        if (!_handlers.TryGetValue(normalized.CommandName, out IHostCommandHandler? handler))
        {
            return SalrynHostCommandResult<TResponse>.Failure(
                normalized,
                Error.NotFound(
                    "HostCommand.UnknownCommand",
                    "Host command is not registered."));
        }

        Result validation = SalrynHostCommandEnvelopeValidator.Validate(
            normalized,
            payloadRequired: handler.RequiresPayload);

        if (validation.IsFailure)
        {
            return SalrynHostCommandResult<TResponse>.Failure(normalized, validation.ErrorDetail);
        }

        Result authority = _authorityGuard.RequireAuthorities(handler.AuthorityRequirements);

        if (authority.IsFailure)
        {
            return SalrynHostCommandResult<TResponse>.Failure(normalized, authority.ErrorDetail);
        }

        if (_terminalRegistry is not null)
        {
            Result terminalAccepted = await _terminalRegistry
                .IsTerminalAcceptedForHostCommandsAsync(normalized.TerminalId, cancellationToken)
                .ConfigureAwait(false);

            if (terminalAccepted.IsFailure)
            {
                return SalrynHostCommandResult<TResponse>.Failure(normalized, terminalAccepted.ErrorDetail);
            }
        }

        try
        {
            SalrynHostCommandResult<object?> handlerResult = await handler
                .HandleAsync(normalized, cancellationToken)
                .ConfigureAwait(false);

            if (!handlerResult.Succeeded)
            {
                return new SalrynHostCommandResult<TResponse>(
                    Succeeded: false,
                    Response: default,
                    ErrorCode: handlerResult.ErrorCode,
                    ErrorMessage: handlerResult.ErrorMessage,
                    RequestId: handlerResult.RequestId,
                    Trace: handlerResult.Trace);
            }

            if (handlerResult.Response is TResponse typedResponse)
            {
                return new SalrynHostCommandResult<TResponse>(
                    Succeeded: true,
                    Response: typedResponse,
                    ErrorCode: null,
                    ErrorMessage: null,
                    RequestId: handlerResult.RequestId,
                    Trace: handlerResult.Trace);
            }

            return SalrynHostCommandResult<TResponse>.Failure(
                normalized,
                Error.Failure(
                    "HostCommand.ResponseTypeMismatch",
                    "Host command handler returned an unexpected response type."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return SalrynHostCommandResult<TResponse>.Failure(
                normalized,
                Error.Failure(
                    "HostCommand.HandlerFailure",
                    $"Host command handler failed: {exception.Message}"));
        }
    }
}

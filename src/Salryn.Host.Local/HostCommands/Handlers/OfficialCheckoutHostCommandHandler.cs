using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class OfficialCheckoutHostCommandHandler : IHostCommandHandler
{
    private readonly IOfficialCheckoutHostCommandExecutor _executor;

    public OfficialCheckoutHostCommandHandler(IOfficialCheckoutHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.OfficialCheckoutComplete;

    public HostCommandAuthorityRequirements AuthorityRequirements =>
        HostCommandAuthorityRequirements.Database
        | HostCommandAuthorityRequirements.OfficialSequence
        | HostCommandAuthorityRequirements.Journal;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not OfficialCheckoutHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.OfficialCheckoutPayloadInvalid",
                    "Official checkout payload is invalid."));
        }

        Result result = await _executor.ExecuteAsync(payload, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(new { Completed = true }, envelope);
    }
}

using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class OfficialLifecycleHostCommandHandler : IHostCommandHandler
{
    private readonly IOfficialLifecycleHostCommandExecutor _executor;

    public OfficialLifecycleHostCommandHandler(IOfficialLifecycleHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.OfficialLifecycleAppend;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Journal;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not OfficialLifecycleHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.OfficialLifecyclePayloadInvalid",
                    "Official lifecycle payload is invalid."));
        }

        Result<OfficialDocumentAuditEventResult> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

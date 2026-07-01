using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class OfficialReadingPreviewXHostCommandHandler : IHostCommandHandler
{
    private readonly IOfficialReadingPreviewXHostCommandExecutor _executor;

    public OfficialReadingPreviewXHostCommandHandler(IOfficialReadingPreviewXHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.OfficialReadingPreviewX;

    public HostCommandAuthorityRequirements AuthorityRequirements =>
        HostCommandAuthorityRequirements.Database | HostCommandAuthorityRequirements.Journal;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not OfficialReadingPreviewXHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.OfficialReadingPreviewXPayloadInvalid",
                    "Official X-reading preview payload is invalid."));
        }

        Result<OfficialXReadingResult> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class OfficialReadingFinalizeZHostCommandHandler : IHostCommandHandler
{
    private readonly IOfficialReadingFinalizeZHostCommandExecutor _executor;

    public OfficialReadingFinalizeZHostCommandHandler(IOfficialReadingFinalizeZHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.OfficialReadingFinalizeZ;

    public HostCommandAuthorityRequirements AuthorityRequirements =>
        HostCommandAuthorityRequirements.Database | HostCommandAuthorityRequirements.Journal;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not OfficialReadingFinalizeZHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.OfficialReadingFinalizeZPayloadInvalid",
                    "Official Z-reading finalize payload is invalid."));
        }

        Result<OfficialZReadingResult> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

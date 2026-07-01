using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class ProductLookupHostCommandHandler : IHostCommandHandler
{
    private readonly IProductLookupHostCommandExecutor _executor;

    public ProductLookupHostCommandHandler(IProductLookupHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.ProductLookup;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProductLookupHostCommandRequest request)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.ProductLookupPayloadInvalid",
                    "Product lookup payload is invalid."));
        }

        Result<ProductLookupHostCommandResponse> result = await _executor
            .ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Host.Local.Dispatch;

// Kirch: Defines Local Host dispatch error codes before real handlers or transport exist.
public static class LocalHostDispatchErrorCodes
{
    public const string BoundaryValidationFailed = "LocalHost.Dispatch.BoundaryValidationFailed";
    public const string HandlerNotRegistered = "LocalHost.Dispatch.HandlerNotRegistered";
    public const string RequestEnvelopeMissing = "LocalHost.Dispatch.RequestEnvelopeMissing";
}

// Kirch: Stores in-process Local Host handlers without creating HTTP, sockets, or database access.
public sealed class LocalHostHandlerRegistry
{
    private readonly Dictionary<string, object> _handlers = new(StringComparer.Ordinal);

    public int RegisteredHandlerCount => _handlers.Count;

    // Kirch: Registers one typed Host operation handler for in-process dispatch only.
    public void Register<TPayload, TResponse>(
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new InvalidOperationException("Local Host handler operation name is required.");
        }

        ArgumentNullException.ThrowIfNull(handler);

        HostOperationHandlerRegistration registration = new(
            OperationName: operationName,
            HandlerType: handler.GetType(),
            PayloadType: typeof(TPayload),
            ResponseType: typeof(TResponse));

        if (!HostOperationRegistrationRules.IsStrictRegistrationValid(registration))
        {
            throw new InvalidOperationException("Local Host handler registration does not match its payload and response types.");
        }

        string key = CreateKey<TPayload, TResponse>(operationName);

        if (!_handlers.TryAdd(key, handler))
        {
            throw new InvalidOperationException($"Local Host handler already registered: {operationName}");
        }
    }

    // Kirch: Finds a typed handler without throwing so dispatch can return a clean Host response.
    public IHostOperationHandler<TPayload, TResponse>? Find<TPayload, TResponse>(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return null;
        }

        string key = CreateKey<TPayload, TResponse>(operationName);

        return _handlers.TryGetValue(key, out object? handler)
            ? handler as IHostOperationHandler<TPayload, TResponse>
            : null;
    }

    // Kirch: Keeps handler lookup scoped by operation name, payload type, and response type.
    private static string CreateKey<TPayload, TResponse>(string operationName)
    {
        return string.Join("|", operationName, typeof(TPayload).FullName, typeof(TResponse).FullName);
    }
}

// Kirch: Dispatches Local Host requests in-process after Core boundary validation succeeds.
public sealed class LocalHostOperationDispatcher : IHostOperationDispatcher
{
    private readonly IHostRequestBoundaryValidator _boundaryValidator;
    private readonly LocalHostHandlerRegistry _handlerRegistry;

    // Kirch: Creates a local dispatcher from the Core boundary validator and in-process handler registry.
    public LocalHostOperationDispatcher(
        IHostRequestBoundaryValidator boundaryValidator,
        LocalHostHandlerRegistry handlerRegistry)
    {
        _boundaryValidator = boundaryValidator ?? throw new ArgumentNullException(nameof(boundaryValidator));
        _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
    }

    // Kirch: Validates, locates a typed handler, and returns a contract response without network transport.
    public async Task<HostResponseEnvelope<TResponse>> DispatchAsync<TPayload, TResponse>(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return CreateFailure<TResponse>(
                requestId: Guid.Empty,
                code: LocalHostDispatchErrorCodes.RequestEnvelopeMissing,
                message: "Local Host request envelope must be provided.",
                type: ErrorType.Validation);
        }

        HostBoundaryValidationResult boundaryResult = _boundaryValidator.Validate(request);

        if (!boundaryResult.IsValid)
        {
            HostBoundaryValidationIssue firstIssue = boundaryResult.Issues.First();

            return CreateFailure<TResponse>(
                requestId: request.RequestId,
                code: firstIssue.Code,
                message: firstIssue.Message,
                type: firstIssue.Type);
        }

        IHostOperationHandler<TPayload, TResponse>? handler = _handlerRegistry.Find<TPayload, TResponse>(request.OperationName);

        if (handler is null)
        {
            return CreateFailure<TResponse>(
                requestId: request.RequestId,
                code: LocalHostDispatchErrorCodes.HandlerNotRegistered,
                message: "Local Host operation handler is not registered.",
                type: ErrorType.NotFound);
        }

        return await handler.HandleAsync(request, cancellationToken);
    }

    // Kirch: Creates a failed Host response envelope without leaking runtime exceptions.
    private static HostResponseEnvelope<TResponse> CreateFailure<TResponse>(
        Guid requestId,
        string code,
        string message,
        ErrorType type)
    {
        return new HostResponseEnvelope<TResponse>(
            RequestId: requestId,
            IsSuccess: false,
            Payload: default,
            Error: new HostContractError(
                Code: code,
                Message: message,
                Type: type));
    }
}

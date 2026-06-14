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
    public const string RuntimeNotRunning = "LocalHost.Runtime.NotRunning";
    public const string RuntimeAlreadyFaulted = "LocalHost.Runtime.AlreadyFaulted";
    public const string HandlerFailure = "LocalHost.Dispatch.HandlerFailure";
}

// Kirch: Creates consistent Local Host failure responses without leaking runtime exceptions.
public static class LocalHostDispatchResponses
{
    public static HostResponseEnvelope<TResponse> Failure<TResponse>(
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

// Kirch: Stores in-process Local Host handlers without creating HTTP, sockets, or database access.
public sealed class LocalHostHandlerRegistry
{
    private readonly Dictionary<string, object> _handlers = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    public int RegisteredHandlerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _handlers.Count;
            }
        }
    }

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

        lock (_syncRoot)
        {
            if (!_handlers.TryAdd(key, handler))
            {
                throw new InvalidOperationException($"Local Host handler already registered: {operationName}");
            }
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

        lock (_syncRoot)
        {
            return _handlers.TryGetValue(key, out object? handler)
                ? handler as IHostOperationHandler<TPayload, TResponse>
                : null;
        }
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
            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: Guid.Empty,
                code: LocalHostDispatchErrorCodes.RequestEnvelopeMissing,
                message: "Local Host request envelope must be provided.",
                type: ErrorType.Validation);
        }

        HostBoundaryValidationResult boundaryResult = _boundaryValidator.Validate(request);

        if (!boundaryResult.IsValid)
        {
            HostBoundaryValidationIssue? firstIssue = boundaryResult.Issues.Count > 0
                ? boundaryResult.Issues[0]
                : null;

            if (firstIssue is null)
            {
                return LocalHostDispatchResponses.Failure<TResponse>(
                    requestId: request.RequestId,
                    code: LocalHostDispatchErrorCodes.BoundaryValidationFailed,
                    message: "Local Host boundary validation failed without a specific issue.",
                    type: ErrorType.Validation);
            }

            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: request.RequestId,
                code: firstIssue.Code,
                message: firstIssue.Message,
                type: firstIssue.Type);
        }

        IHostOperationHandler<TPayload, TResponse>? handler = _handlerRegistry.Find<TPayload, TResponse>(request.OperationName);

        if (handler is null)
        {
            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: request.RequestId,
                code: LocalHostDispatchErrorCodes.HandlerNotRegistered,
                message: "Local Host operation handler is not registered.",
                type: ErrorType.NotFound);
        }

        try
        {
            return await handler.HandleAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: request.RequestId,
                code: LocalHostDispatchErrorCodes.HandlerFailure,
                message: $"Local Host operation handler failed: {exception.Message}",
                type: ErrorType.Failure);
        }
    }
}

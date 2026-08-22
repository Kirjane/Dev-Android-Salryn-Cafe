using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Dispatch;

namespace Salryn.Host.Local.Runtime;

// Kirch: Defines the lifecycle state of the local in-process Store Host runtime.
public enum LocalHostRuntimeState
{
    Stopped = 1,
    Running = 2,
    Faulted = 3
}

// Kirch: Captures the current local Host runtime state without exposing database or transport details.
public sealed record LocalHostRuntimeSnapshot(
    Guid HostId,
    string MachineName,
    SalrynMachineRole Role,
    SalrynDeploymentMode Mode,
    LocalHostRuntimeState State,
    DateTimeOffset? StartedAt,
    int RegisteredOperationCount,
    int RegisteredHandlerCount,
    int RegisteredContractCount);

// Kirch: Defines the local Store Host runtime contract before HTTP, sockets, or LAN discovery exist.
public interface ILocalHostRuntime
{
    LocalHostRuntimeSnapshot GetSnapshot();

    void Start(DateTimeOffset startedAt);

    void Stop();

    HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload>? request);

    void RegisterHandler<TPayload, TResponse>(
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler);

    Task<HostResponseEnvelope<TResponse>> DispatchAsync<TPayload, TResponse>(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default);
}

// Kirch: Centralizes Local Host runtime rules before real Host process and transport implementation.
public static class LocalHostRuntimeRules
{
    // Kirch: A Local Host profile is valid only when a host-capable role owns the store identity.
    public static bool IsHostProfileValid(StoreDeploymentProfile? profile)
    {
        return profile is not null
            && profile.Store is not null
            && profile.Host is not null
            && profile.Store.TenantId != Guid.Empty
            && profile.Store.StoreId != Guid.Empty
            && profile.Host.HostId != Guid.Empty
            && !string.IsNullOrWhiteSpace(profile.Host.MachineName)
            && StoreDeploymentRules.CanOwnDatabase(profile.Host.Role);
    }
}

// Kirch: Runs the local in-process Host boundary without creating network transport or direct UI coupling.
public sealed class LocalHostRuntime : ILocalHostRuntime
{
    private readonly StoreDeploymentProfile _profile;
    private readonly HostOperationCatalog _operationCatalog;
    private readonly LocalHostOperationContractMap _contractMap;
    private readonly HostRequestBoundaryValidator _boundaryValidator;
    private readonly LocalHostHandlerRegistry _handlerRegistry;
    private readonly LocalHostOperationDispatcher _dispatcher;
    private readonly object _stateLock = new();
    private LocalHostRuntimeState _state;
    private DateTimeOffset? _startedAt;

    // Kirch: Creates a Local Host runtime from a deployment profile, operation catalog, and contract map.
    public LocalHostRuntime(
        StoreDeploymentProfile profile,
        HostOperationCatalog? operationCatalog = null,
        LocalHostOperationContractMap? contractMap = null)
    {
        if (!LocalHostRuntimeRules.IsHostProfileValid(profile))
        {
            throw new InvalidOperationException("Invalid local Host deployment profile.");
        }

        _profile = profile;
        _operationCatalog = operationCatalog ?? HostOperationCatalog.CreateDefault();
        _contractMap = contractMap ?? LocalHostOperationContractMap.CreateDefault();

        if (!LocalHostOperationContractRules.CoversCatalog(_contractMap, _operationCatalog))
        {
            throw new InvalidOperationException("Local Host operation contract map does not cover the Host operation catalog.");
        }

        _boundaryValidator = new HostRequestBoundaryValidator(_operationCatalog);
        _handlerRegistry = new LocalHostHandlerRegistry();
        _dispatcher = new LocalHostOperationDispatcher(_boundaryValidator, _handlerRegistry);
        _state = LocalHostRuntimeState.Stopped;
    }

    // Kirch: Returns the current Local Host runtime state for future admin/status UI surfaces.
    public LocalHostRuntimeSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return new LocalHostRuntimeSnapshot(
                HostId: _profile.Host.HostId,
                MachineName: _profile.Host.MachineName,
                Role: _profile.Host.Role,
                Mode: _profile.Mode,
                State: _state,
                StartedAt: _startedAt,
                RegisteredOperationCount: _operationCatalog.Descriptors.Count,
                RegisteredHandlerCount: _handlerRegistry.RegisteredHandlerCount,
                RegisteredContractCount: _contractMap.Contracts.Count);
        }
    }

    // Kirch: Starts the in-process Local Host runtime without opening sockets or touching LAN state.
    public void Start(DateTimeOffset startedAt)
    {
        lock (_stateLock)
        {
            if (_state == LocalHostRuntimeState.Running)
            {
                return;
            }

            if (_state == LocalHostRuntimeState.Faulted)
            {
                throw new InvalidOperationException("Faulted Local Host runtime must be stopped before restart.");
            }

            _state = LocalHostRuntimeState.Running;
            _startedAt = startedAt;
        }
    }

    // Kirch: Stops the in-process Local Host runtime and clears transient runtime state.
    public void Stop()
    {
        lock (_stateLock)
        {
            _state = LocalHostRuntimeState.Stopped;
            _startedAt = null;
        }
    }

    // Kirch: Validates a future Host request through the Core boundary before any real dispatch exists.
    public HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload>? request)
    {
        return _boundaryValidator.Validate(request);
    }

    // Kirch: Registers a typed in-process handler only when its payload and response match the operation contract.
    public void RegisterHandler<TPayload, TResponse>(
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler)
    {
        lock (_stateLock)
        {
            if (_state != LocalHostRuntimeState.Stopped)
            {
                throw new InvalidOperationException("Local Host handlers can only be registered while runtime is stopped.");
            }

            LocalHostOperationContractDescriptor? contract = _contractMap.Find(operationName);

            if (contract is null)
            {
                throw new InvalidOperationException($"Local Host operation contract is not registered: {operationName}");
            }

            if (!LocalHostOperationContractRules.HandlerMatchesContract<TPayload, TResponse>(contract))
            {
                throw new InvalidOperationException($"Local Host handler contract mismatch: {operationName}");
            }

            _handlerRegistry.Register(operationName, handler);
        }
    }

    // Kirch: Dispatches through the local in-process dispatcher skeleton only while runtime is running.
    public async Task<HostResponseEnvelope<TResponse>> DispatchAsync<TPayload, TResponse>(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        LocalHostRuntimeState currentState;

        lock (_stateLock)
        {
            currentState = _state;
        }

        if (currentState == LocalHostRuntimeState.Faulted)
        {
            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: request?.RequestId ?? Guid.Empty,
                code: LocalHostDispatchErrorCodes.RuntimeAlreadyFaulted,
                message: "Local Host runtime is faulted and must be stopped before reuse.",
                type: ErrorType.Failure);
        }

        if (currentState != LocalHostRuntimeState.Running)
        {
            return LocalHostDispatchResponses.Failure<TResponse>(
                requestId: request?.RequestId ?? Guid.Empty,
                code: LocalHostDispatchErrorCodes.RuntimeNotRunning,
                message: "Local Host runtime must be running before dispatch.",
                type: ErrorType.Validation);
        }

        HostResponseEnvelope<TResponse> response = await _dispatcher.DispatchAsync<TPayload, TResponse>(
            request,
            cancellationToken);

        if (!response.IsSuccess && response.Error?.Code == LocalHostDispatchErrorCodes.HandlerFailure)
        {
            lock (_stateLock)
            {
                _state = LocalHostRuntimeState.Faulted;
            }
        }

        return response;
    }
}

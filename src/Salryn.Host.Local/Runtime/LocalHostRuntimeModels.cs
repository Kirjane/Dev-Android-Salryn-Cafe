using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
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
    int RegisteredOperationCount);

// Kirch: Defines the local Store Host runtime contract before HTTP, sockets, or LAN discovery exist.
public interface ILocalHostRuntime
{
    LocalHostRuntimeSnapshot GetSnapshot();

    void Start(DateTimeOffset startedAt);

    void Stop();

    HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload>? request);

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
    private readonly HostRequestBoundaryValidator _boundaryValidator;
    private readonly LocalHostHandlerRegistry _handlerRegistry;
    private readonly LocalHostOperationDispatcher _dispatcher;
    private LocalHostRuntimeState _state;
    private DateTimeOffset? _startedAt;

    // Kirch: Creates a Local Host runtime from a deployment profile and operation catalog.
    public LocalHostRuntime(
        StoreDeploymentProfile profile,
        HostOperationCatalog? operationCatalog = null)
    {
        if (!LocalHostRuntimeRules.IsHostProfileValid(profile))
        {
            throw new InvalidOperationException("Invalid local Host deployment profile.");
        }

        _profile = profile;
        _operationCatalog = operationCatalog ?? HostOperationCatalog.CreateDefault();
        _boundaryValidator = new HostRequestBoundaryValidator(_operationCatalog);
        _handlerRegistry = new LocalHostHandlerRegistry();
        _dispatcher = new LocalHostOperationDispatcher(_boundaryValidator, _handlerRegistry);
        _state = LocalHostRuntimeState.Stopped;
    }

    // Kirch: Returns the current Local Host runtime state for future admin/status UI surfaces.
    public LocalHostRuntimeSnapshot GetSnapshot()
    {
        return new LocalHostRuntimeSnapshot(
            HostId: _profile.Host.HostId,
            MachineName: _profile.Host.MachineName,
            Role: _profile.Host.Role,
            Mode: _profile.Mode,
            State: _state,
            StartedAt: _startedAt,
            RegisteredOperationCount: _operationCatalog.Descriptors.Count);
    }

    // Kirch: Starts the in-process Local Host runtime without opening sockets or touching LAN state.
    public void Start(DateTimeOffset startedAt)
    {
        if (_state == LocalHostRuntimeState.Running)
        {
            return;
        }

        _state = LocalHostRuntimeState.Running;
        _startedAt = startedAt;
    }

    // Kirch: Stops the in-process Local Host runtime and clears transient runtime state.
    public void Stop()
    {
        _state = LocalHostRuntimeState.Stopped;
        _startedAt = null;
    }

    // Kirch: Validates a future Host request through the Core boundary before any real dispatch exists.
    public HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload>? request)
    {
        return _boundaryValidator.Validate(request);
    }

    // Kirch: Registers a typed in-process handler without adding real POS handlers or network transport.
    public void RegisterHandler<TPayload, TResponse>(
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler)
    {
        _handlerRegistry.Register(operationName, handler);
    }

    // Kirch: Dispatches through the local in-process dispatcher skeleton after boundary validation.
    public Task<HostResponseEnvelope<TResponse>> DispatchAsync<TPayload, TResponse>(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        return _dispatcher.DispatchAsync<TPayload, TResponse>(request, cancellationToken);
    }
}

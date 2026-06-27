using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Handlers;
using Salryn.Host.Local.Handlers.Products;
using Salryn.Host.Local.Runtime;

namespace Salryn.Host.Local.Bootstrap;

// Kirch: Defines Local Host bootstrap placeholder error codes before real POS handlers exist.
public static class LocalHostHandlerBootstrapErrorCodes
{
    public const string HandlerNotImplemented = "LocalHost.Bootstrap.HandlerNotImplemented";
}

// Kirch: Reports which handlers were registered during Local Host bootstrap.
public sealed record LocalHostHandlerBootstrapResult(
    int RegisteredHandlerCount,
    IReadOnlyCollection<string> RegisteredOperationNames);

// Kirch: Registers contract-safe Local Host handlers while preserving the default placeholder path.
public sealed class LocalHostHandlerBootstrapper
{
    // Kirch: Registers one placeholder handler for every default Local Host operation contract.
    public LocalHostHandlerBootstrapResult RegisterDefaultHandlers(ILocalHostRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        List<string> registeredOperations = new();

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.Login,
            new LocalHostBootstrapNotImplementedHandler<LocalHostLoginRequest, LocalHostLoginResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductSearch,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductSearchRequest, LocalHostProductSearchResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductLookup,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>());

        RegisterRemainingPlaceholderHandlers(
            runtime,
            registeredOperations,
            officialOutputTextFileWorkflow: null);

        return CreateResult(registeredOperations);
    }

    // Kirch: Registers ProductLookup as the first real handler while keeping the rest as placeholders.
    public LocalHostHandlerBootstrapResult RegisterDefaultHandlersWithProductLookup(
        ILocalHostRuntime runtime,
        IProductReadRepository products)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(products);

        List<string> registeredOperations = new();

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.Login,
            new LocalHostBootstrapNotImplementedHandler<LocalHostLoginRequest, LocalHostLoginResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductSearch,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductSearchRequest, LocalHostProductSearchResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductLookup,
            new LocalHostProductLookupHandler(products));

        RegisterRemainingPlaceholderHandlers(
            runtime,
            registeredOperations,
            officialOutputTextFileWorkflow: null);

        return CreateResult(registeredOperations);
    }

    public LocalHostHandlerBootstrapResult RegisterDefaultHandlersWithOfficialOutputTextFileWorkflow(
        ILocalHostRuntime runtime,
        IOfficialOutputTextFileWorkflowExecutionPort officialOutputTextFileWorkflow)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(officialOutputTextFileWorkflow);

        List<string> registeredOperations = new();

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.Login,
            new LocalHostBootstrapNotImplementedHandler<LocalHostLoginRequest, LocalHostLoginResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductSearch,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductSearchRequest, LocalHostProductSearchResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductLookup,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>());

        RegisterRemainingPlaceholderHandlers(
            runtime,
            registeredOperations,
            officialOutputTextFileWorkflow);

        return CreateResult(registeredOperations);
    }

    public LocalHostHandlerBootstrapResult RegisterDefaultHandlersWithProductLookupAndOfficialOutputTextFileWorkflow(
        ILocalHostRuntime runtime,
        IProductReadRepository products,
        IOfficialOutputTextFileWorkflowExecutionPort officialOutputTextFileWorkflow)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(officialOutputTextFileWorkflow);

        List<string> registeredOperations = new();

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.Login,
            new LocalHostBootstrapNotImplementedHandler<LocalHostLoginRequest, LocalHostLoginResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductSearch,
            new LocalHostBootstrapNotImplementedHandler<LocalHostProductSearchRequest, LocalHostProductSearchResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.ProductLookup,
            new LocalHostProductLookupHandler(products));

        RegisterRemainingPlaceholderHandlers(
            runtime,
            registeredOperations,
            officialOutputTextFileWorkflow);

        return CreateResult(registeredOperations);
    }

    // Kirch: Registers the operation placeholders that are not part of the Phase 64B ProductLookup slice.
    private static void RegisterRemainingPlaceholderHandlers(
        ILocalHostRuntime runtime,
        List<string> registeredOperations,
        IOfficialOutputTextFileWorkflowExecutionPort? officialOutputTextFileWorkflow)
    {
        Register(
            runtime,
            registeredOperations,
            HostOperationNames.CompleteSale,
            new LocalHostBootstrapNotImplementedHandler<LocalHostCompleteSaleRequest, LocalHostCompleteSaleResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.OpenCashierSession,
            new LocalHostBootstrapNotImplementedHandler<LocalHostOpenCashierSessionRequest, LocalHostOpenCashierSessionResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.CloseCashierSession,
            new LocalHostBootstrapNotImplementedHandler<LocalHostCloseCashierSessionRequest, LocalHostCloseCashierSessionResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.DailySalesSummary,
            new LocalHostBootstrapNotImplementedHandler<LocalHostDailySalesSummaryRequest, LocalHostDailySalesSummaryResponse>());

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.BackupHealth,
            new LocalHostBootstrapNotImplementedHandler<LocalHostBackupHealthRequest, LocalHostBackupHealthResponse>());

        if (officialOutputTextFileWorkflow is null)
        {
            Register(
                runtime,
                registeredOperations,
                HostOperationNames.OfficialOutputTextFileWorkflow,
                new LocalHostBootstrapNotImplementedHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>());

            return;
        }

        Register(
            runtime,
            registeredOperations,
            HostOperationNames.OfficialOutputTextFileWorkflow,
            new LocalHostOfficialOutputTextFileWorkflowHandler(officialOutputTextFileWorkflow));
    }

    // Kirch: Registers one typed handler through the Local Host runtime contract gate.
    private static void Register<TPayload, TResponse>(
        ILocalHostRuntime runtime,
        List<string> registeredOperations,
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler)
    {
        runtime.RegisterHandler(operationName, handler);
        registeredOperations.Add(operationName);
    }

    // Kirch: Creates a stable bootstrap result for readiness and smoke checks.
    private static LocalHostHandlerBootstrapResult CreateResult(List<string> registeredOperations)
    {
        return new LocalHostHandlerBootstrapResult(
            RegisteredHandlerCount: registeredOperations.Count,
            RegisteredOperationNames: registeredOperations.ToArray());
    }
}

// Kirch: Placeholder handler proves bootstrap wiring while refusing to perform real business operations.
public sealed class LocalHostBootstrapNotImplementedHandler<TPayload, TResponse> : IHostOperationHandler<TPayload, TResponse>
{
    public Task<HostResponseEnvelope<TResponse>> HandleAsync(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        HostResponseEnvelope<TResponse> response = new(
            RequestId: request.RequestId,
            IsSuccess: false,
            Payload: default,
            Error: new HostContractError(
                Code: LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented,
                Message: $"Local Host placeholder handler is not implemented: {request.OperationName}",
                Type: ErrorType.Failure));

        return Task.FromResult(response);
    }
}

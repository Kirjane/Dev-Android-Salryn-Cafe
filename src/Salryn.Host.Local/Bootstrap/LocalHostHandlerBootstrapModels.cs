using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Host.Local.Bootstrap;

// Kirch: Defines Local Host bootstrap placeholder error codes before real POS handlers exist.
public static class LocalHostHandlerBootstrapErrorCodes
{
    public const string HandlerNotImplemented = "LocalHost.Bootstrap.HandlerNotImplemented";
}

// Kirch: Reports which placeholder handlers were registered during Local Host bootstrap.
public sealed record LocalHostHandlerBootstrapResult(
    int RegisteredHandlerCount,
    IReadOnlyCollection<string> RegisteredOperationNames);

// Kirch: Registers contract-safe placeholder handlers without adding real POS business logic.
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

        return new LocalHostHandlerBootstrapResult(
            RegisteredHandlerCount: registeredOperations.Count,
            RegisteredOperationNames: registeredOperations.ToArray());
    }

    // Kirch: Registers one typed placeholder handler through the Local Host runtime contract gate.
    private static void Register<TPayload, TResponse>(
        ILocalHostRuntime runtime,
        List<string> registeredOperations,
        string operationName,
        IHostOperationHandler<TPayload, TResponse> handler)
    {
        runtime.RegisterHandler(operationName, handler);
        registeredOperations.Add(operationName);
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

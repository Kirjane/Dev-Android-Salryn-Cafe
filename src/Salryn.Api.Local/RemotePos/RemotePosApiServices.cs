using Salryn.Api.Local.Composition;
using Salryn.Composition.Local;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.RemotePos;

/// <summary>
/// LAN-M16 composition for the host-owned remote POS command runtime. It reuses the read-only
/// LAN trust machinery (terminal registry, handshake, transport config, deployment identity) via
/// <see cref="LanRemoteTransportApiServices"/>, but builds its OWN host-side write path from the
/// shared SQLite connection factory. All writes execute on the host process; the remote client
/// only sends intent. The read-only bridge is untouched.
/// </summary>
public sealed class RemotePosApiServices
{
    private RemotePosApiServices(
        RemotePosRuntimeGate runtimeGate,
        RemotePosOperatorSessionService sessionService,
        RemotePosTerminalTrustService terminalTrustService,
        RemotePosCheckoutService checkoutService,
        RemotePosPairingGateService pairingGate,
        LanRemoteTransportApiServices lanServices,
        RemotePosCapacityPolicy capacityPolicy)
    {
        RuntimeGate = runtimeGate;
        SessionService = sessionService;
        TerminalTrustService = terminalTrustService;
        CheckoutService = checkoutService;
        PairingGate = pairingGate;
        LanServices = lanServices;
        CapacityPolicy = capacityPolicy;
    }

    public RemotePosRuntimeGate RuntimeGate { get; }

    public RemotePosOperatorSessionService SessionService { get; }

    public RemotePosTerminalTrustService TerminalTrustService { get; }

    public RemotePosCheckoutService CheckoutService { get; }

    public RemotePosPairingGateService PairingGate { get; }

    public LanRemoteTransportApiServices LanServices { get; }

    public RemotePosCapacityPolicy CapacityPolicy { get; }

    public static RemotePosApiServices Create(
        SalrynLocalComposition composition,
        LanRemoteTransportApiServices lanServices,
        RemotePosRuntimeGate runtimeGate)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(lanServices);
        ArgumentNullException.ThrowIfNull(runtimeGate);

        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);

        CashierSessionService cashierSessionService = new(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        PosCheckoutService posCheckoutService = new(
            repositories.CreateSaleWriteRepository(),
            cashierSessionService);

        // Kirch: why - server-trusted re-pricing (the repository overload) means the remote client
        // can never dictate price; the host re-reads persisted product facts at submit.
        PosCheckoutApplicationService checkoutApplicationService = new(
            posCheckoutService,
            composition.ProductReadRepository);

        RemotePosCheckoutService checkoutService = new(
            composition.ProductReadRepository,
            checkoutApplicationService,
            cashierSessionService,
            composition.SaleReadRepository,
            repositories.CreateAuditWriteRepository(),
            new RemotePosReceiptNumberAllocator());

        RemotePosCapacityPolicy capacityPolicy = new();

        RemotePosOperatorSessionService sessionService = new(
            lanServices,
            runtimeGate,
            composition.Authentication,
            composition.UserReadRepository,
            capacityPolicy);

        RemotePosPairingGateService pairingGate = new(lanServices, capacityPolicy);
        RemotePosTerminalTrustService terminalTrustService = new(sessionService, pairingGate, lanServices);

        return new RemotePosApiServices(
            runtimeGate,
            sessionService,
            terminalTrustService,
            checkoutService,
            pairingGate,
            lanServices,
            capacityPolicy);
    }
}

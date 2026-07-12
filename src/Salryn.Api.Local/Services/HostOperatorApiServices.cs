using Salryn.Composition.Local;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.Services;

public sealed class HostOperatorApiServices
{
    private HostOperatorApiServices(
        ICashierSessionService cashierSessionService,
        HostOperatorShiftApiService shiftService,
        HostOperatorCheckoutApiService checkoutService)
    {
        CashierSessionService = cashierSessionService;
        ShiftService = shiftService;
        CheckoutService = checkoutService;
    }

    public ICashierSessionService CashierSessionService { get; }

    public HostOperatorShiftApiService ShiftService { get; }

    public HostOperatorCheckoutApiService CheckoutService { get; }

    public static HostOperatorApiServices Create(SalrynLocalComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);

        CashierSessionService cashierSessionService = new(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        PosCheckoutService posCheckoutService = new(
            repositories.CreateSaleWriteRepository(),
            cashierSessionService);

        PosCheckoutApplicationService checkoutApplicationService = new(
            posCheckoutService,
            composition.ProductReadRepository);

        HostOperatorShiftApiService shiftService = new(cashierSessionService);
        FileReceiptWriter fileReceiptWriter = new(composition.DbPaths.ReceiptPath);

        HostOperatorCheckoutApiService checkoutService = new(
            composition.ProductReadRepository,
            checkoutApplicationService,
            cashierSessionService,
            composition.SaleReadRepository,
            new HostOperatorReceiptNumberAllocator(),
            fileReceiptWriter);

        return new HostOperatorApiServices(cashierSessionService, shiftService, checkoutService);
    }
}

using Salryn.Composition.Local;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.Services;

public sealed class HostOperatorApiServices
{
    private HostOperatorApiServices(
        ICashierSessionService cashierSessionService,
        HostOperatorShiftApiService shiftService)
    {
        CashierSessionService = cashierSessionService;
        ShiftService = shiftService;
    }

    public ICashierSessionService CashierSessionService { get; }

    public HostOperatorShiftApiService ShiftService { get; }

    public static HostOperatorApiServices Create(SalrynLocalComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);

        CashierSessionService cashierSessionService = new(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        HostOperatorShiftApiService shiftService = new(cashierSessionService);

        return new HostOperatorApiServices(cashierSessionService, shiftService);
    }
}
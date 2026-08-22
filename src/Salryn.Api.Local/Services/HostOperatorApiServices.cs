using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.Services;

public sealed class HostOperatorApiServices
{
    private HostOperatorApiServices(
        ICashierSessionService cashierSessionService,
        HostOperatorShiftApiService shiftService,
        HostOperatorCheckoutApiService checkoutService,
        PosReceiptPrintService receiptPrintService)
    {
        CashierSessionService = cashierSessionService;
        ShiftService = shiftService;
        CheckoutService = checkoutService;
        ReceiptPrintService = receiptPrintService;
    }

    public ICashierSessionService CashierSessionService { get; }

    public HostOperatorShiftApiService ShiftService { get; }

    public HostOperatorCheckoutApiService CheckoutService { get; }

    public PosReceiptPrintService ReceiptPrintService { get; }

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
            composition.ProductReadRepository,
            repositories.CreateStoreSettingsReadRepository());

        HostOperatorShiftApiService shiftService = new(cashierSessionService);
        FileReceiptWriter fileReceiptWriter = new(composition.DbPaths.ReceiptPath);

        HostOperatorCheckoutApiService checkoutService = new(
            composition.ProductReadRepository,
            checkoutApplicationService,
            cashierSessionService,
            composition.SaleReadRepository,
            new HostOperatorReceiptNumberAllocator(),
            fileReceiptWriter,
            repositories.CreateAuditWriteRepository());

        PosReceiptPrintService receiptPrintService = new(
            composition.SaleReadRepository,
            () => CreateConfiguredReceiptPrinterAdapter(repositories));

        return new HostOperatorApiServices(
            cashierSessionService,
            shiftService,
            checkoutService,
            receiptPrintService);
    }

    private static IReceiptPrinterAdapter CreateConfiguredReceiptPrinterAdapter(SqliteRepositoryFactory repositories)
    {
        Result<StoreSettingsReadModel?> settingsResult = repositories
            .CreateStoreSettingsReadRepository()
            .GetCurrentAsync()
            .GetAwaiter()
            .GetResult();

        if (settingsResult.IsFailure)
        {
            return new DisabledReceiptPrinterAdapter(
                "Receipt printing is unavailable because store printer settings could not be read: " + settingsResult.ErrorDetail.Message,
                "ReceiptPrinterSettings.ReadFailed");
        }

        return ReceiptPrinterAdapterFactory.Create(settingsResult.Value);
    }
}

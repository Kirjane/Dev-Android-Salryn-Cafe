using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Host.Local.Contracts;

// Kirch: Describes the payload and response contract expected for one Local Host operation.
public sealed record LocalHostOperationContractDescriptor(
    string OperationName,
    HostOperationKind OperationKind,
    Type PayloadType,
    Type ResponseType);

// Kirch: Stores Local Host operation contract mappings without implementing real POS handlers.
public sealed class LocalHostOperationContractMap
{
    private readonly Dictionary<string, LocalHostOperationContractDescriptor> _contracts;

    // Kirch: Builds a strict operation contract map for local in-process handler registration.
    public LocalHostOperationContractMap(IEnumerable<LocalHostOperationContractDescriptor> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _contracts = new Dictionary<string, LocalHostOperationContractDescriptor>(StringComparer.Ordinal);

        foreach (LocalHostOperationContractDescriptor? contract in contracts)
        {
            if (!LocalHostOperationContractRules.IsContractValid(contract))
            {
                throw new InvalidOperationException("Invalid Local Host operation contract.");
            }

            if (!_contracts.TryAdd(contract.OperationName, contract))
            {
                throw new InvalidOperationException($"Duplicate Local Host operation contract: {contract.OperationName}");
            }
        }
    }

    public IReadOnlyCollection<LocalHostOperationContractDescriptor> Contracts => _contracts.Values.ToArray();

    // Kirch: Finds one operation contract without throwing so runtime registration can fail cleanly.
    public LocalHostOperationContractDescriptor? Find(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return null;
        }

        return _contracts.TryGetValue(operationName, out LocalHostOperationContractDescriptor? contract)
            ? contract
            : null;
    }

    // Kirch: Creates the first Local Host contract map before production handlers are implemented.
    public static LocalHostOperationContractMap CreateDefault()
    {
        return new LocalHostOperationContractMap(new[]
        {
            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.Login,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostLoginRequest),
                ResponseType: typeof(LocalHostLoginResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.ProductSearch,
                OperationKind: HostOperationKind.Query,
                PayloadType: typeof(LocalHostProductSearchRequest),
                ResponseType: typeof(LocalHostProductSearchResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.ProductLookup,
                OperationKind: HostOperationKind.Query,
                PayloadType: typeof(LocalHostProductLookupRequest),
                ResponseType: typeof(LocalHostProductLookupResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.CompleteSale,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostCompleteSaleRequest),
                ResponseType: typeof(LocalHostCompleteSaleResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.OpenCashierSession,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostOpenCashierSessionRequest),
                ResponseType: typeof(LocalHostOpenCashierSessionResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.CloseCashierSession,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostCloseCashierSessionRequest),
                ResponseType: typeof(LocalHostCloseCashierSessionResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.DailySalesSummary,
                OperationKind: HostOperationKind.Query,
                PayloadType: typeof(LocalHostDailySalesSummaryRequest),
                ResponseType: typeof(LocalHostDailySalesSummaryResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.BackupHealth,
                OperationKind: HostOperationKind.Query,
                PayloadType: typeof(LocalHostBackupHealthRequest),
                ResponseType: typeof(LocalHostBackupHealthResponse)),

            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.OfficialOutputTextFileWorkflow,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostOfficialOutputTextFileWorkflowRequest),
                ResponseType: typeof(LocalHostOfficialOutputTextFileWorkflowResponse))
        });
    }
}

// Kirch: Centralizes Local Host contract mapping rules before real operation handlers exist.
public static class LocalHostOperationContractRules
{
    // Kirch: A contract is valid only when it names an operation and uses concrete payload and response types.
    public static bool IsContractValid(LocalHostOperationContractDescriptor? contract)
    {
        return contract is not null
            && !string.IsNullOrWhiteSpace(contract.OperationName)
            && contract.PayloadType != typeof(void)
            && contract.ResponseType != typeof(void)
            && Enum.IsDefined(contract.OperationKind);
    }

    // Kirch: Ensures a Local Host operation contract matches the Core operation descriptor.
    public static bool MatchesDescriptor(
        LocalHostOperationContractDescriptor? contract,
        HostOperationDescriptor? descriptor)
    {
        if (contract is null || descriptor is null || !IsContractValid(contract))
        {
            return false;
        }

        return string.Equals(contract.OperationName, descriptor.OperationName, StringComparison.Ordinal)
            && contract.OperationKind == descriptor.OperationKind;
    }

    // Kirch: Ensures the contract map covers every operation known by the Core Host catalog.
    public static bool CoversCatalog(
        LocalHostOperationContractMap? contractMap,
        HostOperationCatalog? operationCatalog)
    {
        if (contractMap is null || operationCatalog is null)
        {
            return false;
        }

        foreach (HostOperationDescriptor descriptor in operationCatalog.Descriptors)
        {
            LocalHostOperationContractDescriptor? contract = contractMap.Find(descriptor.OperationName);

            if (!MatchesDescriptor(contract, descriptor))
            {
                return false;
            }
        }

        return true;
    }

    // Kirch: Ensures a typed handler registration matches the mapped payload and response contract.
    public static bool HandlerMatchesContract<TPayload, TResponse>(
        LocalHostOperationContractDescriptor? contract)
    {
        if (contract is null || !IsContractValid(contract))
        {
            return false;
        }

        return contract.PayloadType == typeof(TPayload)
            && contract.ResponseType == typeof(TResponse);
    }
}

// Kirch: Local Host login request contract placeholder before real authentication handler implementation.
public sealed record LocalHostLoginRequest(string Username, string DeviceKey);

// Kirch: Local Host login response contract placeholder before real authentication handler implementation.
public sealed record LocalHostLoginResponse(Guid UserId, string DisplayName);

// Kirch: Local Host product search request contract placeholder before real product handler implementation.
public sealed record LocalHostProductSearchRequest(string SearchText, int MaxResults);

// Kirch: Local Host product search response contract placeholder before real product handler implementation.
public sealed record LocalHostProductSearchResponse(int MatchCount);

// Kirch: Local Host product lookup request contract placeholder before real product handler implementation.
public sealed record LocalHostProductLookupRequest(string BarcodeOrSku);

// Kirch: Local Host product lookup response contract placeholder before real product handler implementation.
public sealed record LocalHostProductLookupResponse(
    Guid ProductId,
    string? Sku,
    string? Barcode,
    string DisplayName,
    string UnitName,
    long SellingPriceCents,
    long? CostPriceCents,
    long StockOnHandX1000,
    bool TrackStock,
    bool AllowDiscount,
    bool IsActive);

// Kirch: Local Host checkout request contract placeholder before real sale handler implementation.
public sealed record LocalHostCompleteSaleRequest(Guid DraftSaleId);

// Kirch: Local Host checkout response contract placeholder before real sale handler implementation.
public sealed record LocalHostCompleteSaleResponse(string ReceiptNumber);

// Kirch: Local Host cashier session open request contract placeholder before real cashier handler implementation.
public sealed record LocalHostOpenCashierSessionRequest(Guid CashierId);

// Kirch: Local Host cashier session open response contract placeholder before real cashier handler implementation.
public sealed record LocalHostOpenCashierSessionResponse(Guid CashierSessionId);

// Kirch: Local Host cashier session close request contract placeholder before real cashier handler implementation.
public sealed record LocalHostCloseCashierSessionRequest(Guid CashierSessionId);

// Kirch: Local Host cashier session close response contract placeholder before real cashier handler implementation.
public sealed record LocalHostCloseCashierSessionResponse(bool Closed);

// Kirch: Local Host daily sales summary request contract placeholder before real reporting handler implementation.
public sealed record LocalHostDailySalesSummaryRequest(DateOnly BusinessDate);

// Kirch: Local Host daily sales summary response contract placeholder before real reporting handler implementation.
public sealed record LocalHostDailySalesSummaryResponse(decimal GrossSales);

// Kirch: Local Host backup health request contract placeholder before real backup handler implementation.
public sealed record LocalHostBackupHealthRequest(Guid StoreId);

// Kirch: Local Host backup health response contract placeholder before real backup handler implementation.
public sealed record LocalHostBackupHealthResponse(bool IsHealthy);

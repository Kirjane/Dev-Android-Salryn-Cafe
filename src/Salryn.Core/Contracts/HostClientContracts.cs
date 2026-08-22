using Salryn.Core.Common;

namespace Salryn.Core.Contracts;

// Kirch: Identifies whether a Host request intends to change state or only read state.
public enum HostOperationKind
{
    Command = 1,
    Query = 2
}

// Kirch: Carries caller identity that every future Host command or query can validate consistently.
public sealed record HostRequestContext(
    Guid TenantId,
    Guid StoreId,
    Guid? UserId,
    Guid? RoleId,
    Guid? CashierSessionId,
    Guid? TerminalId,
    string? DeviceKey,
    DateTimeOffset RequestedAt);

// Kirch: Wraps any future Host command or query without tying Core to HTTP, sockets, or WinForms.
public sealed record HostRequestEnvelope<TPayload>(
    Guid RequestId,
    HostOperationKind OperationKind,
    string OperationName,
    HostRequestContext Context,
    TPayload Payload);

// Kirch: Represents a serializable Host-side error without exposing infrastructure exceptions to clients.
public sealed record HostContractError(
    string Code,
    string Message,
    ErrorType Type);

// Kirch: Wraps Host responses so future clients can handle success and failure through one contract shape.
public sealed record HostResponseEnvelope<TPayload>(
    Guid RequestId,
    bool IsSuccess,
    TPayload? Payload,
    HostContractError? Error);

// Kirch: Centralizes Host/client contract rules before networking or Host implementation exists.
public static class HostClientContractRules
{
    // Kirch: Commands change store state and must always carry authenticated user and terminal context.
    public static bool RequiresAuthenticatedUser(HostOperationKind operationKind)
    {
        return operationKind == HostOperationKind.Command;
    }

    // Kirch: Cashier session identity is required only for cashier-bound operations such as checkout.
    public static bool RequiresCashierSession(string operationName)
    {
        return string.Equals(operationName, HostOperationNames.CompleteSale, StringComparison.Ordinal)
            || string.Equals(operationName, HostOperationNames.CloseCashierSession, StringComparison.Ordinal);
    }

    // Kirch: Device identity is required once a request comes from a registered terminal or mobile client.
    public static bool HasTerminalIdentity(HostRequestContext context)
    {
        return context.TerminalId.HasValue && !string.IsNullOrWhiteSpace(context.DeviceKey);
    }

    // Kirch: Store identity is mandatory because future multi-client support cannot rely on one implicit store.
    public static bool HasStoreIdentity(HostRequestContext context)
    {
        return context.TenantId != Guid.Empty && context.StoreId != Guid.Empty;
    }

    // Kirch: Authenticated commands must carry user, role, and terminal identity before Host execution.
    public static bool HasAuthenticatedCommandIdentity(HostRequestContext context)
    {
        return context.UserId.HasValue
            && context.RoleId.HasValue
            && HasTerminalIdentity(context);
    }
}

// Kirch: Names the first Host operations without implementing transport, routing, or handlers yet.
public static class HostOperationNames
{
    public const string Login = "auth.login";
    public const string ProductSearch = "products.search";
    public const string ProductLookup = "products.lookup";
    public const string CompleteSale = "checkout.completeSale";
    public const string OpenCashierSession = "cashierSession.open";
    public const string CloseCashierSession = "cashierSession.close";
    public const string DailySalesSummary = "reports.dailySalesSummary";
    public const string BackupHealth = "backup.health";
    public const string OfficialOutputTextFileWorkflow = "officialOutput.textFileWorkflow";
}

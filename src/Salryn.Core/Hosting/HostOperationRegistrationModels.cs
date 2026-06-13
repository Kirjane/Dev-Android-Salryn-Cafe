using Salryn.Core.Contracts;

namespace Salryn.Core.Hosting;

// Kirch: Describes one Host operation before routing, transport, or handler implementation exists.
public sealed record HostOperationDescriptor(
    string OperationName,
    HostOperationKind OperationKind,
    bool RequiresStoreIdentity,
    bool RequiresTerminalIdentity,
    bool RequiresAuthenticatedUser,
    bool RequiresCashierSession,
    bool AllowsReadOnlyMobileSnapshotAccess);

// Kirch: Describes the handler type planned for one Host operation without instantiating or dispatching it yet.
public sealed record HostOperationHandlerRegistration(
    string OperationName,
    Type HandlerType,
    Type PayloadType,
    Type ResponseType);

// Kirch: Defines the future handler shape without creating the Host runtime or transport layer.
public interface IHostOperationHandler<TPayload, TResponse>
{
    Task<HostResponseEnvelope<TResponse>> HandleAsync(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default);
}

// Kirch: Read-only catalog for operation descriptors used by future Host routing and validation.
public sealed class HostOperationCatalog
{
    private readonly Dictionary<string, HostOperationDescriptor> _descriptors;

    // Kirch: Builds a catalog and rejects missing, invalid, or duplicate operation descriptors early.
    public HostOperationCatalog(IEnumerable<HostOperationDescriptor> descriptors)
    {
        // Kirch: Rejects a missing descriptor collection before catalog composition proceeds.
        ArgumentNullException.ThrowIfNull(descriptors);

        _descriptors = new Dictionary<string, HostOperationDescriptor>(StringComparer.Ordinal);

        foreach (HostOperationDescriptor? descriptor in descriptors)
        {
            if (!HostOperationRegistrationRules.IsDescriptorValid(descriptor))
            {
                throw new InvalidOperationException("Invalid Host operation descriptor.");
            }

            if (!_descriptors.TryAdd(descriptor.OperationName, descriptor))
            {
                throw new InvalidOperationException($"Duplicate Host operation descriptor: {descriptor.OperationName}");
            }
        }
    }

    public IReadOnlyCollection<HostOperationDescriptor> Descriptors => _descriptors.Values.ToArray();

    // Kirch: Finds a descriptor without throwing so future Host routing can return clean contract errors.
    public HostOperationDescriptor? Find(string operationName)
    {
        // Kirch: Missing or blank operation names are treated as unknown instead of throwing.
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return null;
        }

        return _descriptors.TryGetValue(operationName, out HostOperationDescriptor? descriptor)
            ? descriptor
            : null;
    }

    // Kirch: Checks whether an operation is known to the Host boundary catalog.
    public bool Contains(string operationName)
    {
        // Kirch: Missing or blank operation names are never considered registered.
        return !string.IsNullOrWhiteSpace(operationName)
            && _descriptors.ContainsKey(operationName);
    }

    // Kirch: Defines the first planned Host operations without implementing handlers or routing.
    public static HostOperationCatalog CreateDefault()
    {
        return new HostOperationCatalog(new[]
        {
            new HostOperationDescriptor(
                OperationName: HostOperationNames.Login,
                OperationKind: HostOperationKind.Command,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: false,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.ProductSearch,
                OperationKind: HostOperationKind.Query,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.ProductLookup,
                OperationKind: HostOperationKind.Query,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.CompleteSale,
                OperationKind: HostOperationKind.Command,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: true,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.OpenCashierSession,
                OperationKind: HostOperationKind.Command,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.CloseCashierSession,
                OperationKind: HostOperationKind.Command,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: true,
                AllowsReadOnlyMobileSnapshotAccess: false),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.DailySalesSummary,
                OperationKind: HostOperationKind.Query,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: true),

            new HostOperationDescriptor(
                OperationName: HostOperationNames.BackupHealth,
                OperationKind: HostOperationKind.Query,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: true)
        });
    }
}

// Kirch: Centralizes operation descriptor and handler registration rules before runtime dispatch exists.
public static class HostOperationRegistrationRules
{
    // Kirch: A descriptor is valid only when it names an operation and keeps store identity mandatory.
    public static bool IsDescriptorValid(HostOperationDescriptor? descriptor)
    {
        return descriptor is not null
            && !string.IsNullOrWhiteSpace(descriptor.OperationName)
            && descriptor.RequiresStoreIdentity;
    }

    // Kirch: Handler registrations must name the operation and provide concrete handler/payload/response types.
    public static bool IsRegistrationValid(HostOperationHandlerRegistration? registration)
    {
        return registration is not null
            && !string.IsNullOrWhiteSpace(registration.OperationName)
            && registration.HandlerType != typeof(void)
            && registration.PayloadType != typeof(void)
            && registration.ResponseType != typeof(void);
    }

    // Kirch: Checks whether a handler type implements IHostOperationHandler<TPayload, TResponse> for the registered types.
    public static bool HandlerMatchesRegistration(HostOperationHandlerRegistration? registration)
    {
        if (registration is null || !IsRegistrationValid(registration))
        {
            return false;
        }

        return registration.HandlerType
            .GetInterfaces()
            .Any(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IHostOperationHandler<,>)
                && candidate.GetGenericArguments()[0] == registration.PayloadType
                && candidate.GetGenericArguments()[1] == registration.ResponseType);
    }

    // Kirch: Validates the full handler registration contract, not just the shallow field shape.
    public static bool IsStrictRegistrationValid(HostOperationHandlerRegistration? registration)
    {
        return IsRegistrationValid(registration)
            && HandlerMatchesRegistration(registration);
    }
}

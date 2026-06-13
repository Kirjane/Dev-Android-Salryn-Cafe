using Salryn.Core.Common;
using Salryn.Core.Contracts;

namespace Salryn.Core.Hosting;

// Kirch: Categorizes why a future Host request is rejected before reaching handlers or infrastructure.
public enum HostBoundaryFailureReason
{
    MissingRequestId = 1,
    MissingOperationName = 2,
    MissingStoreIdentity = 3,
    MissingTerminalIdentity = 4,
    MissingAuthenticatedUserIdentity = 5,
    MissingCashierSessionIdentity = 6,
    UnknownOperationName = 7,
    OperationKindMismatch = 8
}

// Kirch: Represents a validation issue at the Host boundary without exposing transport or infrastructure details.
public sealed record HostBoundaryValidationIssue(
    HostBoundaryFailureReason Reason,
    string Code,
    string Message,
    ErrorType Type);

// Kirch: Carries the validation result for a Host request envelope before future dispatch happens.
public sealed record HostBoundaryValidationResult(
    bool IsValid,
    IReadOnlyList<HostBoundaryValidationIssue> Issues)
{
    // Kirch: Creates a successful boundary validation result.
    public static HostBoundaryValidationResult Valid()
    {
        return new HostBoundaryValidationResult(true, Array.Empty<HostBoundaryValidationIssue>());
    }

    // Kirch: Creates a failed boundary validation result.
    public static HostBoundaryValidationResult Invalid(IReadOnlyList<HostBoundaryValidationIssue> issues)
    {
        return new HostBoundaryValidationResult(false, issues);
    }
}

// Kirch: Defines the Core-side boundary validator contract before any Host project or transport exists.
public interface IHostRequestBoundaryValidator
{
    HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload> request);
}

// Kirch: Defines the future dispatch seam without implementing routing, HTTP, sockets, or handlers yet.
public interface IHostOperationDispatcher
{
    Task<HostResponseEnvelope<TResponse>> DispatchAsync<TPayload, TResponse>(
        HostRequestEnvelope<TPayload> request,
        CancellationToken cancellationToken = default);
}

// Kirch: Validates request identity rules against the Host operation catalog before Host execution exists.
public sealed class HostRequestBoundaryValidator : IHostRequestBoundaryValidator
{
    private readonly HostOperationCatalog _operationCatalog;

    // Kirch: Uses the default operation catalog for current SALRYN Host boundary validation.
    public HostRequestBoundaryValidator()
        : this(HostOperationCatalog.CreateDefault())
    {
    }

    // Kirch: Allows tests and future Host composition to inject a stricter or expanded operation catalog.
    public HostRequestBoundaryValidator(HostOperationCatalog operationCatalog)
    {
        _operationCatalog = operationCatalog ?? throw new ArgumentNullException(nameof(operationCatalog));
    }

    // Kirch: Validates common envelope rules and descriptor-specific operation requirements.
    public HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload> request)
    {
        List<HostBoundaryValidationIssue> issues = new();
        HostOperationDescriptor? descriptor = null;

        if (request.RequestId == Guid.Empty)
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingRequestId,
                "HostBoundary.MissingRequestId",
                "Host request must have a request id.",
                ErrorType.Validation));
        }

        if (string.IsNullOrWhiteSpace(request.OperationName))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingOperationName,
                "HostBoundary.MissingOperationName",
                "Host request must have an operation name.",
                ErrorType.Validation));
        }
        else
        {
            descriptor = _operationCatalog.Find(request.OperationName);

            if (descriptor is null)
            {
                issues.Add(new HostBoundaryValidationIssue(
                    HostBoundaryFailureReason.UnknownOperationName,
                    "HostBoundary.UnknownOperationName",
                    "Host request operation is not registered in the operation catalog.",
                    ErrorType.NotFound));
            }
            else if (descriptor.OperationKind != request.OperationKind)
            {
                issues.Add(new HostBoundaryValidationIssue(
                    HostBoundaryFailureReason.OperationKindMismatch,
                    "HostBoundary.OperationKindMismatch",
                    "Host request operation kind does not match the registered descriptor.",
                    ErrorType.Validation));
            }
        }

        bool requiresStoreIdentity = descriptor?.RequiresStoreIdentity ?? true;
        bool requiresTerminalIdentity = descriptor?.RequiresTerminalIdentity ?? false;
        bool requiresAuthenticatedUser = descriptor?.RequiresAuthenticatedUser
            ?? HostClientContractRules.RequiresAuthenticatedUser(request.OperationKind);
        bool requiresCashierSession = descriptor?.RequiresCashierSession
            ?? HostClientContractRules.RequiresCashierSession(request.OperationName);

        if (requiresStoreIdentity && !HostClientContractRules.HasStoreIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingStoreIdentity,
                "HostBoundary.MissingStoreIdentity",
                "Host request must carry tenant and store identity.",
                ErrorType.Validation));
        }

        if (requiresTerminalIdentity && !HostClientContractRules.HasTerminalIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingTerminalIdentity,
                "HostBoundary.MissingTerminalIdentity",
                "Host request must carry terminal identity.",
                ErrorType.Validation));
        }

        if (requiresAuthenticatedUser && !HostClientContractRules.HasAuthenticatedCommandIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingAuthenticatedUserIdentity,
                "HostBoundary.MissingAuthenticatedUserIdentity",
                "Host request must carry user, role, terminal, and device identity.",
                ErrorType.Unauthorized));
        }

        if (requiresCashierSession && !request.Context.CashierSessionId.HasValue)
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingCashierSessionIdentity,
                "HostBoundary.MissingCashierSessionIdentity",
                "Cashier-bound operation must carry cashier session identity.",
                ErrorType.Validation));
        }

        return issues.Count == 0
            ? HostBoundaryValidationResult.Valid()
            : HostBoundaryValidationResult.Invalid(issues);
    }
}

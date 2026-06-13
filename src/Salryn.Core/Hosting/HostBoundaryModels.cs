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
    MissingCashierSessionIdentity = 6
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

// Kirch: Validates request identity rules at the Core boundary before Host execution exists.
public sealed class HostRequestBoundaryValidator : IHostRequestBoundaryValidator
{
    // Kirch: Validates the common Host request envelope requirements.
    public HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload> request)
    {
        List<HostBoundaryValidationIssue> issues = new();

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

        if (!HostClientContractRules.HasStoreIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingStoreIdentity,
                "HostBoundary.MissingStoreIdentity",
                "Host request must carry tenant and store identity.",
                ErrorType.Validation));
        }

        bool isLogin = string.Equals(
            request.OperationName,
            HostOperationNames.Login,
            StringComparison.Ordinal);

        if (isLogin && !HostClientContractRules.HasTerminalIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingTerminalIdentity,
                "HostBoundary.MissingTerminalIdentity",
                "Login request must carry terminal identity.",
                ErrorType.Validation));
        }

        if (!isLogin
            && HostClientContractRules.RequiresAuthenticatedUser(request.OperationKind)
            && !HostClientContractRules.HasAuthenticatedCommandIdentity(request.Context))
        {
            issues.Add(new HostBoundaryValidationIssue(
                HostBoundaryFailureReason.MissingAuthenticatedUserIdentity,
                "HostBoundary.MissingAuthenticatedUserIdentity",
                "Command request must carry user, role, terminal, and device identity.",
                ErrorType.Unauthorized));
        }

        if (HostClientContractRules.RequiresCashierSession(request.OperationName)
            && !request.Context.CashierSessionId.HasValue)
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

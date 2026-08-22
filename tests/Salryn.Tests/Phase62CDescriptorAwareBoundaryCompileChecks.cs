using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Tests;

// Kirch: Verifies Phase 62C descriptor-aware Host boundary validation compiles without Host runtime or transport.
public static class Phase62CDescriptorAwareBoundaryCompileChecks
{
    // Kirch: This compile check proves validation now reads HostOperationCatalog descriptor requirements.
    public static void Run()
    {
        HostRequestBoundaryValidator validator = new();

        HostRequestContext fullContext = new(
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            RoleId: Guid.NewGuid(),
            CashierSessionId: Guid.NewGuid(),
            TerminalId: Guid.NewGuid(),
            DeviceKey: "sample-device-key",
            RequestedAt: DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> validCheckout = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.CompleteSale,
            Context: fullContext,
            Payload: "sample-checkout");

        HostBoundaryValidationResult validCheckoutResult = validator.Validate(validCheckout);

        if (!validCheckoutResult.IsValid)
        {
            throw new InvalidOperationException("Descriptor-aware validator should accept a valid checkout request.");
        }

        HostRequestEnvelope<string> mismatchedKind = validCheckout with
        {
            OperationKind = HostOperationKind.Query
        };

        HostBoundaryValidationResult mismatchedKindResult = validator.Validate(mismatchedKind);

        if (!mismatchedKindResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.OperationKindMismatch))
        {
            throw new InvalidOperationException("Descriptor-aware validator should reject operation kind mismatch.");
        }

        HostRequestEnvelope<string> unknownOperation = validCheckout with
        {
            OperationName = "unknown.operation"
        };

        HostBoundaryValidationResult unknownOperationResult = validator.Validate(unknownOperation);

        if (!unknownOperationResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.UnknownOperationName))
        {
            throw new InvalidOperationException("Descriptor-aware validator should reject unknown operations.");
        }

        HostRequestContext terminalOnlyContext = new(
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            UserId: null,
            RoleId: null,
            CashierSessionId: null,
            TerminalId: Guid.NewGuid(),
            DeviceKey: "sample-device-key",
            RequestedAt: DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> loginRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.Login,
            Context: terminalOnlyContext,
            Payload: "sample-login");

        HostBoundaryValidationResult loginResult = validator.Validate(loginRequest);

        if (!loginResult.IsValid)
        {
            throw new InvalidOperationException("Login descriptor should allow terminal identity before user identity exists.");
        }

        HostOperationCatalog strictQueryCatalog = new(new[]
        {
            new HostOperationDescriptor(
                OperationName: "strict.query",
                OperationKind: HostOperationKind.Query,
                RequiresStoreIdentity: true,
                RequiresTerminalIdentity: true,
                RequiresAuthenticatedUser: true,
                RequiresCashierSession: false,
                AllowsReadOnlyMobileSnapshotAccess: false)
        });

        HostRequestBoundaryValidator strictValidator = new(strictQueryCatalog);

        HostRequestEnvelope<string> strictQueryWithoutUser = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: "strict.query",
            Context: terminalOnlyContext,
            Payload: "sample-query");

        HostBoundaryValidationResult strictQueryResult = strictValidator.Validate(strictQueryWithoutUser);

        if (!strictQueryResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.MissingAuthenticatedUserIdentity))
        {
            throw new InvalidOperationException("Descriptor-aware validator should enforce authenticated query descriptors.");
        }
    }
}

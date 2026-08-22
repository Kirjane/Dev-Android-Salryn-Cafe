using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Tests;

// Kirch: Verifies Phase 62A Host boundary validation compiles without creating a Host project or transport layer.
public static class Phase62AHostBoundaryCompileChecks
{
    // Kirch: This compile check protects boundary validation rules for future Host dispatch work.
    public static void Run()
    {
        HostRequestBoundaryValidator validator = new();

        HostRequestContext checkoutContext = new(
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            RoleId: Guid.NewGuid(),
            CashierSessionId: Guid.NewGuid(),
            TerminalId: Guid.NewGuid(),
            DeviceKey: "sample-device-key",
            RequestedAt: DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> checkoutRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.CompleteSale,
            Context: checkoutContext,
            Payload: "sample-checkout-payload");

        HostBoundaryValidationResult checkoutResult = validator.Validate(checkoutRequest);

        if (!checkoutResult.IsValid)
        {
            throw new InvalidOperationException("Valid checkout Host request should pass boundary validation.");
        }

        HostRequestContext loginContext = new(
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
            Context: loginContext,
            Payload: "sample-login-payload");

        HostBoundaryValidationResult loginResult = validator.Validate(loginRequest);

        if (!loginResult.IsValid)
        {
            throw new InvalidOperationException("Login Host request should pass with terminal identity before user identity exists.");
        }

        HostRequestContext invalidContext = new(
            TenantId: Guid.Empty,
            StoreId: Guid.Empty,
            UserId: null,
            RoleId: null,
            CashierSessionId: null,
            TerminalId: null,
            DeviceKey: null,
            RequestedAt: DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> invalidRequest = new(
            RequestId: Guid.Empty,
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.CompleteSale,
            Context: invalidContext,
            Payload: "invalid-payload");

        HostBoundaryValidationResult invalidResult = validator.Validate(invalidRequest);

        if (invalidResult.IsValid || invalidResult.Issues.Count == 0)
        {
            throw new InvalidOperationException("Invalid Host request should fail boundary validation.");
        }
    }
}

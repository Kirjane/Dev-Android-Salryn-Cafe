using Salryn.Core.Common;
using Salryn.Core.Contracts;

namespace Salryn.Tests;

// Kirch: Verifies the Phase 61C Host/client contract boundary compiles without adding networking or Host projects.
public static class Phase61CHostClientContractsCompileChecks
{
    // Kirch: This compile check protects the request, response, and identity contract shapes for future Host work.
    public static void Run()
    {
        HostRequestContext context = new(
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            RoleId: Guid.NewGuid(),
            CashierSessionId: Guid.NewGuid(),
            TerminalId: Guid.NewGuid(),
            DeviceKey: "sample-device-key",
            RequestedAt: DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> request = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.CompleteSale,
            Context: context,
            Payload: "sample-payload");

        HostResponseEnvelope<string> success = new(
            RequestId: request.RequestId,
            IsSuccess: true,
            Payload: "sample-result",
            Error: null);

        HostContractError error = new(
            Code: "Contracts.SampleFailure",
            Message: "Sample contract failure.",
            Type: ErrorType.Failure);

        HostResponseEnvelope<string> failure = new(
            RequestId: request.RequestId,
            IsSuccess: false,
            Payload: null,
            Error: error);

        if (!HostClientContractRules.HasStoreIdentity(context))
        {
            throw new InvalidOperationException("Host requests must carry store identity.");
        }

        if (!HostClientContractRules.HasAuthenticatedCommandIdentity(context))
        {
            throw new InvalidOperationException("Command requests must carry authenticated command identity.");
        }

        if (!HostClientContractRules.RequiresCashierSession(request.OperationName))
        {
            throw new InvalidOperationException("Complete sale should require cashier session identity.");
        }

        if (!success.IsSuccess || failure.Error is null)
        {
            throw new InvalidOperationException("Host response envelopes should represent success and failure.");
        }
    }
}

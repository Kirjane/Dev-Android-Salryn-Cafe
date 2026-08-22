using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Tests;

// Kirch: Verifies Phase 62E Host boundary safety checks before runtime transport exists.
public static class Phase62EHostBoundaryRuntimeSafetyCompileChecks
{
    // Kirch: This compile check protects malformed Host request handling and strict handler registration rules.
    public static void Run()
    {
        HostRequestBoundaryValidator validator = new();

        HostBoundaryValidationResult nullRequestResult = validator.Validate<string>(null);

        if (nullRequestResult.IsValid
            || !nullRequestResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.MissingRequestEnvelope))
        {
            throw new InvalidOperationException("Null Host request envelope should fail safely.");
        }

        HostRequestEnvelope<string> missingContextRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.Login,
            Context: null!,
            Payload: "missing-context");

        HostBoundaryValidationResult missingContextResult = validator.Validate(missingContextRequest);

        if (missingContextResult.IsValid
            || !missingContextResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.MissingRequestContext))
        {
            throw new InvalidOperationException("Missing Host request context should fail safely.");
        }

        bool nullCatalogRejected = false;

        try
        {
            _ = new HostOperationCatalog(null!);
        }
        catch (ArgumentNullException)
        {
            nullCatalogRejected = true;
        }

        if (!nullCatalogRejected)
        {
            throw new InvalidOperationException("Null Host operation catalog descriptor collection should be rejected.");
        }

        HostOperationHandlerRegistration validRegistration = new(
            OperationName: "runtime.safety.sample",
            HandlerType: typeof(SampleRuntimeSafetyHandler),
            PayloadType: typeof(string),
            ResponseType: typeof(string));

        if (!HostOperationRegistrationRules.IsStrictRegistrationValid(validRegistration))
        {
            throw new InvalidOperationException("Strict handler registration should accept matching handler types.");
        }

        HostOperationHandlerRegistration invalidRegistration = validRegistration with
        {
            HandlerType = typeof(string)
        };

        if (HostOperationRegistrationRules.IsStrictRegistrationValid(invalidRegistration))
        {
            throw new InvalidOperationException("Strict handler registration should reject mismatched handler types.");
        }
    }

    // Kirch: Sample handler exists only to prove strict registration validation; it is not runtime infrastructure.
    private sealed class SampleRuntimeSafetyHandler : IHostOperationHandler<string, string>
    {
        public Task<HostResponseEnvelope<string>> HandleAsync(
            HostRequestEnvelope<string> request,
            CancellationToken cancellationToken = default)
        {
            HostResponseEnvelope<string> response = new(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: "runtime-safety-ok",
                Error: null);

            return Task.FromResult(response);
        }
    }
}

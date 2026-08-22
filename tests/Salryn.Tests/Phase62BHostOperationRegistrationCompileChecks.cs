using Salryn.Core.Contracts;
using Salryn.Core.Hosting;

namespace Salryn.Tests;

// Kirch: Verifies Phase 62B Host operation descriptors and handler registration seams compile without Host runtime.
public static class Phase62BHostOperationRegistrationCompileChecks
{
    // Kirch: This compile check protects the future Host operation catalog and handler registration theory.
    public static void Run()
    {
        HostOperationCatalog catalog = HostOperationCatalog.CreateDefault();

        HostOperationDescriptor? completeSale = catalog.Find(HostOperationNames.CompleteSale);

        if (completeSale is null)
        {
            throw new InvalidOperationException("Complete sale descriptor should exist.");
        }

        if (completeSale.OperationKind != HostOperationKind.Command
            || !completeSale.RequiresAuthenticatedUser
            || !completeSale.RequiresTerminalIdentity
            || !completeSale.RequiresCashierSession)
        {
            throw new InvalidOperationException("Complete sale descriptor should require authenticated cashier identity.");
        }

        HostOperationDescriptor? login = catalog.Find(HostOperationNames.Login);

        if (login is null || login.RequiresAuthenticatedUser || !login.RequiresTerminalIdentity)
        {
            throw new InvalidOperationException("Login descriptor should require terminal identity but not user identity.");
        }

        HostOperationDescriptor? dailySalesSummary = catalog.Find(HostOperationNames.DailySalesSummary);

        if (dailySalesSummary is null
            || dailySalesSummary.OperationKind != HostOperationKind.Query
            || !dailySalesSummary.AllowsReadOnlyMobileSnapshotAccess)
        {
            throw new InvalidOperationException("Daily sales summary should be read-only mobile snapshot safe.");
        }

        HostOperationHandlerRegistration registration = new(
            OperationName: HostOperationNames.DailySalesSummary,
            HandlerType: typeof(SampleDailySalesSummaryHandler),
            PayloadType: typeof(string),
            ResponseType: typeof(string));

        if (!HostOperationRegistrationRules.IsRegistrationValid(registration))
        {
            throw new InvalidOperationException("Sample Host handler registration should be structurally valid.");
        }

        if (!HostOperationRegistrationRules.HandlerMatchesRegistration(registration))
        {
            throw new InvalidOperationException("Sample Host handler should match registered payload and response types.");
        }

        bool duplicateRejected = false;

        try
        {
            _ = new HostOperationCatalog(new[] { completeSale, completeSale });
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        if (!duplicateRejected)
        {
            throw new InvalidOperationException("Duplicate Host operation descriptors should be rejected.");
        }
    }

    // Kirch: Sample handler exists only to prove registration shape; it is not part of a Host runtime.
    private sealed class SampleDailySalesSummaryHandler : IHostOperationHandler<string, string>
    {
        public Task<HostResponseEnvelope<string>> HandleAsync(
            HostRequestEnvelope<string> request,
            CancellationToken cancellationToken = default)
        {
            HostResponseEnvelope<string> response = new(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: "sample-summary",
                Error: null);

            return Task.FromResult(response);
        }
    }
}

using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Security;

const string SmokeArgument = "--smoke";
const string SmokeIfSupportedArgument = "--smoke-if-supported";

if (IsSmokeRequested(args))
{
    await RunSmokeAsync(args).ConfigureAwait(false);
    return;
}

WebApplication app = await LocalApiApplicationFactory.CreateAsync(args).ConfigureAwait(false);
app.Run();

static bool IsSmokeRequested(string[] args)
{
    return args.Any(item =>
        string.Equals(item, SmokeArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item, SmokeIfSupportedArgument, StringComparison.OrdinalIgnoreCase));
}

static async Task RunSmokeAsync(string[] args)
{
    SalrynLocalComposition composition = SalrynLocalLaunchModeResolver.CreateComposition(args);
    await composition.InitializeDatabaseAsync().ConfigureAwait(false);

    DashboardSummaryService summaryService = new(composition);
    InMemoryLocalApiSessionStore sessionStore = new();
    LocalApiSessionService sessionService = new(composition.Authentication, sessionStore);

    SessionResponse unauthenticated = sessionService.GetSession(null);
    if (unauthenticated.IsAuthenticated)
    {
        throw new InvalidOperationException("API smoke expected unauthenticated session by default.");
    }

    Result<LocalApiSession> invalidLogin = await sessionService.LoginAsync(
        new LoginRequest("web-a2-smoke-invalid", "not-a-real-password")).ConfigureAwait(false);

    if (invalidLogin.IsSuccess)
    {
        throw new InvalidOperationException("API smoke expected invalid login to fail.");
    }

    var summary = await summaryService.GetSummaryAsync().ConfigureAwait(false);

    OperatorReadService operatorReads = new(composition);
    Result<PagedApiResponse<ProductListItemResponse>> productsProbe =
        await operatorReads.SearchProductsAsync(null, null, null, null, 1, 1).ConfigureAwait(false);

    if (productsProbe.IsFailure)
    {
        throw new InvalidOperationException(
            $"API smoke expected products read probe to succeed: {productsProbe.ErrorDetail.Message}");
    }

    RemoteReadOnlyApiService remoteReadOnly = new(summaryService, operatorReads);
    if (!string.Equals(remoteReadOnly.GetStatus().Mode, "LocalOnlyReadMode", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Remote read-only API smoke expected LocalOnlyReadMode status.");
    }

    Console.WriteLine(
        $"API smoke passed: session=unauthenticated, invalidLogin={invalidLogin.ErrorDetail.Code}, products={summary.ProductCount}, lowStock={summary.LowStockCount}, readProbeItems={productsProbe.Value.Items.Count}");
}

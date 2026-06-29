using Salryn.Api.Local;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Services;

if (ApiLaunchModeResolver.IsSmokeRequested(args))
{
    await RunSmokeAsync(args).ConfigureAwait(false);
    return;
}

SalrynApiComposition composition = ApiLaunchModeResolver.CreateComposition(args);
await composition.InitializeDatabaseAsync().ConfigureAwait(false);

DashboardSummaryService dashboardSummaryService = new(composition);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5299");

builder.Services.AddSingleton(composition);
builder.Services.AddSingleton(dashboardSummaryService);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

WebApplication app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Salryn.Api.Local"
}));

app.MapGet("/api/dashboard/summary", async (
    DashboardSummaryService summaryService,
    CancellationToken cancellationToken) =>
{
    var summary = await summaryService.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(summary);
});

app.Run();

static async Task RunSmokeAsync(string[] args)
{
    SalrynApiComposition composition = ApiLaunchModeResolver.CreateComposition(args);
    await composition.InitializeDatabaseAsync().ConfigureAwait(false);

    DashboardSummaryService summaryService = new(composition);
    var summary = await summaryService.GetSummaryAsync().ConfigureAwait(false);

    Console.WriteLine(
        $"API smoke passed: status={summary.Status}, products={summary.ProductCount}, lowStock={summary.LowStockCount}");
}

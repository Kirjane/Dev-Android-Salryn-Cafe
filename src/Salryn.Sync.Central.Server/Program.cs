using Salryn.Sync.Central.Server;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

CentralServerOptions options = new();
builder.Configuration.GetSection("CentralSync").Bind(options);

string? envDbPath = Environment.GetEnvironmentVariable("SALRYN_CAFE_CENTRAL_DB_PATH");
if (!string.IsNullOrWhiteSpace(envDbPath))
{
    options.DatabasePath = envDbPath;
}

string? envRequireHttps = Environment.GetEnvironmentVariable("SALRYN_CAFE_CENTRAL_REQUIRE_HTTPS");
if (!string.IsNullOrWhiteSpace(envRequireHttps) && bool.TryParse(envRequireHttps, out bool parsedHttps))
{
    options.RequireHttps = parsedHttps;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CentralSyncDatabase>();
builder.Services.AddSingleton<CentralSyncService>();

WebApplication app = builder.Build();

CentralSyncDatabase db = app.Services.GetRequiredService<CentralSyncDatabase>();
db.Initialize();

app.MapCentralSyncEndpoints();
app.Run();

public partial class Program;

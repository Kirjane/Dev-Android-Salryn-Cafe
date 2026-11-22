using Salryn.LicensingIssuer.Server;

string dbPath = Environment.GetEnvironmentVariable("SALRYN_ISSUER_DB_PATH")?.Trim()
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SalrynLicensingIssuer", "issuer.db");

string listenUrl = Environment.GetEnvironmentVariable("SALRYN_ISSUER_URL")?.Trim()
    ?? "http://127.0.0.1:5410";

string? webRoot = Environment.GetEnvironmentVariable("SALRYN_ISSUER_WEB_ROOT")?.Trim();
if (string.IsNullOrWhiteSpace(webRoot))
{
    string repoCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "apps", "licensing-web", "dist"));
    if (Directory.Exists(repoCandidate))
    {
        webRoot = repoCandidate;
    }
}

var options = new LicensingIssuerHostOptions
{
    DatabasePath = dbPath,
    ListenUrl = listenUrl,
    BootstrapAdminUsername = Environment.GetEnvironmentVariable("SALRYN_ISSUER_ADMIN_USERNAME")?.Trim(),
    BootstrapAdminDisplayName = Environment.GetEnvironmentVariable("SALRYN_ISSUER_ADMIN_DISPLAY_NAME")?.Trim(),
    BootstrapAdminPassword = Environment.GetEnvironmentVariable("SALRYN_ISSUER_ADMIN_PASSWORD"),
    StaticWebRootPath = webRoot,
    SignerOptions = ExternalLicenseSignerService.FromEnvironment()
};

var app = LicensingIssuerAppFactory.Create(args, options);
app.Run();

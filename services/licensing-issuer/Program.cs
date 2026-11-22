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
    BootstrapReviewerUsername = Environment.GetEnvironmentVariable("SALRYN_ISSUER_REVIEWER_USERNAME")?.Trim(),
    BootstrapReviewerDisplayName = Environment.GetEnvironmentVariable("SALRYN_ISSUER_REVIEWER_DISPLAY_NAME")?.Trim(),
    BootstrapReviewerPassword = Environment.GetEnvironmentVariable("SALRYN_ISSUER_REVIEWER_PASSWORD"),
    BootstrapOperatorUsername = Environment.GetEnvironmentVariable("SALRYN_ISSUER_OPERATOR_USERNAME")?.Trim(),
    BootstrapOperatorDisplayName = Environment.GetEnvironmentVariable("SALRYN_ISSUER_OPERATOR_DISPLAY_NAME")?.Trim(),
    BootstrapOperatorPassword = Environment.GetEnvironmentVariable("SALRYN_ISSUER_OPERATOR_PASSWORD"),
    AllowSelfApprovalException = string.Equals(
        Environment.GetEnvironmentVariable("SALRYN_ISSUER_ALLOW_SELF_APPROVAL_EXCEPTION")?.Trim(),
        "1",
        StringComparison.Ordinal),
    StaticWebRootPath = webRoot,
    SignerOptions = ExternalLicenseSignerService.FromEnvironment()
};

var app = LicensingIssuerAppFactory.Create(args, options);
app.Run();

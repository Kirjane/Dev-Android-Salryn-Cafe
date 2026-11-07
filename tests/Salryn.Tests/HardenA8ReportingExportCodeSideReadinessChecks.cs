namespace Salryn.Tests;

public static class HardenA8ReportingExportCodeSideReadinessChecks
{
    public static void Run()
    {
        string page = ReadRepoFile("src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx");
        string client = ReadRepoFile("src/Salryn.App.Web/src/api/productionReports.ts");
        string endpoints = ReadRepoFile("src/Salryn.Api.Local/LocalProductionReportEndpoints.cs");
        string artifactPolicy = ReadRepoFile("src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactRootPolicy.cs");

        Require(page.Contains("Native PDF primary", StringComparison.Ordinal), "HARDEN-A8 UI must state native PDF is primary.");
        Require(page.Contains("Crystal parked", StringComparison.Ordinal), "HARDEN-A8 UI must state Crystal is parked.");
        Require(page.Contains("No printer integration", StringComparison.Ordinal), "HARDEN-A8 UI must state printer integration is absent.");
        Require(page.Contains("Export native PDF", StringComparison.Ordinal), "HARDEN-A8 UI must expose the existing native PDF export action.");
        Require(page.Contains("No caller path input", StringComparison.Ordinal), "HARDEN-A8 UI must state caller path input is absent.");
        Require(page.Contains("relativeArtifactPath", StringComparison.Ordinal), "HARDEN-A8 UI must surface relative artifact metadata when present.");
        Require(page.Contains("relativeManifestPath", StringComparison.Ordinal), "HARDEN-A8 UI must surface manifest metadata when present.");
        Require(page.Contains("pdfSha256", StringComparison.Ordinal), "HARDEN-A8 UI must surface PDF hash metadata when present.");
        Require(page.Contains("callerPathAccepted", StringComparison.Ordinal), "HARDEN-A8 UI must surface caller-path acceptance metadata when present.");

        Require(client.Contains("/api/production-reports/renderer/status", StringComparison.Ordinal), "HARDEN-A8 client must read renderer status.");
        Require(client.Contains("/api/production-reports/export", StringComparison.Ordinal), "HARDEN-A8 client must use existing export endpoint.");
        Require(client.Contains("requestedOutputKind: \"pdf\"", StringComparison.Ordinal), "HARDEN-A8 client must constrain export request to PDF.");
        Require(!client.Contains("outputPath", StringComparison.OrdinalIgnoreCase), "HARDEN-A8 client must not send caller output paths.");
        Require(!client.Contains("outputDirectory", StringComparison.OrdinalIgnoreCase), "HARDEN-A8 client must not add caller-controlled output directories.");

        Require(endpoints.Contains("reportingEngine = \"native-pdf-primary\"", StringComparison.Ordinal), "HARDEN-A8 backend must keep native PDF primary status.");
        Require(endpoints.Contains("disabledByDefault = true", StringComparison.Ordinal), "HARDEN-A8 Crystal compatibility must remain disabled by default.");
        Require(endpoints.Contains("MapPost(\"/api/production-reports/export\"", StringComparison.Ordinal), "HARDEN-A8 export endpoint must remain mapped.");
        Require(endpoints.Contains("Guard(httpContext, sessions", StringComparison.Ordinal), "HARDEN-A8 report endpoints must remain guarded.");
        Require(endpoints.Contains("Reports.Forbidden", StringComparison.Ordinal), "HARDEN-A8 report permission denial must remain deterministic.");

        Require(artifactPolicy.Contains("RejectCallerPath", StringComparison.Ordinal), "HARDEN-A8 artifact policy must reject caller paths.");
        Require(artifactPolicy.Contains("EnsureSafeRelativePath", StringComparison.Ordinal), "HARDEN-A8 artifact policy must validate safe relative paths.");
        Require(artifactPolicy.Contains("path traversal is forbidden", StringComparison.OrdinalIgnoreCase), "HARDEN-A8 artifact policy must reject path traversal.");

        string changedFrontend = page + Environment.NewLine + client;
        string[] forbiddenFrontendTerms =
        [
            "PrinterSettings",
            "SAP",
            ".rpt",
            "Vercel",
            "CloudUpload",
            "eSales production",
            "BIR approved",
            "outputDirectory",
            "outputPath"
        ];

        foreach (string term in forbiddenFrontendTerms)
        {
            Require(
                !changedFrontend.Contains(term, StringComparison.OrdinalIgnoreCase),
                "HARDEN-A8 forbidden reporting/export frontend term found: " + term);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            throw new InvalidOperationException("Unable to locate repository root from " + Directory.GetCurrentDirectory());
        }

        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required HARDEN-A8 source file was not found.", path);
        }

        return File.ReadAllText(path);
    }

    private static string? FindRepoRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

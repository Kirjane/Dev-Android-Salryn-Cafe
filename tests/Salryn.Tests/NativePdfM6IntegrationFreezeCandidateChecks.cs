using Salryn.Api.Local;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports;
using Salryn.Infrastructure.Reports.NativePdf;

namespace Salryn.Tests;

public static class NativePdfM6IntegrationFreezeCandidateChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string endpointSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs"));
        string datasetServiceSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "PreCrystalReportDatasetService.cs"));
        string artifactContractsSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "Reports", "ReportArtifactContracts.cs"));
        string rootPolicySource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfArtifactRootPolicy.cs"));
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "REPORTING_CLOSURE_M6_DONE_DEFINITION_EVIDENCE_MAP.md")), "M6 done-definition evidence map required.");
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "REPORTING_CLOSURE_M6_INTEGRATION_FREEZE_CANDIDATE_REPORT.md")), "M6 freeze candidate report required.");

        RequireContains(coreSmoke, "reports-native-pdf-m6-integration-freeze-candidate", "Focused M6 CoreSmoke route required.");
        RequireContains(coreSmoke, "NativePdfM6IntegrationFreezeCandidateChecks.Run()", "Full CoreSmoke must run M6 checks.");

        RequireContains(endpointSource, "MapPost(\"/api/production-reports/export\"", "Export endpoint must remain mapped.");
        RequireContains(endpointSource, "ProductionReportExportApiRequest", "Export request contract required.");
        RequireContains(endpointSource, "TryBuildDatasetRequest", "Export path must rebuild a server-owned dataset from filters.");
        RequireContains(endpointSource, "ComposeDatasetService(connectionFactory)", "Export path must use server-side dataset service.");
        RequireContains(endpointSource, "NativePdfReportExportService", "Export path must use native PDF export service.");
        RequireContains(endpointSource, "Guard(httpContext, sessions, catalogItem.RequiredPermission)", "Report catalog permission guard must be preserved.");
        RequireContains(endpointSource, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Loopback/local operator guard must be preserved.");
        RequireContains(endpointSource, "ApiAuthExtensions.RequireAuthenticatedSession", "Authenticated session guard must be preserved.");
        RequireContains(endpointSource, "ProductionReportDatasetLimits.MaxRange", "Range cap must be preserved.");
        RequireContains(endpointSource, "DocumentKind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT", "Document kind allow-list must be preserved.");

        string[] exportProperties = typeof(LocalProductionReportEndpoints.ProductionReportExportApiRequest)
            .GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        RequireSequence(exportProperties, new[]
        {
            "BirMachineProfileId",
            "DocumentKind",
            "RangeEnd",
            "RangeStart",
            "RegistrationScopeCode",
            "ReportKind",
            "RequestedOutputKind",
            "TerminalId"
        }, "Export request contract must accept filters and output kind only.");

        RequireNoProperty(exportProperties, "OutputRoot");
        RequireNoProperty(exportProperties, "OutputPath");
        RequireNoProperty(exportProperties, "ArtifactPath");
        RequireNoProperty(exportProperties, "TemplatePath");
        RequireNoProperty(exportProperties, "Sql");
        RequireNoProperty(exportProperties, "SqlText");
        RequireNoProperty(exportProperties, "Printer");
        RequireNoProperty(exportProperties, "PrinterName");

        RequireNotContains(endpointSource, "OutputRoot", "Endpoint must not expose output root.");
        RequireNotContains(endpointSource, "OutputPath", "Endpoint must not expose output path.");
        RequireNotContains(endpointSource, "TemplatePath", "Endpoint must not expose template path.");
        RequireNotContains(endpointSource, "SqlText", "Endpoint must not expose SQL text.");
        RequireNotContains(endpointSource, "PrinterName", "Endpoint must not expose printer fields.");

        RequireContains(endpointSource, "reportingEngine = \"native-pdf-primary\"", "Renderer status must identify native PDF as primary.");
        RequireContains(endpointSource, "primaryRenderer", "Renderer status must expose primary renderer.");
        RequireContains(endpointSource, "primaryCapability", "Renderer status must expose primary capability.");
        RequireContains(endpointSource, "crystalCompatibility", "Renderer status must expose Crystal parked compatibility.");
        RequireContains(endpointSource, "optional-compatibility-parked", "Crystal status must be parked optional compatibility.");
        RequireContains(endpointSource, "disabledByDefault = true", "Crystal compatibility must be disabled by default.");

        NativePdfReportRenderer nativeRenderer = new();
        ProductionReportRendererStatus nativeStatus = nativeRenderer.GetStatus();
        ProductionReportRendererCapability nativeCapability = nativeRenderer.GetCapability();
        RequireTrue(nativeStatus.Available, "Native PDF renderer must be the available primary renderer candidate.");
        RequireTrue(nativeStatus.CanRenderPdf, "Native PDF renderer must render PDFs.");
        RequireTrue(!nativeStatus.CanPrint, "Native PDF renderer must not claim printer support.");
        RequireTrue(!nativeStatus.RequiresExternalRuntime, "Native PDF renderer must not require external runtime.");
        RequireTrue(nativeCapability.DirectInProcessSupported, "Native PDF capability must support direct in-process rendering.");
        RequireTrue(!nativeCapability.IsolatedWorkerSupported, "Native PDF primary path must not require isolated worker.");

        UnavailableCrystalReportRenderer crystalRenderer = new();
        ProductionReportRendererStatus crystalStatus = crystalRenderer.GetStatus();
        ProductionReportRendererCapability crystalCapability = crystalRenderer.GetCapability();
        RequireTrue(!crystalStatus.Available, "Crystal renderer remains unavailable/parked.");
        RequireTrue(!crystalStatus.CanRenderPdf, "Crystal renderer must not claim PDF capability.");
        RequireTrue(!crystalStatus.CanPrint, "Crystal renderer must not claim printer support.");
        RequireTrue(!crystalCapability.DirectInProcessSupported, "Crystal direct in-process path remains unsupported.");
        RequireTrue(crystalCapability.IsolatedWorkerSupported, "Crystal remains isolated compatibility worker only.");

        RequireContains(datasetServiceSource, "BirAccreditationClaimed: false", "BIR accreditation must remain unclaimed.");
        RequireContains(datasetServiceSource, "BirESalesSchemaConfirmed: projection.BirAuthoritativeESalesSchemaConfirmed", "eSales schema flag must come from projection and remain explicit.");
        RequireContains(datasetServiceSource, "ESalesTransmissionEnabled: projection.TransmissionEnabled", "eSales transmission flag must come from projection and remain explicit.");
        RequireContains(datasetServiceSource, "Internal eSales draft dataset only", "Internal eSales draft boundary must remain visible.");
        RequireContains(datasetServiceSource, "transmission disabled", "Internal eSales transmission must remain disabled/unclaimed in summary text.");

        RequireContains(artifactContractsSource, "CallerPathAccepted", "Artifact manifest must record whether caller path was accepted.");
        RequireContains(artifactContractsSource, "RetentionCleanupNote", "Artifact manifest must record retention/cleanup note.");
        RequireContains(rootPolicySource, "path traversal is forbidden", "Root policy must reject traversal.");
        RequireContains(rootPolicySource, "Absolute report artifact paths are forbidden", "Root policy must reject absolute caller paths.");
    }

    private static string FindRoot()
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

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + expected);
        }
    }

    private static void RequireNotContains(string text, string forbidden, string message)
    {
        if (text.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + forbidden);
        }
    }

    private static void RequireNoProperty(IEnumerable<string> properties, string forbidden)
    {
        if (properties.Any(x => string.Equals(x, forbidden, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Export request must not expose property: " + forbidden);
        }
    }

    private static void RequireSequence(string[] actual, string[] expected, string message)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(message + " Actual: " + string.Join(", ", actual));
        }
    }
}

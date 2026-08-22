namespace Salryn.Tests;

public static class J22OfficialOutputTextFilePackageSupplyBoundaryChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string supplyBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string uiBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string appContracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowContracts.cs");
        string readinessContracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowReadinessContracts.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupplySummary", "10H package supply summary must exist.");
        RequireContains(supplyBoundary, "IOfficialOutputTextFilePackageSupplyBoundary", "10H package supply boundary contract must exist.");
        RequireContains(supplyBoundary, "GetAvailablePackageSummary", "10H package supply must expose summary behavior.");
        RequireContains(supplyBoundary, "Result<OfficialOutputExportPackage> TryGetPackage", "10H package supply must expose trusted package retrieval.");
        RequireContains(supplyBoundary, "OfficialOutputExportPackage? trustedPackage = null", "10H package supply must allow no currently supplied package.");
        RequireContains(supplyBoundary, "IOfficialOutputTextFileTrustedPackageProducer? trustedPackageProducer = null", "RMO-M1-A package supply must accept a trusted package producer.");
        RequireContains(supplyBoundary, "OfficialOutputTextFileTrustedPackageProducerRequest? producerRequest = null", "RMO-M1-A package supply must accept a producer source request.");
        RequireContains(supplyBoundary, "IOfficialOutputTextFileSourceSelectionBoundary? sourceSelectionBoundary = null", "RMO-M1-B2 package supply must accept a source-selection boundary.");
        RequireContains(supplyBoundary, "OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest", "RMO-M1-B2 package supply must accept selected source requests.");
        RequireContains(supplyBoundary, "FromTrustedPackage", "10H package supply must allow a trusted package to be supplied explicitly.");
        RequireContains(supplyBoundary, "FromTrustedPackageProducer", "RMO-M1-A package supply must allow producer-backed package supply.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.NoTrustedPackage", "10H package supply must fail safely when no package exists.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.ProducerRequestRequired", "RMO-M1-A package supply must stay unavailable until an official source request is provided.");
        RequireContains(supplyBoundary, "Result<OfficialOutputExportPackage>.Fail", "10H package supply must return a safe failure.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.PackageNotTrusted", "10H package supply must reject untrusted package identity.");
        RequireContains(supplyBoundary, "AppOfficialOutputTextFileWorkflowReadinessRules.IsTrustedExportPackage(package)", "10H package supply must reuse the app trust rule.");
        RequireContains(supplyBoundary, "producer.ProduceAsync(producerRequest.Value, cancellationToken)", "RMO-M1-B2 package supply must call the trusted package producer with a selected or stored producer request.");
        RequireContains(supplyBoundary, "sourceSelectionBoundary.SelectSource(selectedSourceRequest)", "RMO-M1-B2 package supply must convert selected source requests through source-selection boundary.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.AsyncProductionRequired", "RMO-M1-B2 sync selected-source package retrieval must fail cleanly when async production is required.");
        RequireContains(supplyBoundary, "PackageReference", "10H summary must expose package reference.");
        RequireContains(supplyBoundary, "PackageHash", "10H summary must expose package hash.");
        RequireContains(supplyBoundary, "PackageKind", "10H summary must expose package kind.");
        RequireContains(supplyBoundary, "EntryCount", "10H summary must expose entry count.");
        RequireContains(supplyBoundary, "PayloadCount", "10H summary must expose payload count.");
        RequireContains(supplyBoundary, "TotalLineCount", "10H summary must expose total line count.");

        RequireDoesNotContain(supplyBoundary, "OfficialOutputExportPackageBuilder", "10H package supply must not build official output packages.");
        RequireDoesNotContain(supplyBoundary, "FromReceiptInvoicePreview", "10H package supply must not generate receipt/invoice payloads.");
        RequireDoesNotContain(supplyBoundary, "FromReadingReportPreview", "10H package supply must not generate reading report payloads.");
        RequireDoesNotContain(supplyBoundary, "FromAuditEvidenceReportPreview", "10H package supply must not generate audit evidence payloads.");
        RequireDoesNotContain(supplyBoundary, "Build(", "10H package supply must not build packages from UI state.");
        RequireDoesNotContain(supplyBoundary, "RunAsync", "10H package supply must not execute output.");
        RequireDoesNotContain(supplyBoundary, "DispatchAsync", "10H package supply must not dispatch output.");

        RequireContains(uiBoundary, "IOfficialOutputTextFilePackageSupplyBoundary? _packageSupplyBoundary", "10H UI shell must depend on the package supply boundary.");
        RequireContains(uiBoundary, "TryGetSuppliedPackage", "10H UI shell must check package supply.");
        RequireContains(uiBoundary, "packageSupplyBoundary.TryGetPackage(selectedSourceRequest)", "RMO-M1-B3 UI shell must retrieve package status through selected source request when present.");
        RequireContains(uiBoundary, "packageSupplyBoundary.TryGetPackageAsync(selectedSourceRequest, cancellationToken)", "RMO-M1-B3 UI shell must retrieve produced packages through selected source request when present.");
        RequireContains(uiBoundary, "OfficialOutputTextFilePackageSupply.BoundaryUnavailable", "10H UI shell must fail safely when supply boundary is unavailable.");
        RequireContains(uiBoundary, "packageResult.IsSuccess ? packageResult.Value : null", "10H UI readiness must use supplied package only when trusted.");
        RequireContains(uiBoundary, "packageResult.Value", "10H UI execution request must pass the supplied trusted package.");

        int packageSupplyIndex = uiBoundary.IndexOf("Result<OfficialOutputExportPackage> packageResult =", StringComparison.Ordinal);
        int statusIndex = uiBoundary.IndexOf("OfficialOutputTextFileWorkflowUiStatus status = GetStatus(request, packageResult);", StringComparison.Ordinal);
        int runIndex = uiBoundary.IndexOf("workflowGateway.RunAsync(appRequest, cancellationToken)", StringComparison.Ordinal);

        if (packageSupplyIndex < 0 ||
            statusIndex < 0 ||
            runIndex < 0 ||
            !(packageSupplyIndex < statusIndex && statusIndex < runIndex))
        {
            throw new InvalidOperationException("10H UI shell must check package supply before any workflow gateway execution.");
        }

        RequireDoesNotContain(uiBoundary, "request?.ExportPackage", "10H UI trigger request must not take package data directly from UI state.");
        RequireContains(appServices, "public IOfficialOutputTextFilePackageSupplyBoundary OfficialOutputTextFilePackageSupply { get; }", "10H AppServices must expose the package supply boundary.");
        RequireContains(appServices, "public IOfficialOutputTextFileTrustedPackageProducer OfficialOutputTextFileTrustedPackageProducer { get; }", "RMO-M1-A AppServices must expose the trusted package producer.");
        RequireContains(appServices, "new OfficialOutputTextFileTrustedPackageProducer(", "RMO-M1-A AppServices must wire trusted package producer.");
        RequireContains(appServices, "OfficialOutputSourceRead", "RMO-M1-A AppServices must wire producer to official output source.");
        RequireContains(appServices, "OfficialEngineeringEvidencePackageAssembler", "PRELAN-HARDEN-2 AppServices must wire engineering evidence assembler.");
        RequireContains(appServices, "trustedPackageProducer: OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A AppServices package supply must be producer-connected.");
        RequireContains(appServices, "OfficialOutputTextFilePackageSupply,", "10H AppServices must wire package supply into the UI shell.");

        RequireContains(appContracts, "IAppOfficialOutputTextFileWorkflowGateway", "10H must keep the app workflow gateway contract.");
        RequireContains(readinessContracts, "IAppOfficialOutputTextFileWorkflowReadinessGateway", "10H must keep readiness contract intact.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFilePackageSupply", "10H must not couple package supply to checkout presenter.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFilePackageSupply", "10H must not couple package supply to checkout product search.");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10H must not add a Host.Local Infrastructure reference.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10H must not add a Host.Local App.WinForms reference.");

        foreach (string text in new[] { supplyBoundary, uiBoundary })
        {
            foreach (string marker in RawOutputMarkers())
            {
                RequireDoesNotContain(text, marker, "10H package supply and UI shell must not expose raw output payload data: " + marker);
            }

            foreach (string forbidden in ForbiddenMarkers())
            {
                RequireDoesNotContain(text, forbidden, "10H package supply and UI shell source must not contain forbidden marker: " + forbidden);
            }
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFilePackageSupplyBoundaryChecks.Run();", "CoreSmoke must run 10H package supply checks.");
        RequireDoesNotContain(coreSmoke, "TryRunAsync(", "CoreSmoke must not execute official output text-file workflow UI commands.");
    }

    private static IEnumerable<string> RawOutputMarkers()
    {
        yield return "Text" + "Result";
        yield return "Payload" + "Text";
        yield return "Written" + "File";
        yield return "Written" + "Artifact";
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "System.Windows." + "Forms";
        yield return "Microsoft.Data." + "Sql" + "ite";
        yield return "Sql" + "ite";
        yield return "SQL" + "ite";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "Checkout" + "Presenter";
        yield return "Official" + "Checkout";
        yield return "Electronic" + "Journal" + "Append";
        yield return "Official" + "Document" + "Lifecycle";
        yield return "Official" + "ZReading" + "Service";
        yield return "Finalize" + "Async";
        yield return "Save" + "Async";
        yield return "IN" + "SERT";
        yield return "UP" + "DATE";
        yield return "DE" + "LETE";
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

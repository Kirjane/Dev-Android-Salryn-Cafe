namespace Salryn.Tests;

public static class J22OfficialOutputTextFileSourceSelectionPackageSupplyBridgeChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string sourceSelection = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileSourceSelectionBoundary.cs");
        string supplyBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string uiBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellForm = Read(root, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelectionBoundary", "RMO-M1-B2/B3 must keep the neutral source-selection boundary.");
        RequireContains(sourceSelection, "OfficialOutputSourceReadService.ValidateRequest(request.SourceReadRequest)", "RMO-M1-B2/B3 source selection must continue to use official source identity validation.");

        RequireContains(supplyBoundary, "IOfficialOutputTextFileSourceSelectionBoundary? sourceSelectionBoundary = null", "RMO-M1-B2 package supply must accept a source-selection boundary.");
        RequireContains(supplyBoundary, "OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest", "RMO-M1-B2 package supply must accept selected source requests.");
        RequireContains(supplyBoundary, "sourceSelectionBoundary.SelectSource(selectedSourceRequest)", "RMO-M1-B2 package supply must convert selected sources through the source-selection boundary.");
        RequireContains(supplyBoundary, "selectedSource.ProducerRequest", "RMO-M1-B2 package supply must use the producer request returned by source selection.");
        RequireContains(supplyBoundary, "producer.ProduceAsync(producerRequest.Value, cancellationToken)", "RMO-M1-B2 async package production must receive the selected or stored producer request.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.AsyncProductionRequired", "RMO-M1-B2 sync selected-source retrieval must fail cleanly when async production is required.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.ProducerRequestRequired", "RMO-M1-B2 default producer-backed supply must remain unavailable without selected source.");
        RequireContains(supplyBoundary, "FromTrustedPackage(", "RMO-M1-B2 existing trusted package path must remain.");
        RequireContains(supplyBoundary, "FromTrustedPackageProducer(", "RMO-M1-B2 existing producer-request path must remain.");
        RequireContains(supplyBoundary, "_producerRequest", "RMO-M1-B2 existing stored producer request path must remain.");
        RequireContains(supplyBoundary, "_trustedPackage", "RMO-M1-B2 existing trusted package path must remain.");

        RequireContains(uiBoundary, "OfficialOutputTextFileSourceSelectionRequest? SourceSelectionRequest = null", "RMO-M1-B3 UI trigger request must carry optional selected source request.");
        RequireContains(uiBoundary, "TryGetSuppliedPackage(request?.SourceSelectionRequest)", "RMO-M1-B3 UI status must pass selected source request to package supply.");
        RequireContains(uiBoundary, "TryGetSuppliedPackageAsync(request?.SourceSelectionRequest, cancellationToken)", "RMO-M1-B3 UI run must pass selected source request to async package supply.");
        RequireContains(uiBoundary, "packageSupplyBoundary.TryGetPackage(selectedSourceRequest)", "RMO-M1-B3 UI sync package check must pass selected source request.");
        RequireContains(uiBoundary, "packageSupplyBoundary.TryGetPackageAsync(selectedSourceRequest, cancellationToken)", "RMO-M1-B3 UI async package retrieval must pass selected source request.");

        RequireContains(appServices, "public IOfficialOutputTextFileSourceSelectionBoundary OfficialOutputTextFileSourceSelection { get; }", "RMO-M1-B2/B3 AppServices must expose the source-selection boundary.");
        RequireContains(appServices, "new OfficialOutputTextFileSourceSelectionBoundary()", "RMO-M1-B2/B3 AppServices must construct the source-selection boundary.");
        RequireContains(appServices, "sourceSelectionBoundary: OfficialOutputTextFileSourceSelection", "RMO-M1-B2/B3 AppServices must wire source selection into package supply.");
        RequireContains(appServices, "trustedPackageProducer: OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-B2/B3 default AppServices package supply must remain producer-backed.");
        RequireDoesNotContain(appServices, "producerRequest:", "RMO-M1-B2/B3 default AppServices must remain unavailable without selected source.");

        RequireDoesNotContain(shellForm, "OfficialOutputTextFileSourceSelection", "RMO-M1-B2/B3 must not add visible shell UI wiring.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileSourceSelection", "RMO-M1-B2/B3 must not wire checkout presenter to official output source selection.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFileSourceSelection", "RMO-M1-B2/B3 must not wire checkout product search to official output source selection.");

        foreach (string text in new[] { supplyBoundary, uiBoundary, sourceSelection })
        {
            foreach (string forbidden in ForbiddenMarkers())
            {
                RequireDoesNotContain(text, forbidden, "RMO-M1-B2/B3 official output source selection bridge must not contain forbidden marker: " + forbidden);
            }
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFileSourceSelectionPackageSupplyBridgeChecks.Run();", "CoreSmoke must run RMO-M1-B2/B3 source-selection package supply bridge checks.");
        RequireDoesNotContain(coreSmoke, "TryRunAsync(", "CoreSmoke must not execute official output text-file workflow UI commands.");
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

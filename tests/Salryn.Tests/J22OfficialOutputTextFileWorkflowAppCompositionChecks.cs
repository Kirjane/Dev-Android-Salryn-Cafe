namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowAppCompositionChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string contracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowContracts.cs");
        string adapter = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string services = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string localHostContracts = Read(root, "src", "Salryn.Host.Local", "Contracts", "LocalHostOperationContractModels.cs");

        RequireContains(adapter, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10A gateway adapter must exist.");
        RequireContains(adapter, "OfficialExternalOutputTextFileWorkflowRunner", "10A gateway must reuse the existing controlled text-file workflow runner.");
        RequireContains(adapter, "AppOfficialOutputTextFileWorkflowRules.ValidateRequest", "10A gateway must validate requests before execution.");
        RequireContains(adapter, "OfficialExternalOutputWorkflowRequest", "10A gateway must adapt to existing 09S workflow request.");
        RequireContains(adapter, "AppOfficialOutputTextFileWorkflowResult.Success", "10A gateway must map successful workflow results.");
        RequireContains(adapter, "AppOfficialOutputTextFileWorkflowResult.Failure", "10A gateway must map workflow failures.");

        RequireContains(services, "using Salryn.Infrastructure.OfficialOutput;", "10A app composition must import official output infrastructure explicitly.");
        RequireContains(services, "IAppOfficialOutputTextFileWorkflowGateway OfficialOutputTextFileWorkflow", "10A AppServices must expose the app gateway property.");
        RequireContains(services, "new CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10A AppServices must wire the app gateway adapter.");
        RequireContains(services, "new OfficialExternalOutputTextFileWorkflowRunner", "10A AppServices must wire the existing controlled workflow runner.");

        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileWorkflow", "10A must not wire controlled output workflow into checkout presenter or UI behavior.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFileWorkflow", "10A must not wire controlled output workflow into checkout product search behavior.");
        RequireContains(localHostContracts, "HostOperationNames.OfficialOutputTextFileWorkflow", "10B must add the Host.Local official output text-file workflow operation contract.");
        RequireContains(localHostContracts, "LocalHostOfficialOutputTextFileWorkflowRequest", "10B must add the Host.Local official output text-file workflow request contract.");
        RequireDoesNotContain(localHostContracts, "OfficialOutputWorkflow", "10A must not add generic Host.Local workflow naming.");

        foreach (string text in new[] { contracts, adapter })
        {
            foreach (string forbidden in new[]
            {
                "CrystalDecisions", ".rpt", "Microsoft.Data.Sqlite", "Sqlite", "SQLite",
                "Process.Start", "ESC/POS", "PrintDocument", "PrinterSettings", "System.Drawing.Printing",
                "PdfDocument", "ExportToDisk", "CompleteSale", "OfficialCheckout", "ElectronicJournalAppend",
                "OfficialZReadingService", "FinalizeAsync", "SaveAsync", "INSERT ", "UPDATE ", "DELETE "
            })
            {
                RequireDoesNotContain(text, forbidden, "10A app exposure file must not contain forbidden marker: " + forbidden);
            }
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
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
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }
}

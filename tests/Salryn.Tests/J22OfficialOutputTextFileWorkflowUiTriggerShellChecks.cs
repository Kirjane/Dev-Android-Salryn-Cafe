namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowUiTriggerShellChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string boundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string supplyBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string appContracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowContracts.cs");
        string readinessContracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowReadinessContracts.cs");
        string localHostGateway = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string directGateway = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string shellForm = Read(root, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(boundary, "namespace Salryn.App.WinForms.Features.OfficialOutput", "10G UI trigger shell must live in the OfficialOutput feature namespace.");
        RequireContains(boundary, "OfficialOutputTextFileWorkflowUiTriggerRequest", "10G UI trigger request must exist.");
        RequireContains(boundary, "OfficialOutputTextFileSourceSelectionRequest? SourceSelectionRequest = null", "RMO-M1-B3 UI trigger request must carry optional selected official output source request.");
        RequireContains(boundary, "OfficialOutputTextFileWorkflowUiStatus", "10G UI status contract must exist.");
        RequireContains(boundary, "OfficialOutputTextFileWorkflowUiCommandResult", "10G UI command result must exist.");
        RequireContains(boundary, "IOfficialOutputTextFileWorkflowUiBoundary", "10G UI boundary interface must exist.");
        RequireContains(boundary, "public sealed class OfficialOutputTextFileWorkflowUiBoundary", "10G UI boundary implementation must exist.");
        RequireContains(boundary, "GetStatus", "10G UI boundary must expose readiness/status.");
        RequireContains(boundary, "TryRunAsync", "10G UI boundary must expose a guarded command method.");
        RequireContains(boundary, "IAppOfficialOutputTextFileWorkflowReadinessGateway", "10G UI boundary must depend on the 10F readiness gateway.");
        RequireContains(boundary, "IAppOfficialOutputTextFileWorkflowGateway", "10G UI boundary must depend on the app workflow gateway contract.");
        RequireContains(boundary, "IOfficialOutputTextFilePackageSupplyBoundary", "10G/10H UI boundary must depend on the package supply boundary.");
        RequireContains(boundary, "new AppOfficialOutputTextFileWorkflowReadinessRequest", "10G UI boundary must map to the readiness request.");
        RequireContains(boundary, "TryGetSuppliedPackage", "10G/10H UI boundary must obtain a trusted package from the supply boundary.");
        RequireContains(boundary, "request?.SourceSelectionRequest", "RMO-M1-B3 UI boundary must pass selected source request from trigger request.");
        RequireContains(boundary, "packageResult.IsSuccess ? packageResult.Value : null", "10G/10H readiness must use the supplied trusted package when available.");
        RequireContains(boundary, "request?.OutputRootPath", "10G UI boundary must require an explicit output root.");
        RequireContains(boundary, "AppOfficialOutputTextFileWorkflowRules.ValidateRequest", "10G UI command must validate before execution.");
        RequireContains(boundary, "if (!status.IsAvailable)", "10G UI command must block execution when readiness is unavailable.");
        RequireContains(boundary, "AttemptedExecution: false", "10G blocked command result must prove no workflow attempt happened.");
        RequireContains(boundary, "workflowGateway.RunAsync(appRequest, cancellationToken)", "10G UI command may call the workflow only after readiness and request validation.");
        RequireContains(boundary, "OfficialOutputTextFileWorkflowUi.ReadinessUnavailable", "10G UI blocked status must be explicit.");

        int packageSupplyIndex = boundary.IndexOf("Result<OfficialOutputExportPackage> packageResult =", StringComparison.Ordinal);
        int statusCheckIndex = boundary.IndexOf("OfficialOutputTextFileWorkflowUiStatus status = GetStatus(request, packageResult);", StringComparison.Ordinal);
        int blockedIndex = boundary.IndexOf("if (!status.IsAvailable)", StringComparison.Ordinal);
        int validationIndex = boundary.IndexOf("AppOfficialOutputTextFileWorkflowRules.ValidateRequest(appRequest)", StringComparison.Ordinal);
        int runIndex = boundary.IndexOf("workflowGateway.RunAsync(appRequest, cancellationToken)", StringComparison.Ordinal);

        if (packageSupplyIndex < 0 ||
            statusCheckIndex < 0 ||
            blockedIndex < 0 ||
            validationIndex < 0 ||
            runIndex < 0 ||
            !(packageSupplyIndex < statusCheckIndex && statusCheckIndex < blockedIndex && blockedIndex < validationIndex && validationIndex < runIndex))
        {
            throw new InvalidOperationException("10G UI command must check package supply and readiness, block unavailable state, validate, then call the workflow gateway.");
        }

        RequireContains(appServices, "public IOfficialOutputTextFileWorkflowUiBoundary OfficialOutputTextFileWorkflowUi { get; }", "10G AppServices must expose the guarded UI trigger boundary.");
        RequireContains(appServices, "new OfficialOutputTextFileWorkflowUiBoundary(", "10G AppServices must construct the guarded UI trigger boundary.");
        RequireContains(appServices, "OfficialOutputTextFileWorkflowReadiness,", "10G UI trigger must use the readiness surface.");
        RequireContains(appServices, "LocalHostOfficialOutputTextFileWorkflow,", "10G UI trigger must use the Local Host app gateway for guarded command execution.");
        RequireContains(appServices, "OfficialOutputTextFilePackageSupply);", "10G/10H UI trigger must use the package supply boundary.");
        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway OfficialOutputTextFileWorkflow { get; }", "10G must keep the direct 10A app gateway intact.");
        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway LocalHostOfficialOutputTextFileWorkflow { get; }", "10G must keep the Local Host 10D app gateway intact.");
        RequireContains(supplyBoundary, "public interface IOfficialOutputTextFilePackageSupplyBoundary", "10H package supply boundary must exist.");
        RequireContains(supplyBoundary, "TryGetPackage(OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest)", "RMO-M1-B3 supply boundary must expose selected-source sync status path.");
        RequireContains(supplyBoundary, "TryGetPackageAsync(OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest", "RMO-M1-B3 supply boundary must expose selected-source async package path.");

        RequireContains(appContracts, "IAppOfficialOutputTextFileWorkflowGateway", "10G must keep the app workflow gateway contract.");
        RequireContains(readinessContracts, "IAppOfficialOutputTextFileWorkflowReadinessGateway", "10G must keep the readiness gateway contract.");
        RequireContains(localHostGateway, "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter", "10G must keep the Local Host gateway bridge.");
        RequireContains(directGateway, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10G must keep the direct gateway bridge.");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10G must not add a Host.Local Infrastructure reference.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10G must not add a Host.Local App.WinForms reference.");

        RequireDoesNotContain(shellForm, "OfficialOutputTextFileWorkflowUi", "10G must defer visible shell UI control wiring.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileWorkflow", "10G must not couple the trigger shell to checkout presenter behavior.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFileWorkflow", "10G must not couple the trigger shell to checkout product search.");

        foreach (string marker in RawOutputMarkers())
        {
            RequireDoesNotContain(boundary, marker, "10G UI shell must not expose raw output payload data: " + marker);
        }

        foreach (string forbidden in ForbiddenMarkers())
        {
            RequireDoesNotContain(boundary, forbidden, "10G UI shell source must not contain forbidden marker: " + forbidden);
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFileWorkflowUiTriggerShellChecks.Run();", "CoreSmoke must run 10G UI trigger shell checks.");
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

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowReadinessChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string contracts = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowReadinessContracts.cs");
        string gateway = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowReadinessGateway.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string directGateway = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string localHostGateway = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string seal = Read(root, "00-HANDOFF", "J22_OFFICIAL_OUTPUT_TEXT_FILE_LOCAL_HOST_WORKFLOW_SEAL.md");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contracts, "AppOfficialOutputTextFileWorkflowReadinessRequest", "10F readiness request contract must exist.");
        RequireContains(contracts, "OfficialOutputExportPackage? ExportPackage", "10F readiness must accept an already trusted export package.");
        RequireContains(contracts, "string? OutputRootPath", "10F readiness must require explicit output root input.");
        RequireContains(contracts, "AppOfficialOutputTextFileWorkflowReadinessIssue", "10F readiness issue contract must exist.");
        RequireContains(contracts, "AppOfficialOutputTextFileWorkflowReadinessState", "10F readiness state contract must exist.");
        RequireContains(contracts, "DirectGatewayAvailable", "10F readiness must report direct gateway availability.");
        RequireContains(contracts, "LocalHostGatewayAvailable", "10F readiness must report Local Host gateway availability.");
        RequireContains(contracts, "LocalHostRuntimeHolderAvailable", "10F readiness must report Local Host runtime holder availability.");
        RequireContains(contracts, "LocalHostRuntimeStartupSucceeded", "10F readiness must report Local Host runtime startup state.");
        RequireContains(contracts, "RequestContextProviderAvailable", "10F readiness must report request context provider availability.");
        RequireContains(contracts, "SessionIdentityProviderAvailable", "10F readiness must report session identity provider availability.");
        RequireContains(contracts, "AuthenticatedAppIdentityAvailable", "10F readiness must report authenticated app identity availability.");
        RequireContains(contracts, "RequestContextCanBeCreated", "10F readiness must report request context creation capability.");
        RequireContains(contracts, "OutputRootSupplied", "10F readiness must report explicit output root readiness.");
        RequireContains(contracts, "TrustedExportPackageSupplied", "10F readiness must report trusted package readiness.");
        RequireContains(contracts, "TextFileOnly", "10F readiness must keep the official workflow text-file-only.");
        RequireContains(contracts, "IAppOfficialOutputTextFileWorkflowReadinessGateway", "10F readiness gateway interface must exist.");
        RequireContains(contracts, "CheckReadiness", "10F readiness gateway must expose CheckReadiness.");
        RequireContains(gateway, "TrustedExportPackageRequired", "10F readiness must require trusted package status.");
        RequireContains(gateway, "OutputRootRequired", "10F readiness must require output root status.");

        RequireContains(gateway, "public sealed class CompositionOfficialOutputTextFileWorkflowReadinessGateway", "10F composition readiness gateway must exist.");
        RequireContains(gateway, "IAppOfficialOutputTextFileWorkflowReadinessGateway", "10F readiness gateway must implement the app readiness contract.");
        RequireContains(gateway, "CheckReadiness", "10F readiness must be checked without executing output.");
        RequireContains(gateway, "AppOfficialOutputTextFileWorkflowReadinessResult.Available(state)", "10F readiness must report available state.");
        RequireContains(gateway, "AppOfficialOutputTextFileWorkflowReadinessResult.Unavailable(state, issues.ToArray())", "10F readiness must report unavailable state.");
        RequireContains(gateway, "AuthenticatedIdentityUnavailable", "10F readiness must report missing app identity clearly.");
        RequireContains(gateway, "RequestContextUnavailable", "10F readiness must report context creation failure clearly.");
        RequireContains(gateway, "LocalHostRuntimeStartupUnavailable", "10F readiness must report Local Host startup failure clearly.");
        RequireContains(gateway, "TextFileOnly: true", "10F readiness must pin text-file-only status.");

        RequireDoesNotContain(gateway, ".RunAsync(", "10F readiness must not execute the direct or Local Host workflow.");
        RequireDoesNotContain(gateway, "DispatchAsync", "10F readiness must not dispatch through Local Host.");
        RequireDoesNotContain(gateway, "File.Write", "10F readiness must not write files.");
        RequireDoesNotContain(gateway, "FileStream", "10F readiness must not open file streams.");
        RequireDoesNotContain(gateway, "StreamWriter", "10F readiness must not write output streams.");
        RequireDoesNotContain(gateway, "Process.Start", "10F readiness must not launch processes.");
        RequireDoesNotContain(gateway, "ThrowIfNull", "10F readiness unavailable cases must not throw through constructor guards.");
        RequireDoesNotContain(gateway, "throw ", "10F readiness unavailable cases must be non-throwing.");

        foreach (string text in new[] { contracts, gateway })
        {
            foreach (string rawOutputMarker in RawOutputMarkers())
            {
                RequireDoesNotContain(text, rawOutputMarker, "10F readiness must not expose raw output payload data: " + rawOutputMarker);
            }

            foreach (string forbidden in ForbiddenMarkers())
            {
                RequireDoesNotContain(text, forbidden, "10F readiness source must not contain forbidden marker: " + forbidden);
            }
        }

        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowReadinessGateway OfficialOutputTextFileWorkflowReadiness { get; }", "10F AppServices must expose the readiness gateway.");
        RequireContains(appServices, "new CompositionOfficialOutputTextFileWorkflowReadinessGateway(", "10F AppServices must construct the readiness gateway.");
        RequireContains(appServices, "OfficialOutputTextFileWorkflow,", "10F readiness must observe the direct 10A gateway.");
        RequireContains(appServices, "LocalHostOfficialOutputTextFileWorkflow,", "10F readiness must observe the Local Host 10D gateway.");
        RequireContains(appServices, "LocalHostRuntimeStartupResult,", "10F readiness must observe Local Host startup status.");

        RequireContains(directGateway, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "Direct 10A gateway must remain intact.");
        RequireContains(localHostGateway, "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter", "Local Host 10D gateway must remain intact.");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10F must not add a Host.Local Infrastructure reference.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10F must not add a Host.Local App.WinForms reference.");

        RequireContains(seal, "parallel app-facing paths", "10F must clarify direct and Local Host gateways are parallel app-facing paths.");
        RequireContains(seal, "Direct 10A path:", "10F seal wording must preserve the direct app path.");
        RequireContains(seal, "Local Host 10D path:", "10F seal wording must preserve the Local Host app path.");

        RequireContains(coreSmoke, "J22OfficialOutputTextFileWorkflowReadinessChecks.Run();", "CoreSmoke must run 10F readiness checks.");
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
        yield return "Microsoft.Data." + "Sqlite";
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

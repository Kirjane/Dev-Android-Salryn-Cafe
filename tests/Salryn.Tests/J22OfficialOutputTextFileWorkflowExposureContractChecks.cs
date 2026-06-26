namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowExposureContractChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string contractText = Read(root, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowContracts.cs");
        string adapterText = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");

        RequireContains(contractText, "AppOfficialOutputTextFileWorkflowRequest", "10A request contract must exist.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflowResult", "10A result contract must exist.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflowReferenceSummary", "10A reference summary must exist.");
        RequireContains(contractText, "IAppOfficialOutputTextFileWorkflowGateway", "10A app gateway interface must exist.");
        RequireContains(contractText, "OfficialOutputExportPackage? ExportPackage", "10A must accept a trusted official output export package.");
        RequireContains(contractText, "string? OutputRootPath", "10A must require explicit runtime output root path input.");
        RequireContains(contractText, "DateTimeOffset? RequestedAt", "10A must require explicit requested timestamp input.");
        RequireContains(contractText, "Guid? RequestedByUserId", "10A must require explicit requested user input.");
        RequireContains(contractText, "string? TerminalId", "10A must require explicit terminal input.");
        RequireContains(contractText, "IReadOnlyDictionary<string, string>? Metadata", "10A must require explicit metadata input.");

        foreach (string field in new[]
        {
            "PackageReference", "PackageHash", "DeliveryReference", "DeliveryHash",
            "DeliveryLedgerReference", "DeliveryLedgerHash", "DispatchPlanReference", "DispatchPlanHash",
            "ExecutionReference", "ExecutionHash", "ExecutionLedgerReference", "ExecutionLedgerHash",
            "VerificationReference", "VerificationHash", "WorkflowReference", "WorkflowHash"
        })
        {
            RequireContains(contractText, field, "10A result must expose and map reference/hash field: " + field);
        }

        RequireContains(contractText, "Verified", "10A result must expose verification status.");
        RequireContains(contractText, "StatusCode", "10A result must expose status code.");
        RequireContains(contractText, "ErrorCode", "10A result must expose error code.");
        RequireContains(contractText, "ErrorMessage", "10A result must expose error message.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.OutputRootRequired", "10A contract must reject blank output root.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.PackageRequired", "10A contract must reject missing package.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.RequestedAtRequired", "10A contract must reject missing timestamp.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.RequestedByUserIdRequired", "10A contract must reject missing user id.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.TerminalIdRequired", "10A contract must reject missing terminal id.");
        RequireContains(contractText, "AppOfficialOutputTextFileWorkflow.MetadataRequired", "10A contract must reject missing metadata envelope.");
        RequireContains(adapterText, "OfficialExternalOutputTextFileWorkflowRunner", "10A adapter must call the controlled text-file workflow runner.");
        RequireContains(adapterText, "AppOfficialOutputTextFileWorkflowResult.Success", "10A adapter must map successful runner output through the app result contract.");

        RequireDoesNotContain(contractText, "TextResult", "10A app result must not expose raw workflow text.");
        RequireDoesNotContain(contractText, "WrittenArtifacts", "10A app result must not expose file payload internals.");
        RequireDoesNotContain(contractText, "OfficialOutputWorkflow", "10A must use explicit text-file naming, not generic workflow naming.");
        RequireContains(contractText, "OfficialOutputTextFileWorkflow", "10A must use explicit controlled text-file naming.");
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

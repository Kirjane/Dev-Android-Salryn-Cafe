namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowLocalHostSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J22_OFFICIAL_OUTPUT_TEXT_FILE_LOCAL_HOST_WORKFLOW_SEAL.md");
        string matrixText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_COMPLIANCE_MATRIX.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string appContracts = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppOfficialOutputTextFileWorkflowContracts.cs");
        string directGateway = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string localHostContracts = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Contracts", "LocalHostOfficialOutputTextFileWorkflowContracts.cs");
        string corePort = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputTextFileWorkflowExecutionPort.cs");
        string hostHandler = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Handlers", "LocalHostOfficialOutputTextFileWorkflowHandler.cs");
        string appBridge = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string appServices = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string hostProject = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string matrixSealSection = ExtractSection(
            matrixText,
            "## J22-10E Official Output Text-File Local Host Workflow Documentation Seal",
            "## First-Try Best-Practice Guardrails");

        AssertDocumentation(handoffText, "handoff seal");
        AssertDocumentation(matrixSealSection, "compliance matrix 10E section");

        RequireContains(handoffText, "Status: documentation seal only.", "10E handoff must be documentation-only.");
        RequireContains(handoffText, "No behavior is added in this phase.", "10E handoff must block behavior changes.");
        RequireContains(handoffText, "The direct 10A official output text-file workflow gateway remains intact.", "10E handoff must preserve direct 10A gateway.");
        RequireContains(handoffText, "Host.Local remains Core-only.", "10E handoff must document Host.Local dependency boundary.");
        RequireContains(handoffText, "The App.WinForms Local Host bridge exposes references, hashes, status, and verification fields only.", "10E handoff must document safe response shape.");
        RequireContains(handoffText, "parallel app-facing paths", "10F wording correction must clarify direct and Local Host gateways are parallel paths.");
        RequireContains(handoffText, "Direct 10A path:", "10F wording correction must keep direct path separate.");
        RequireContains(handoffText, "Local Host 10D path:", "10F wording correction must keep Local Host path separate.");

        RequireContains(matrixText, "## J22-10E Official Output Text-File Local Host Workflow Documentation Seal", "10E matrix section must exist.");
        RequireContains(matrixSealSection, "10A app-facing official output text-file workflow exposure exists.", "Matrix must mention 10A app exposure.");
        RequireContains(matrixSealSection, "10B Local Host official output text-file workflow contract surface exists.", "Matrix must mention 10B Local Host contract.");
        RequireContains(matrixSealSection, "10C-A Core official output text-file workflow execution port exists.", "Matrix must mention 10C-A Core port.");
        RequireContains(matrixSealSection, "10C-B real Host.Local official output text-file workflow handler exists and depends only on the Core port.", "Matrix must mention 10C-B handler.");
        RequireContains(matrixSealSection, "10D App.WinForms Local Host official output text-file workflow bridge and composition wiring exists.", "Matrix must mention 10D app bridge.");

        RequireContains(appContracts, "IAppOfficialOutputTextFileWorkflowGateway", "10E must seal the 10A app-facing gateway contract.");
        RequireContains(directGateway, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10E must seal the direct 10A gateway.");
        RequireContains(localHostContracts, "LocalHostOfficialOutputTextFileWorkflowRequest", "10E must seal the 10B Local Host request contract.");
        RequireContains(corePort, "IOfficialOutputTextFileWorkflowExecutionPort", "10E must seal the 10C-A Core execution port.");
        RequireContains(hostHandler, "LocalHostOfficialOutputTextFileWorkflowHandler", "10E must seal the 10C-B Host.Local handler.");
        RequireContains(appBridge, "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter", "10E must seal the 10D App.WinForms bridge.");
        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway LocalHostOfficialOutputTextFileWorkflow { get; }", "10E must seal the 10D passive app service exposure.");
        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway OfficialOutputTextFileWorkflow { get; }", "10E must seal the direct 10A app service exposure.");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must keep its Core reference.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10E must not add a Host.Local Infrastructure reference.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10E must not add a Host.Local App.WinForms reference.");

        RequireDoesNotContain(hostHandler, "OfficialExternalOutputTextFileWorkflowRunner", "10E must preserve handler dependency on Core port only.");
        RequireDoesNotContain(hostHandler, "SalrynAppServices", "10E must preserve Host.Local isolation from App.WinForms services.");

        RequireContains(coreSmokeText, "J22OfficialOutputTextFileWorkflowLocalHostSealChecks.Run();", "CoreSmoke must run the 10E seal guard.");
    }

    private static void AssertDocumentation(string text, string label)
    {
        RequireContains(text, "10A app-facing official output text-file workflow exposure exists.", label + " must mention 10A app exposure.");
        RequireContains(text, "10B Local Host official output text-file workflow contract surface exists.", label + " must mention 10B Local Host contract.");
        RequireContains(text, "10C-A Core official output text-file workflow execution port exists.", label + " must mention 10C-A Core port.");
        RequireContains(text, "10C-B real Host.Local official output text-file workflow handler exists", label + " must mention 10C-B handler.");
        RequireContains(text, "10D App.WinForms Local Host official output text-file workflow bridge", label + " must mention 10D app bridge.");
        RequireContains(text, "No UI trigger.", label + " must state no UI trigger.");
        RequireContains(text, "No checkout", label + " must state no checkout mutation or behavior change.");
        RequireContains(text, "No journal append mutation.", label + " must state no journal append mutation.");
        RequireContains(text, "No lifecycle mutation.", label + " must state no lifecycle mutation.");
        RequireContains(text, "No PDF", label + " must state no PDF output.");
        RequireContains(text, "No Crystal Reports", label + " must state no Crystal Reports output.");
        RequireContains(text, "No printing.", label + " must state no printing.");
        RequireContains(text, "No ESC/POS output.", label + " must state no ESC/POS output.");
        RequireContains(text, "Salryn is not BIR-certified.", label + " must state Salryn is not BIR-certified.");
        RequireContains(text, "does not make Salryn RMO 24-2023 complete.", label + " must not claim complete RMO compliance.");

        foreach (string forbidden in ForbiddenClaims())
        {
            RequireDoesNotContain(text, forbidden, label + " must not overclaim: " + forbidden);
        }
    }

    private static IEnumerable<string> ForbiddenClaims()
    {
        yield return "Salryn is BIR-certified";
        yield return "BIR certification complete";
        yield return "RMO certification complete";
        yield return "RMO compliance complete";
        yield return "full RMO compliance achieved";
        yield return "accreditation-ready";
    }

    private static string ExtractSection(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException("Required section start marker was not found: " + startMarker);
        }

        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        if (end < 0)
        {
            throw new InvalidOperationException("Required section end marker was not found: " + endMarker);
        }

        return text[start..end];
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

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

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

namespace Salryn.Tests;

public static class RmoA7InternalOfficialOutputTextFileWorkflowSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string sealText = Read(repoRoot, "00-COMPLIANCE", "RMO_A7_INTERNAL_OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_SEAL.md");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokeProgram = Read(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string permissions = Read(repoRoot, "src", "Salryn.Core", "Security", "SalrynPermissions.cs");
        string seedData = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Seeding", "DefaultSeedData.cs");
        string shellPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");
        string boundary = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string navigation = Read(repoRoot, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(repoRoot, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string auditContracts = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputTextFileWorkflowAuditContracts.cs");
        string pathPolicy = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileOutputPathPolicy.cs");
        string adapter = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs");

        RequireContains(coreSmoke, "RmoA7InternalOfficialOutputTextFileWorkflowSealChecks.Run();", "CoreSmoke must run RMO-A7 internal workflow seal checks.");

        RequireContains(sealText, "Status: documentation seal only.", "A7 seal must remain documentation-only.");
        RequireContains(sealText, "Native Official Output Text File workflow only.", "A7 seal must state native Official Output Text File scope.");
        RequireContains(sealText, "internally controlled native Official Output Text File workflow", "A7 seal must state internally controlled native workflow.");
        RequireContains(sealText, "RMO-A2", "A7 seal must reference RMO-A2.");
        RequireContains(sealText, "RMO-A3", "A7 seal must reference RMO-A3.");
        RequireContains(sealText, "RMO-A4", "A7 seal must reference RMO-A4.");
        RequireContains(sealText, "RMO-A5", "A7 seal must reference RMO-A5.");
        RequireContains(sealText, "RMO-A6", "A7 seal must reference RMO-A6.");
        RequireContains(sealText, "RMO-A6.1", "A7 seal must reference RMO-A6.1.");
        RequireContains(sealText, "OfficialOutput.Run", "A7 seal must state OfficialOutput.Run permission.");
        RequireContains(sealText, "audit_logs", "A7 seal must state generic audit_logs audit trail.");
        RequireContains(sealText, "Sanitized audit failure surface", "A7 seal must state sanitized audit failure surface.");
        RequireContains(sealText, "AuditRecordFailed", "A7 seal must reference AuditRecordFailed behavior.");
        RequireContains(sealText, "OutputPackageAlreadyExists", "A7 seal must reference duplicate package refusal.");
        RequireContains(sealText, "official-output-text-file", "A7 seal must reference official-output-text-file smoke command.");
        RequireContains(sealText, "Web/API official-output", "A7 seal must state Web/API official-output remains disabled.");
        RequireContains(sealText, "Text-file output only", "A7 seal must state text-file output only.");
        RequireContains(sealText, "not BIR certification", "A7 seal must deny BIR certification.");
        RequireContains(sealText, "not BIR accreditation", "A7 seal must deny BIR accreditation.");
        RequireContains(sealText, "not full RMO 24-2023 compliance", "A7 seal must deny full RMO compliance claim.");
        RequireContains(sealText, "Not stable until Codex verifies/promotes.", "A7 seal must include promotion warning.");

        RequireContains(permissions, "OfficialOutputRun = \"OfficialOutput.Run\"", "Source must still define OfficialOutput.Run permission.");
        RequireContains(seedData, "Permissions.OfficialOutputRun", "Seed data must still include OfficialOutput.Run for Admin.");
        RequireContains(shellPresenter, "SalrynPermissions.OfficialOutputRun", "Shell route must remain permission-gated.");
        RequireContains(boundary, "OfficialOutputWorkflowPermissions.CallerCanRunExportWorkflow", "UI boundary must remain permission-gated.");
        RequireContains(navigation, "Official Output Text File", "Web nav must still list Official Output Text File surface.");
        RequireContains(navigation, "enabled: false", "Web nav must keep official-output surfaces disabled.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");
        RequireContains(auditContracts, "OfficialOutputTextFileWorkflowAuditFailure", "Audit contracts must retain sanitized audit failure surface.");
        RequireContains(auditContracts, "OfficialOutputTextFileWorkflowUi.AuditRecordFailed", "Audit contracts must retain AuditRecordFailed code.");
        RequireContains(pathPolicy, "ValidateWritableOutputRoot", "Output-root policy must remain hardened.");
        RequireContains(pathPolicy, "RejectIfOutputPackageDirectoryExists", "Duplicate package policy must remain in place.");
        RequireContains(infraSmokeProgram, "case \"official-output-text-file\":", "InfrastructureSmoke must keep official-output-text-file command.");
        RequireContains(infraSmokeProgram, "OfficialTextFileExternalOutputSmokeChecks.Run(projectRoot);", "InfrastructureSmoke must keep official-output artifact smoke.");
        RequireContains(infraSmokeProgram, "OfficialOutputTextFileWorkflowUiBoundaryAuditSmokeChecks.Run();", "InfrastructureSmoke must keep boundary audit smoke.");

        foreach (string marker in ForbiddenProductionMarkers())
        {
            RequireDoesNotContain(adapter, marker, "A7 must not introduce forbidden adapter marker: " + marker);
        }
    }

    private static IEnumerable<string> ForbiddenProductionMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "Pdf" + "Document";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
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

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required file was not found: " + path);
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

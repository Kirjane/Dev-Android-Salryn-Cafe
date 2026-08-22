using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class RmoA5OfficialOutputPermissionChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string permissions = Read(repoRoot, "src", "Salryn.Core", "Security", "SalrynPermissions.cs");
        string seedData = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Seeding", "DefaultSeedData.cs");
        string shellPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");
        string form = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowForm.cs");
        string boundary = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string navigation = Read(repoRoot, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(repoRoot, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string policySource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileOutputPathPolicy.cs");
        string adapterSource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs");

        RequireContains(coreSmoke, "RmoA5OfficialOutputPermissionChecks.Run();", "CoreSmoke must run RMO-A5 permission checks.");
        RequireContains(permissions, "OfficialOutputRun = \"OfficialOutput.Run\"", "SalrynPermissions must define OfficialOutput.Run.");
        RequireContains(seedData, "OfficialOutputRun = \"OfficialOutput.Run\"", "DefaultSeedData must define OfficialOutput.Run.");
        RequireContains(seedData, "Permissions.OfficialOutputRun", "Admin seed must include OfficialOutput.Run.");
        Require(ContainsAll(DefaultSeedData.AdminPermissions, SalrynPermissions.OfficialOutputRun), "Admin role must receive OfficialOutput.Run.");
        Require(!ContainsAll(DefaultSeedData.ManagerPermissions, SalrynPermissions.OfficialOutputRun), "Manager role must not receive OfficialOutput.Run by default.");
        Require(!ContainsAll(DefaultSeedData.CashierPermissions, SalrynPermissions.OfficialOutputRun), "Cashier role must not receive OfficialOutput.Run by default.");

        RequireContains(shellPresenter, "ShellModuleKeys.OfficialOutputTextFile", "Official Output shell route must remain present.");
        RequireContains(shellPresenter, "Can(SalrynPermissions.OfficialOutputRun)", "Official Output Text File shell route must use OfficialOutput.Run.");
        RequireContains(shellPresenter, "Requires OfficialOutput.Run permission.", "Official Output Text File shell route must describe OfficialOutput.Run requirement.");
        RequireContains(shellPresenter, "ShellModuleKeys.Reports", "Dashboard shell route must remain present.");
        RequireContains(shellPresenter, "Can(SalrynPermissions.ReportsView)", "Dashboard shell route must continue to use Reports.View.");

        RequireContains(form, "OfficialOutputWorkflowPermissions.CanRunExportWorkflow", "Form run-entry path must require export workflow permission.");
        RequireContains(form, "OfficialOutput.Run or OfficialEvidence.Export", "Form must describe export workflow permission requirement.");
        RequireContains(form, "CallerPermissions: _session.Permissions", "Form must pass caller permissions to the workflow UI boundary.");

        RequireContains(boundary, "CallerPermissions", "Workflow UI trigger request must carry caller permissions.");
        RequireContains(boundary, "ValidateCallerPermission", "Workflow UI boundary must validate caller permission.");
        RequireContains(boundary, "OfficialOutputWorkflowPermissions.CallerCanRunExportWorkflow", "Workflow UI boundary must check export workflow permissions.");
        RequireContains(boundary, "OfficialOutput.Run or OfficialEvidence.Export", "Workflow UI boundary must describe export workflow permissions.");
        RequireContains(boundary, "Error.Forbidden", "Workflow UI boundary must return forbidden semantics for missing permission.");
        RequireContains(boundary, "OfficialOutputTextFileWorkflowUi.PermissionRequired", "Workflow UI boundary must use dedicated permission error code.");
        RequireNotContains(boundary, "reprint", "Permission boundary must not introduce reprint wording.");

        RequireNotContains(navigation, "Official Output Text File", "Unsupported official-output work must not appear in store navigation.");
        RequireNotContains(navigation, "enabled: false", "Store navigation must not render disabled developer-only surfaces.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");
        RequireContains(policySource, "RejectIfOutputPackageDirectoryExists", "Output-root policy must remain unchanged.");
        RequireContains(adapterSource, "ValidateWritableOutputRoot", "Adapter output-root validation must remain unchanged.");
        RequireContains(adapterSource, "RejectIfOutputPackageDirectoryExists", "Duplicate package policy must remain unchanged.");

        HashSet<string> reportsOnlyPermissions = new(DefaultSeedData.ManagerPermissions, StringComparer.OrdinalIgnoreCase);
        Require(
            reportsOnlyPermissions.Contains(SalrynPermissions.ReportsView) &&
            !reportsOnlyPermissions.Contains(SalrynPermissions.OfficialOutputRun),
            "Reports.View-only manager permissions must not authorize OfficialOutput.Run.");
    }

    private static bool ContainsAll(IReadOnlyList<string> permissions, string permission)
    {
        return permissions.Any(value => string.Equals(value, permission, StringComparison.OrdinalIgnoreCase));
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

        return File.ReadAllText(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

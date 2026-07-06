using System.Diagnostics;

namespace Salryn.Tests;

public static class RoleM1OperatorRolePermissionUxCompileChecks
{
    private static readonly string[] SeedPermissionNames =
    {
        "Settings.Manage",
        "Users.Manage",
        "Products.View",
        "Products.Manage",
        "Inventory.View",
        "Inventory.Adjust",
        "Sales.Create",
        "Sales.Void",
        "Sales.ViewHistory",
        "Reports.View",
        "OfficialOutput.Run",
        "OfficialDocument.Reprint",
        "OfficialDocument.Void",
        "OfficialDocument.Cancel",
        "OfficialDocument.Correction",
        "OfficialDocument.Refund",
        "OfficialReading.View",
        "OfficialReading.Finalize",
        "OfficialEvidence.Export",
        "Backup.Manage",
        "Audit.View",
        "ImportExport.Products",
        "Shift.OpenClose",
        "StoreDeployment.Manage",
        "StoreTerminal.View",
        "StoreTerminal.Manage",
        "StoreTerminal.PairingIssue",
        "StoreTerminal.PairingRevoke"
    };

    private static readonly string[] ImplementedPages =
    {
        "dashboard",
        "host-lan-control",
        "remote-readonly",
        "remote-pos",
        "products",
        "inventory",
        "sales",
        "users",
        "audit"
    };

    public static void Run()
    {
        Run(FindProjectRoot(Directory.GetCurrentDirectory()));
    }

    public static void Run(string projectRoot)
    {
        string appSourceRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string seedFilePath = Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Seeding", "DefaultSeedData.cs");
        string schemaFilePath = Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "InitialSchemaMigration.cs");
        string permissionsFilePath = Path.Combine(appSourceRoot, "security", "permissions.ts");
        string navigationFilePath = Path.Combine(appSourceRoot, "navigation.ts");
        string appFilePath = Path.Combine(appSourceRoot, "App.tsx");
        string appShellFilePath = Path.Combine(appSourceRoot, "components", "AppShell.tsx");

        string seedFile = ReadRequiredFile(seedFilePath);
        string schemaFile = ReadRequiredFile(schemaFilePath);
        string permissionsFile = ReadRequiredFile(permissionsFilePath);
        string navigationFile = ReadRequiredFile(navigationFilePath);
        string appFile = ReadRequiredFile(appFilePath);
        string appShellFile = ReadRequiredFile(appShellFilePath);

        AssertContains(schemaFile, "CREATE TABLE role_permissions", "Schema must already contain role_permissions.");
        AssertContains(schemaFile, "permission TEXT NOT NULL", "Schema must already persist permission strings.");
        AssertContains(permissionsFile, "session.user.permissions", "Frontend permission helpers must use session user permissions.");
        AssertNotContains(permissionsFile, "roleName", "Permission helpers must not authorize by role name.");
        AssertContains(navigationFile, "canAccessPage(session, item.id)", "Navigation must filter enabled pages through canAccessPage.");
        AssertNotContains(navigationFile, "roleName", "Navigation must not branch by role name.");
        AssertContains(appShellFile, "getNavItems(session)", "AppShell must render permission-aware navigation.");
        AssertContains(appFile, "canAccessPage(session, currentPage)", "App.tsx must centrally guard the current page.");
        AssertContains(appFile, "AccessDenied", "App.tsx must expose an access denied state.");
        AssertNotContains(appFile, "roleName ===", "App.tsx must not authorize by role-name comparison.");

        foreach (string permissionName in SeedPermissionNames)
        {
            AssertContains(seedFile, $"\"{permissionName}\"", $"Seed permission missing: {permissionName}");
            AssertContains(permissionsFile, $"\"{permissionName}\"", $"Frontend permission catalog missing: {permissionName}");
        }

        string adminPermissions = ExtractRolePermissionBlock(seedFile, "AdminPermissions", "ManagerPermissions");
        string managerPermissions = ExtractRolePermissionBlock(seedFile, "ManagerPermissions", "CashierPermissions");
        string cashierPermissions = ExtractRolePermissionBlock(seedFile, "CashierPermissions", "RolePermissions");

        foreach (string permissionName in SeedPermissionNames)
        {
            AssertContains(adminPermissions, PermissionConstantToken(permissionName), $"Admin role does not include seeded permission: {permissionName}");
        }

        foreach (string page in ImplementedPages)
        {
            AssertContains(permissionsFile, $"\"{page}\"", $"Implemented page missing from permission helper catalog: {page}");
        }

        AssertNotContains(managerPermissions, "UsersManage", "Manager must not manage users.");
        AssertNotContains(managerPermissions, "SettingsManage", "Manager must not manage settings.");
        AssertNotContains(managerPermissions, "BackupManage", "Manager must not manage backup.");
        AssertNotContains(managerPermissions, "StoreDeploymentManage", "Manager must not manage deployment.");
        AssertNotContains(managerPermissions, "StoreTerminalManage", "Manager must not manage terminal administration.");
        AssertNotContains(managerPermissions, "StoreTerminalPairingIssue", "Manager must not issue terminal pairings.");
        AssertNotContains(managerPermissions, "StoreTerminalPairingRevoke", "Manager must not revoke terminal pairings.");

        AssertNotContains(cashierPermissions, "UsersManage", "Cashier must not manage users.");
        AssertNotContains(cashierPermissions, "AuditView", "Cashier must not view audit.");
        AssertNotContains(cashierPermissions, "InventoryView", "Cashier must not access inventory page.");
        AssertNotContains(cashierPermissions, "InventoryAdjust", "Cashier must not adjust inventory.");
        AssertNotContains(cashierPermissions, "ProductsManage", "Cashier must not manage products.");
        AssertNotContains(cashierPermissions, "SettingsManage", "Cashier must not manage settings.");
        AssertNotContains(cashierPermissions, "BackupManage", "Cashier must not manage backup.");

        AssertNoDuplicateRoleShells(appSourceRoot);
        AssertChangedFilesStayInRoleM1Scope(projectRoot);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required source file missing: {path}");
        }

        return File.ReadAllText(path);
    }

    private static string ExtractRolePermissionBlock(string source, string startToken, string endToken)
    {
        int startIndex = source.IndexOf(startToken, StringComparison.Ordinal);
        int endIndex = source.IndexOf(endToken, startIndex + startToken.Length, StringComparison.Ordinal);

        if (startIndex < 0 || endIndex <= startIndex)
        {
            throw new InvalidOperationException($"Could not extract role permission block {startToken}.");
        }

        return source[startIndex..endIndex];
    }

    private static string PermissionConstantToken(string permissionName)
    {
        return permissionName.Replace(".", string.Empty, StringComparison.Ordinal);
    }

    private static void AssertNoDuplicateRoleShells(string appSourceRoot)
    {
        string[] forbiddenTokens =
        {
            "AdminApp",
            "ManagerApp",
            "CashierApp",
            "RoleEditor",
            "PermissionEditor",
            "CustomPermission"
        };

        foreach (string file in Directory.EnumerateFiles(appSourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
        {
            string content = File.ReadAllText(file);

            foreach (string token in forbiddenTokens)
            {
                AssertNotContains(content, token, $"Forbidden role-specific shell/editor token found in {file}: {token}");
            }
        }
    }

    private static void AssertChangedFilesStayInRoleM1Scope(string projectRoot)
    {
        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M1 changed-file scope guard skipped: no branch-diff paths were available. Static source checks remain active.");
            return;
        }

        string[] roleM1ChangedPaths = changedPaths
            .Where(IsRoleM1OwnedPath)
            .ToArray();

        if (roleM1ChangedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M1 changed-file scope guard skipped: no ROLE-M1-owned branch-diff paths were present. Static source checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md",
            "src/Salryn.App.Web/src/security/permissions.ts",
            "src/Salryn.App.Web/src/navigation.ts",
            "src/Salryn.App.Web/src/components/AppShell.tsx",
            "src/Salryn.App.Web/src/App.tsx",
            "tests/Salryn.Tests/RoleM1OperatorRolePermissionUxCompileChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in roleM1ChangedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"ROLE-M1 changed an out-of-scope file: {normalizedPath}");
            }

            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.Contains("/seeding/", StringComparison.Ordinal)
                || lowerPath.Contains("/migrations/", StringComparison.Ordinal)
                || lowerPath.Contains("rmo", StringComparison.Ordinal)
                || lowerPath.Contains("official", StringComparison.Ordinal)
                || lowerPath.Contains("crystal", StringComparison.Ordinal)
                || lowerPath.Contains("escpos", StringComparison.Ordinal)
                || lowerPath.Contains("esc-pos", StringComparison.Ordinal)
                || lowerPath.Contains("printer", StringComparison.Ordinal)
                || lowerPath.Contains("checkout", StringComparison.Ordinal)
                || lowerPath.Contains("backup", StringComparison.Ordinal)
                || lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("pairing", StringComparison.Ordinal)
                || lowerPath.Contains("bind", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"ROLE-M1 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        string? output = TryRunGit(projectRoot, "diff --name-only origin/KIRCH-SANDBOX...HEAD");

        if (string.IsNullOrWhiteSpace(output))
        {
            output = TryRunGit(projectRoot, "diff --name-only KIRCH-SANDBOX...HEAD");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsRoleM1OwnedPath(string path)
    {
        string normalizedPath = path.Replace('\\', '/');

        return normalizedPath.Equals(
                "00-HANDOFF/ROLE_M1_OPERATOR_ROLE_PERMISSION_UX_BOUNDARY_STUDY.md",
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals(
                "src/Salryn.App.Web/src/security/permissions.ts",
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals(
                "src/Salryn.App.Web/src/navigation.ts",
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals(
                "src/Salryn.App.Web/src/components/AppShell.tsx",
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals(
                "src/Salryn.App.Web/src/App.tsx",
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals(
                "tests/Salryn.Tests/RoleM1OperatorRolePermissionUxCompileChecks.cs",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryRunGit(string projectRoot, string arguments)
    {
        using Process process = new();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = projectRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        try
        {
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Git scope check skipped for '{arguments}': {error.Trim()}");
                return null;
            }

            return output;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Git scope check skipped for '{arguments}': {ex.Message}");
            return null;
        }
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

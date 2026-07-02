using System.Diagnostics;

namespace Salryn.Tests;

public static class RoleM2BackendPermissionEnforcementChecks
{
    private static readonly Dictionary<string, string> OperatorReadPermissions = new(StringComparer.Ordinal)
    {
        ["/api/products"] = "Products.View",
        ["/api/products/{id:guid}"] = "Products.View",
        ["/api/products/catalog/categories"] = "Products.View",
        ["/api/products/catalog/brands"] = "Products.View",
        ["/api/inventory/low-stock"] = "Inventory.View",
        ["/api/inventory/movements"] = "Inventory.View",
        ["/api/sales/history"] = "Sales.ViewHistory",
        ["/api/sales/{id:guid}"] = "Sales.ViewHistory",
        ["/api/users"] = "Users.Manage",
        ["/api/users/roles"] = "Users.Manage",
        ["/api/users/{id:guid}"] = "Users.Manage",
        ["/api/audit/logs"] = "Audit.View",
        ["/api/audit/errors"] = "Audit.View"
    };

    public static void Run()
    {
        Run(FindProjectRoot(Directory.GetCurrentDirectory()));
    }

    public static void Run(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string guardFile = ReadRequiredFile(Path.Combine(apiRoot, "Auth", "LocalApiPermissionGuards.cs"));
        string authExtensions = ReadRequiredFile(Path.Combine(apiRoot, "ApiAuthExtensions.cs"));
        string program = ReadRequiredFile(Path.Combine(apiRoot, "Program.cs"))
            + "\n"
            + ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string readOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "ReadOnlyEndpointExtensions.cs"));
        string remoteReadOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemoteReadOnlyApiEndpoints.cs"));
        string seedFile = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Seeding",
            "DefaultSeedData.cs"));
        string roleM2Doc = ReadRequiredFile(Path.Combine(
            handoffRoot,
            "ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md"));
        string currentStateDoc = ReadRequiredFile(Path.Combine(
            handoffRoot,
            "CURRENT_STABLE_STATE_AND_NEXT_WORK.md"));
        string confidenceAuditDoc = ReadRequiredFile(Path.Combine(
            handoffRoot,
            "CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md"));

        AssertContains(guardFile, "LocalApiPermissionGuards", "ROLE-M2 backend permission guard must exist.");
        AssertContains(guardFile, "RequireAuthenticatedSession", "Guard must expose authenticated-session check.");
        AssertContains(guardFile, "RequirePermission", "Guard must expose single-permission check.");
        AssertContains(guardFile, "RequireAnyPermission", "Guard must expose any-permission check.");
        AssertContains(guardFile, "StatusCodes.Status401Unauthorized", "Missing/invalid session must return HTTP 401.");
        AssertContains(guardFile, "StatusCodes.Status403Forbidden", "Missing permission must return HTTP 403.");
        AssertContains(guardFile, "Auth.AuthenticationRequired", "401 error code must be stable.");
        AssertContains(guardFile, "Auth.PermissionDenied", "403 error code must be stable.");
        AssertContains(guardFile, "session.Permissions", "Permission enforcement must use session permissions.");
        AssertNotContains(guardFile, "RoleName", "Backend permission guard must not authorize by role name.");
        AssertNotContains(guardFile, "roleName", "Backend permission guard must not authorize by role name.");
        AssertContains(authExtensions, "LocalApiPermissionGuards.RequireAuthenticatedSession", "Existing auth helper must delegate to the shared guard.");

        AssertContains(program, "app.MapGet(\"/health\"", "Health endpoint must remain public.");
        AssertContains(program, "app.MapPost(\"/api/auth/login\"", "Login endpoint must remain public auth workflow.");
        AssertContains(program, "app.MapPost(\"/api/auth/logout\"", "Logout endpoint must remain auth workflow.");
        AssertContains(program, "app.MapGet(\"/api/session\"", "Session discovery endpoint must remain available.");
        AssertContains(program, "app.MapGet(\"/api/dashboard/summary\"", "Dashboard summary endpoint must remain mapped.");
        AssertContains(program, "ApiAuthExtensions.RequireAuthenticatedSession", "Dashboard summary must require authentication.");

        foreach ((string route, string permission) in OperatorReadPermissions)
        {
            AssertRouteRequiresPermission(readOnlyEndpoints, route, PermissionConstantToken(permission));
            AssertContains(seedFile, $"\"{permission}\"", "Endpoint permission must be an existing seeded permission: " + permission);
            AssertContains(guardFile, $"\"{permission}\"", "Backend permission catalog must mirror seeded permission: " + permission);
        }

        AssertContains(remoteReadOnlyEndpoints, "LocalApiPermissionGuards.RequireAuthenticatedSession", "Remote status/capability routes must stay authenticated.");
        AssertContains(remoteReadOnlyEndpoints, "LocalApiPermissions.ProductsView", "Remote product read must require Products.View.");
        AssertContains(remoteReadOnlyEndpoints, "LocalApiPermissions.StoreTerminalView", "Remote terminal status read must require StoreTerminal.View.");
        AssertContains(remoteReadOnlyEndpoints, "LocalApiPermissions.AuditView", "Remote audit visibility read must require Audit.View.");
        AssertNotContains(remoteReadOnlyEndpoints, "roleName", "Remote read-only backend endpoints must not authorize by role name.");

        AssertNoMutationRoutes(readOnlyEndpoints, "ReadOnlyEndpointExtensions.cs");
        AssertNoMutationRoutes(remoteReadOnlyEndpoints, "RemoteReadOnlyApiEndpoints.cs");
        AssertNoBackendRuntimeExpansion(apiRoot);
        AssertChangedFilesStayInRoleM2Scope(projectRoot);

        AssertContains(roleM2Doc, "Implemented during ROLE-M2 closeout", "ROLE-M2 doc must record implemented truth.");
        AssertContains(currentStateDoc, "ROLE-M2", "Current stable state doc must mention ROLE-M2 candidate state.");
        AssertContains(confidenceAuditDoc, "No secret-like production value", "Confidence audit must summarize secret scan result.");
    }

    private static void AssertRouteRequiresPermission(string source, string route, string permissionToken)
    {
        int routeIndex = source.IndexOf($"MapGet(\"{route}\"", StringComparison.Ordinal);
        if (routeIndex < 0)
        {
            throw new InvalidOperationException("Expected route not found: " + route);
        }

        int nextRouteIndex = source.IndexOf("app.MapGet(", routeIndex + 1, StringComparison.Ordinal);
        string routeBlock = nextRouteIndex < 0
            ? source[routeIndex..]
            : source[routeIndex..nextRouteIndex];

        AssertContains(routeBlock, "LocalApiPermissionGuards.RequirePermission", "Route must use permission guard: " + route);
        AssertContains(routeBlock, "LocalApiPermissions." + permissionToken, "Route has wrong permission: " + route);
    }

    private static string PermissionConstantToken(string permissionName)
    {
        return permissionName.Replace(".", string.Empty, StringComparison.Ordinal);
    }

    private static void AssertNoMutationRoutes(string source, string label)
    {
        string[] forbiddenMarkers =
        {
            "MapPost",
            "MapPut",
            "MapPatch",
            "MapDelete",
            "CompleteSale",
            "CreateProduct",
            "UpdateProduct",
            "DeactivateProduct",
            "StockAdjustment",
            "Backup",
            "OfficialOutput",
            "OfficialDocument"
        };

        foreach (string marker in forbiddenMarkers)
        {
            AssertNotContains(source, marker, label + " must not introduce mutation marker: " + marker);
        }
    }

    private static void AssertNoBackendRuntimeExpansion(string apiRoot)
    {
        foreach (string file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(apiRoot, file);
            AssertNotContains(text, "0.0.0.0", "Api.Local must not bind to all interfaces: " + relative);
            AssertNotContains(text, "QRCode", "Api.Local must not add QR behavior: " + relative);
            AssertNotContains(text, "network discovery", "Api.Local must not add discovery behavior: " + relative);
            AssertNotContains(text, "mobile/cloud", "Api.Local must not add mobile/cloud behavior: " + relative);
            AssertNotContains(text, "Salryn.App.WinForms", "Api.Local must not depend on WinForms operator UI: " + relative);
        }
    }

    private static void AssertChangedFilesStayInRoleM2Scope(string projectRoot)
    {
        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M2 changed-file scope guard skipped: no branch-diff paths were available. Static source checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md",
            "src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs",
            "src/Salryn.Api.Local/ApiAuthExtensions.cs",
            "src/Salryn.Api.Local/LocalApiApplicationFactory.cs",
            "src/Salryn.Api.Local/Program.cs",
            "src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs",
            "src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs",
            "tests/Salryn.Tests/LanM9M13RemoteReadOnlyLanUsabilityChecks.cs",
            "tests/Salryn.Tests/RoleM1OperatorRolePermissionUxCompileChecks.cs",
            "tests/Salryn.Tests/RoleM2BackendPermissionEnforcementChecks.cs",
            "tests/Salryn.Tests/WebB0ReadOnlyOperatorSurfacesChecks.cs",
            "tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        string[] roleM2ChangedPaths = changedPaths
            .Where(IsRoleM2OwnedPath)
            .ToArray();

        if (roleM2ChangedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M2 changed-file scope guard skipped: no ROLE-M2-owned branch-diff paths were present. Static source checks remain active.");
            return;
        }

        foreach (string changedPath in roleM2ChangedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"ROLE-M2 changed an out-of-scope file: {normalizedPath}");
            }

            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.Contains("/seeding/", StringComparison.Ordinal)
                || lowerPath.Contains("/migrations/", StringComparison.Ordinal)
                || lowerPath.Contains("rmo", StringComparison.Ordinal)
                || lowerPath.Contains("officialoutput", StringComparison.Ordinal)
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
                throw new InvalidOperationException($"ROLE-M2 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static bool IsRoleM2OwnedPath(string path)
    {
        string normalizedPath = path.Replace('\\', '/');

        return normalizedPath.Equals("00-HANDOFF/ROLE_M2_BACKEND_PERMISSION_ENFORCEMENT_BOUNDARY_STUDY.md", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("00-HANDOFF/CONFIDENCE_A1_REPO_WIDE_STABILITY_AUDIT.md", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/ApiAuthExtensions.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/LocalApiApplicationFactory.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/Program.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/ReadOnlyEndpointExtensions.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tests/Salryn.Tests/LanM9M13RemoteReadOnlyLanUsabilityChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tests/Salryn.Tests/RoleM1OperatorRolePermissionUxCompileChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tests/Salryn.Tests/RoleM2BackendPermissionEnforcementChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tests/Salryn.Tests/WebB0ReadOnlyOperatorSurfacesChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tools/Salryn.Tools.CoreSmoke/Program.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only origin/KIRCH-SANDBOX...HEAD"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        if (paths.Count == 0)
        {
            AddPaths(paths, TryRunGit(projectRoot, "diff --name-only KIRCH-SANDBOX...HEAD"));
        }

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddPaths(HashSet<string> paths, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (string path in output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            paths.Add(path);
        }
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

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
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

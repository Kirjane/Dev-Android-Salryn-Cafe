namespace Salryn.Tests;

public static class WebA3ShellDesignSystemChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");

        // Required files exist.
        RequireFileExists(webSrc, "components", "AppShell.tsx");
        RequireFileExists(webSrc, "pages", "LoginPage.tsx");
        RequireFileExists(webSrc, "pages", "DashboardPage.tsx");
        RequireDirectoryExists(webSrc, "components", "ui");
        RequireFileExists(webSrc, "components", "ui", "MetricCard.tsx");
        RequireFileExists(webSrc, "components", "ui", "StatusPill.tsx");
        RequireFileExists(webSrc, "components", "ui", "DataTable.tsx");

        string sessionApiText = ReadRequiredFile(webSrc, "api", "session.ts");
        string dashboardApiText = ReadRequiredFile(webSrc, "api", "dashboard.ts");
        string webAppText = ReadRequiredFile(webSrc, "App.tsx");
        string dashboardPageText = ReadRequiredFile(webSrc, "pages", "DashboardPage.tsx");
        string loginPageText = ReadRequiredFile(webSrc, "pages", "LoginPage.tsx");
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        // Real backend wiring preserved.
        RequireContains(sessionApiText, "/api/auth/login", "Web app must call /api/auth/login.");
        RequireContains(sessionApiText, "/api/session", "Web app must call /api/session.");
        RequireContains(dashboardApiText, "/api/dashboard/summary", "Web app must call /api/dashboard/summary.");
        RequireContains(dashboardPageText, "fetchDashboardSummary", "Dashboard must read the dashboard summary.");
        RequireContains(webAppText, "AppShell", "Authenticated app must render through AppShell.");
        RequireContains(webAppText, "useAppSession", "App must remain session-aware.");
        RequireContains(loginPageText, "Sign in to Salryn POS", "Login must use client-facing Salryn POS branding.");
        RequireNotContains(loginPageText, "local API", "Login must not expose local API diagnostics.");

        // Guard wired into CoreSmoke.
        RequireContains(coreSmokeText, "WebA3ShellDesignSystemChecks.Run();", "CoreSmoke must run WEB-A3 checks.");

        // Scoped: dashboard/login/session clients remain checkout-free; host checkout lives in CheckoutPage.
        foreach (string scoped in new[] { sessionApiText, dashboardApiText, dashboardPageText, loginPageText })
        {
            RequireNotContains(scoped, "checkout", "WEB-A3 must not add checkout logic to dashboard/session clients.");
            RequireNotContains(scoped, "handoff", "WEB-A3 must not reintroduce session handoff.");
            RequireNotContains(scoped, "NativeHandoff", "WEB-A3 must not reintroduce native handoff.");
        }

        // Approved stacked admin/safety pages (not free module sprawl).
        // Backup UI1: BackupRestorePage is the only approved Backup* page.
        HashSet<string> approvedModulePages = new(StringComparer.OrdinalIgnoreCase)
        {
            "BackupRestorePage.tsx",
        };

        // Keep rejecting uncontrolled Backup*/Settings*/OfficialOutput* sprawl outside the allowlist.
        string[] forbiddenPagePrefixes =
        {
            "Backup", "Settings", "OfficialOutput",
        };
        string pagesDir = Path.Combine(webSrc, "pages");
        foreach (string pageFile in Directory.GetFiles(pagesDir, "*.tsx", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(pageFile);
            if (name.Equals("LoginPage.tsx", StringComparison.Ordinal) ||
                name.Equals("DashboardPage.tsx", StringComparison.Ordinal) ||
                name.Equals("ProductsPage.tsx", StringComparison.Ordinal) ||
                name.Equals("InventoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("SalesHistoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("UsersPage.tsx", StringComparison.Ordinal) ||
                name.Equals("AuditPage.tsx", StringComparison.Ordinal) ||
                name.Equals("CheckoutPage.tsx", StringComparison.Ordinal) ||
                approvedModulePages.Contains(name))
            {
                continue;
            }

            foreach (string prefix in forbiddenPagePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"WEB-A3 must not add module page: {name}");
                }
            }
        }

        // Backup UI1 must remain an admin-gated local safety surface without client-facing disclaimer walls.
        string navigationText = ReadRequiredFile(webSrc, "navigation.ts");
        string permissionsText = ReadRequiredFile(webSrc, "security", "permissions.ts");
        string appShellText = ReadRequiredFile(webSrc, "components", "AppShell.tsx");
        string backupPageText = ReadRequiredFile(webSrc, "pages", "BackupRestorePage.tsx");
        RequireContains(navigationText, "id: \"backup-restore\", label: \"Backup & Restore\"", "BackupRestorePage must remain an Admin nav item.");
        RequireContains(permissionsText, "\"backup-restore\": [Permissions.BackupManage]", "BackupRestorePage must require Backup.Manage.");
        RequireNotContains(appShellText, "item.id === \"backup-restore\"", "AppShell must not show an Admin badge on Backup & Restore.");
        RequireContains(webAppText, "BackupRestorePage", "App must render the approved BackupRestorePage.");
        RequireContains(backupPageText, "Restore replaces the current local database", "BackupRestorePage must keep the destructive restore warning.");
        RequireContains(backupPageText, "Type RESTORE", "BackupRestorePage must keep typed restore confirmation.");
        RequireNotContains(backupPageText, "not cloud backup", "BackupRestorePage must not show cloud disclaimer copy.");
        RequireNotContains(backupPageText, "installer migration proof", "BackupRestorePage must not show installer disclaimer copy.");
        RequireNotContains(backupPageText, "BIR archive", "BackupRestorePage must not show regulatory disclaimer copy.");
        RequireContains(navigationText, "VITE_SALRYN_INTERNAL_SURFACES", "Internal navigation must require an explicit build flag.");
        RequireContains(navigationText, "label: \"Terminal Management\"", "Internal LAN navigation must use Terminal Management.");
        RequireContains(navigationText, "label: \"Report Artifacts\"", "Report Artifacts must remain explicitly internal-gated.");
        RequireNotContains(appShellText, "SALRYN LEDGER CONSOLE", "AppShell must not use engineering-console branding.");
        RequireNotContains(appShellText, "Local API", "AppShell must not expose local API diagnostics.");
        RequireNotContains(appShellText, "Host-local API surfaces", "AppShell footer must not expose API surfaces.");

        // Broad scan of web source for clearly-unique forbidden capabilities.
        string combinedWebSource = ReadAllWebSource(webSrc);
        string capabilitySource = combinedWebSource.Replace(
            "No ESC/POS",
            string.Empty,
            StringComparison.Ordinal);

        // PRE-BETA-C/D + PRE-I audit CSV: approved file surfaces only —
        // store logo (Settings.Manage), portable backup package import (Backup.Manage),
        // and filtered audit CSV download (Audit.View).
        string storeSettingsPageText = ReadRequiredFile(webSrc, "pages", "StoreSettingsPage.tsx");
        string storeSettingsApiText = ReadRequiredFile(webSrc, "api", "storeSettings.ts");
        string backupApiText = ReadRequiredFile(webSrc, "api", "backups.ts");
        string auditApiText = ReadRequiredFile(webSrc, "api", "audit.ts");
        string auditPageText = ReadRequiredFile(webSrc, "pages", "AuditPage.tsx");
        RequireContains(storeSettingsPageText, "Upload logo", "Store logo upload UI must remain on Store Settings.");
        RequireContains(storeSettingsApiText, "uploadStoreLogo", "Store logo API client must remain scoped.");
        RequireContains(storeSettingsApiText, "/api/settings/store/logo", "Store logo upload must use settings logo endpoint.");
        RequireContains(backupPageText, "Import Backup File", "Portable backup import UI must remain on Backup & Restore.");
        RequireContains(backupApiText, "importBackupFile", "Portable backup import client must remain scoped.");
        RequireContains(auditPageText, "Download audit CSV", "Audit CSV export UI must remain on Audit page.");
        RequireContains(auditApiText, "downloadAuditCsv", "Audit CSV export client must remain scoped.");
        RequireContains(auditApiText, "/api/audit/logs/export", "Audit CSV export must use audit export endpoint.");
        // Path-based exclusion avoids Replace false-negatives from encoding/newline drift.
        string capabilitySourceWithoutLogoUpload = ReadAllWebSourceExcluding(webSrc, new[]
        {
            Path.Combine("pages", "StoreSettingsPage.tsx"),
            Path.Combine("api", "storeSettings.ts"),
            Path.Combine("pages", "BackupRestorePage.tsx"),
            Path.Combine("api", "backups.ts"),
            Path.Combine("pages", "AuditPage.tsx"),
            Path.Combine("api", "audit.ts"),
        }).Replace("No ESC/POS", string.Empty, StringComparison.Ordinal);

        string[] alwaysForbiddenTokens =
        {
            "/api/session/handoff/register",
            "/api/session/handoff/complete",
            "NativeHandoff",
            "CrystalDecisions",
            "SAPBusinessObjects",
            ".rpt",
            "file-picker",
            "window.print",
            "PrinterSettings",
            "printerTarget",
            "scanner",
            "photo",
        };

        // Upload-related tokens remain banned outside the approved logo settings surface.
        string[] uploadForbiddenOutsideLogo =
        {
            "multipart/form-data",
            "/upload",
            "FormData(",
            "type=\"file\"",
            "blob",
        };

        foreach (string token in alwaysForbiddenTokens)
        {
            RequireNotContains(capabilitySource, token, $"WEB-A3 must not add forbidden capability: {token}");
        }

        // Client terms may name unsupported hardware explicitly, but must not advertise the capability.
        RequireNotContains(capabilitySource, "ESC/POS supported", "WEB-A3 must not claim ESC/POS support.");
        RequireNotContains(capabilitySource, "escpos", "WEB-A3 must not introduce an ESC/POS implementation identifier.");

        foreach (string token in uploadForbiddenOutsideLogo)
        {
            RequireNotContains(
                capabilitySourceWithoutLogoUpload,
                token,
                $"WEB-A3 must not add forbidden capability outside store logo branding: {token}");
        }
    }

    private static string ReadAllWebSource(string webSrc)
    {
        IEnumerable<string> files = Directory
            .GetFiles(webSrc, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase));

        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static string ReadAllWebSourceExcluding(string webSrc, IEnumerable<string> relativeExcludeSuffixes)
    {
        string[] excludes = relativeExcludeSuffixes
            .Select(x => x.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))
            .ToArray();
        IEnumerable<string> files = Directory
            .GetFiles(webSrc, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                foreach (string suffix in excludes)
                {
                    if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            });

        return string.Join("\n", files.Select(File.ReadAllText));
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

    private static void RequireFileExists(string root, params string[] pathParts)
    {
        string path = root;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }
    }

    private static void RequireDirectoryExists(string root, params string[] pathParts)
    {
        string path = root;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"Required directory was not found: {path}");
        }
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;

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

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

namespace Salryn.Tests;

public static class PosStabB2AuditViewerChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string auditFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Audit",
            "AuditLogViewerForm.cs");
        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            auditFormText,
            "Audit / Error Logs",
            "B2-B Audit module must expose a real audit/error log viewer.");
        RequireContains(
            auditFormText,
            "_services.AuditReadRepository",
            "B2-B Audit viewer must read audit rows through AppServices.");
        RequireContains(
            auditFormText,
            "_services.ErrorLogReadRepository",
            "B2-B Audit viewer must read error rows through AppServices.");
        RequireContains(
            auditFormText,
            "SearchAuditLogsAsync(PageRequest.FirstPage(100))",
            "B2-B Audit viewer must use the audit read repository search method.");
        RequireContains(
            auditFormText,
            "SearchErrorLogsAsync(PageRequest.FirstPage(100))",
            "B2-B Audit viewer must use the error log read repository search method.");
        RequireContains(
            auditFormText,
            "J20MvpUi.Grid()",
            "B2-B Audit viewer must use read-only MVP grids.");
        RequireContains(
            auditFormText,
            "Refresh",
            "B2-B Audit viewer must expose a refresh action.");
        RequireDoesNotContain(
            auditFormText,
            "Microsoft.Data.Sqlite",
            "B2-B Audit viewer must not reference SQLite directly.");
        RequireDoesNotContain(
            auditFormText,
            "SqliteConnection",
            "B2-B Audit viewer must not open SQLite connections directly.");
        RequireDoesNotContain(
            auditFormText,
            "INSERT ",
            "B2-B Audit viewer must not issue insert SQL.");
        RequireDoesNotContain(
            auditFormText,
            "UPDATE ",
            "B2-B Audit viewer must not issue update SQL.");
        RequireDoesNotContain(
            auditFormText,
            "DELETE ",
            "B2-B Audit viewer must not issue delete SQL.");
        RequireDoesNotContain(
            auditFormText,
            "File.Write",
            "B2-B Audit viewer must not write files.");

        RequireContains(
            appServicesText,
            "public IAuditReadRepository AuditReadRepository { get; }",
            "B2-B AppServices must expose the audit read repository.");
        RequireContains(
            appServicesText,
            "public IErrorLogReadRepository ErrorLogReadRepository { get; }",
            "B2-B AppServices must expose the error log read repository.");
        RequireContains(
            appServicesText,
            "repositories.CreateAuditReadRepository();",
            "B2-B AppServices must create audit read repository through the repository factory.");
        RequireContains(
            appServicesText,
            "repositories.CreateErrorLogReadRepository();",
            "B2-B AppServices must create error log read repository through the repository factory.");

        RequireContains(
            shellText,
            "using Salryn.App.WinForms.Features.Audit;",
            "B2-B Shell must reference the audit feature form.");
        RequireContains(
            shellText,
            "module.Key == ShellModuleKeys.Audit",
            "B2-B Shell must route the Audit module.");
        RequireContains(
            shellText,
            "using AuditLogViewerForm auditForm = new(_services);",
            "B2-B Shell must open the real Audit viewer through AppServices.");

        RequireContains(
            coreSmokeText,
            "PosStabB2AuditViewerChecks.Run();",
            "CoreSmoke must run POS-STAB-B2 audit viewer checks.");
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

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
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

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

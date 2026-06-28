namespace Salryn.Tests;

public static class PosStabB6AuditOperationsChecks
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
        string readModelsText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "ReadModels",
            "InventorySystemReadModels.cs");
        string repositoryInterfacesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Repositories",
            "ReadRepositoryInterfaces.cs");
        string logRepositoriesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "System",
            "SqliteLogReadRepositories.cs");
        string initialSchemaText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "InitialSchemaMigration.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(readModelsText, "AuditLogSearchFilter", "B6 must define audit log search filter contracts.");
        RequireContains(readModelsText, "ErrorLogSearchFilter", "B6 must define error log search filter contracts.");
        RequireContains(readModelsText, "SearchText Search", "B6 filters must carry search text through Core read models.");

        RequireContains(repositoryInterfacesText, "SearchAuditLogsAsync(", "B6 audit repository contract must expose audit search.");
        RequireContains(repositoryInterfacesText, "AuditLogSearchFilter filter", "B6 audit repository contract must accept audit search filters.");
        RequireContains(repositoryInterfacesText, "SearchErrorLogsAsync(", "B6 error repository contract must expose error search.");
        RequireContains(repositoryInterfacesText, "ErrorLogSearchFilter filter", "B6 error repository contract must accept error search filters.");

        RequireContains(logRepositoriesText, "a.action LIKE $searchLike", "B6 audit repository must search action text.");
        RequireContains(logRepositoriesText, "a.entity_type LIKE $searchLike", "B6 audit repository must search entity type text.");
        RequireContains(logRepositoriesText, "a.entity_id", "B6 audit repository must search entity id text.");
        RequireContains(logRepositoriesText, "u.display_name", "B6 audit repository must search user display name.");
        RequireContains(logRepositoriesText, "IFNULL(a.reason, '') LIKE $searchLike", "B6 audit repository must search audit reason text.");
        RequireContains(logRepositoriesText, "e.area LIKE $searchLike", "B6 error repository must search error area text.");
        RequireContains(logRepositoriesText, "e.message LIKE $searchLike", "B6 error repository must search error message text.");
        RequireContains(logRepositoriesText, "e.technical_detail", "B6 error repository must search technical detail text.");
        RequireContains(logRepositoriesText, "a.created_at >= $from", "B6 audit repository must support from-date filtering.");
        RequireContains(logRepositoriesText, "a.created_at <= $to", "B6 audit repository must support to-date filtering.");
        RequireContains(logRepositoriesText, "LIMIT $limit OFFSET $offset", "B6 log repositories must keep paging in SQL.");
        RequireDoesNotContain(logRepositoriesText, "DROP TABLE", "B6 log repositories must not change schema.");
        RequireDoesNotContain(logRepositoriesText, "CREATE TABLE", "B6 log repositories must not add schema migration behavior.");

        RequireContains(auditFormText, "_fromDatePicker", "B6 audit viewer must expose from-date filtering.");
        RequireContains(auditFormText, "_toDatePicker", "B6 audit viewer must expose to-date filtering.");
        RequireContains(auditFormText, "ShowCheckBox = true", "B6 audit viewer must use optional DateTimePicker filters.");
        RequireContains(auditFormText, "_searchTextBox", "B6 audit viewer must expose text search.");
        RequireContains(auditFormText, "_pageSizeComboBox", "B6 audit viewer must expose page size selection.");
        RequireContains(auditFormText, "AllowedPageSizes", "B6 audit viewer must bound page size choices.");
        RequireContains(auditFormText, "_previousPageButton", "B6 audit viewer must expose previous-page paging.");
        RequireContains(auditFormText, "_nextPageButton", "B6 audit viewer must expose next-page paging.");
        RequireContains(auditFormText, "AuditLogSearchFilter", "B6 audit viewer must build audit filter contracts.");
        RequireContains(auditFormText, "ErrorLogSearchFilter", "B6 audit viewer must build error filter contracts.");
        RequireContains(auditFormText, "_services.AuditReadRepository", "B6 audit viewer must read audit rows through AppServices.");
        RequireContains(auditFormText, "_services.ErrorLogReadRepository", "B6 audit viewer must read error rows through AppServices.");
        RequireContains(auditFormText, "TotalPages", "B6 audit viewer must show page/total status.");
        RequireContains(auditFormText, "TotalCount", "B6 audit viewer must show total row count status.");
        RequireContains(auditFormText, "J20MvpUi.Grid()", "B6 audit viewer must keep read-only MVP grids.");
        RequireDoesNotContain(auditFormText, "Microsoft.Data.Sqlite", "B6 audit viewer must not reference SQLite directly.");
        RequireDoesNotContain(auditFormText, "SqliteConnection", "B6 audit viewer must not open SQLite connections directly.");
        RequireDoesNotContain(auditFormText, "INSERT ", "B6 audit viewer must not issue insert SQL.");
        RequireDoesNotContain(auditFormText, "UPDATE ", "B6 audit viewer must not issue update SQL.");
        RequireDoesNotContain(auditFormText, "DELETE ", "B6 audit viewer must not issue delete SQL.");
        RequireDoesNotContain(auditFormText, "File.Write", "B6 audit viewer must not export files in this batch.");
        RequireDoesNotContain(auditFormText, "Csv", "B6 audit viewer must not add CSV export.");
        RequireNoForbiddenMarkers(auditFormText, "Audit viewer");

        RequireContains(initialSchemaText, "CREATE TABLE audit_logs", "B6 guard should read existing audit schema only.");
        RequireContains(initialSchemaText, "CREATE TABLE error_logs", "B6 guard should read existing error log schema only.");
        RequireContains(coreSmokeText, "PosStabB6AuditOperationsChecks.Run();", "CoreSmoke must run POS-STAB-B6 audit operations checks.");
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

    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "R" + "MO",
            "official output",
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "raw " + "USB",
            "scan" + "ner",
            "hard" + "ware",
            "cloud backup"
        };
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B6 {surface} must not introduce forbidden marker: {marker}");
        }
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

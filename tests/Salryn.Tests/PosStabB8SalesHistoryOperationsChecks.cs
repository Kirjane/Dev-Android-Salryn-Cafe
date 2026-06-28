namespace Salryn.Tests;

public static class PosStabB8SalesHistoryOperationsChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string salesHistoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Sales",
            "SalesHistoryForm.cs");
        string saleRepositoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "Sales",
            "SqliteSaleReadRepository.cs");
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

        RequireContains(salesHistoryText, "_fromDatePicker", "B8 sales history must expose from-date filtering.");
        RequireContains(salesHistoryText, "_toDatePicker", "B8 sales history must expose to-date filtering.");
        RequireContains(salesHistoryText, "ShowCheckBox = true", "B8 sales history must use optional DateTimePicker filters.");
        RequireContains(salesHistoryText, "_statusFilterComboBox", "B8 sales history must expose status filtering.");
        RequireContains(salesHistoryText, "\"Completed\"", "B8 sales history must offer completed status filter.");
        RequireContains(salesHistoryText, "\"Voided\"", "B8 sales history must offer voided status filter.");
        RequireContains(salesHistoryText, "_receiptSearchTextBox", "B8 sales history must keep receipt number search.");
        RequireContains(salesHistoryText, "_pageSizeComboBox", "B8 sales history must expose page size selection.");
        RequireContains(salesHistoryText, "AllowedPageSizes", "B8 sales history must bound page size choices.");
        RequireContains(salesHistoryText, "_previousPageButton", "B8 sales history must expose previous-page paging.");
        RequireContains(salesHistoryText, "_nextPageButton", "B8 sales history must expose next-page paging.");
        RequireContains(salesHistoryText, "_applyButton", "B8 sales history must expose Apply.");
        RequireContains(salesHistoryText, "_refreshButton", "B8 sales history must expose Refresh.");
        RequireContains(salesHistoryText, "SaleHistoryFilter", "B8 sales history must build sale history filter contracts.");
        RequireContains(salesHistoryText, "_services.SaleReadRepository", "B8 sales history must read sales through AppServices.");
        RequireContains(salesHistoryText, "TotalPages", "B8 sales history must show page/total status.");
        RequireContains(salesHistoryText, "TotalCount", "B8 sales history must show total row count status.");
        RequireContains(salesHistoryText, "ReceiptPreviewForm", "B8 sales history must still open receipt preview.");
        RequireContains(salesHistoryText, "Void Sale", "B8 sales history must still support void sale.");
        RequireContains(salesHistoryText, "VoidSelectedSaleAsync", "B8 sales history must keep void sale workflow.");
        RequireContains(salesHistoryText, "J20MvpUi.Grid()", "B8 sales history must keep read-only MVP grids.");
        RequireDoesNotContain(salesHistoryText, "Microsoft.Data.Sqlite", "B8 sales history must not reference SQLite directly.");
        RequireDoesNotContain(salesHistoryText, "SqliteConnection", "B8 sales history must not open SQLite connections directly.");
        RequireDoesNotContain(salesHistoryText, "INSERT ", "B8 sales history must not issue insert SQL.");
        RequireDoesNotContain(salesHistoryText, "UPDATE ", "B8 sales history must not issue update SQL.");
        RequireDoesNotContain(salesHistoryText, "DELETE ", "B8 sales history must not issue delete SQL.");
        RequireDoesNotContain(salesHistoryText, "File.Write", "B8 sales history must not export files in this batch.");
        RequireDoesNotContain(salesHistoryText, "Csv", "B8 sales history must not add CSV export.");
        RequireNoForbiddenMarkers(salesHistoryText, "Sales history");

        RequireContains(saleRepositoryText, "filter.From?.ToUniversalTime().ToString(\"O\")", "B8 sale repository must normalize from-date bounds to UTC.");
        RequireContains(saleRepositoryText, "filter.To?.ToUniversalTime().ToString(\"O\")", "B8 sale repository must normalize to-date bounds to UTC.");
        RequireContains(saleRepositoryText, "s.sale_date >= $from", "B8 sale repository must support from-date filtering.");
        RequireContains(saleRepositoryText, "s.sale_date <= $to", "B8 sale repository must support to-date filtering.");
        RequireContains(saleRepositoryText, "LIMIT $limit OFFSET $offset", "B8 sale repository must keep paging in SQL.");
        RequireDoesNotContain(saleRepositoryText, "DROP TABLE", "B8 sale repository must not change schema.");
        RequireDoesNotContain(saleRepositoryText, "CREATE TABLE", "B8 sale repository must not add schema migration behavior.");

        RequireContains(initialSchemaText, "CREATE TABLE sales", "B8 guard should read existing sales schema only.");
        RequireContains(coreSmokeText, "PosStabB8SalesHistoryOperationsChecks.Run();", "CoreSmoke must run POS-STAB-B8 sales history operations checks.");
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
            RequireDoesNotContain(text, marker, $"B8 {surface} must not introduce forbidden marker: {marker}");
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

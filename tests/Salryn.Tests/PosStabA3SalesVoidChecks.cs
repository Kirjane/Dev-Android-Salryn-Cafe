namespace Salryn.Tests;

public static class PosStabA3SalesVoidChecks
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
        string reasonFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Sales",
            "SalesVoidReasonForm.cs");
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
        string inventoryReadModelText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "ReadModels",
            "InventorySystemReadModels.cs");
        string inventoryRepositoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "Inventory",
            "SqliteInventoryReadRepository.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            salesHistoryText,
            "_voidSaleButton",
            "A3 Sales History must expose a guarded Void Sale action.");
        RequireContains(
            salesHistoryText,
            "SalesVoidReasonForm",
            "A3 Sales History must require a void reason prompt.");
        RequireContains(
            salesHistoryText,
            "SalrynPermissions.SalesVoid",
            "A3 Sales History must gate voiding by Sales.Void permission.");
        RequireContains(
            salesHistoryText,
            "SaleStatus.Completed",
            "A3 Sales History must only void completed sales.");
        RequireContains(
            salesHistoryText,
            "FindSaleByIdAsync",
            "A3 Sales History must reload selected sale detail before voiding.");
        RequireContains(
            salesHistoryText,
            "ReferenceType: \"SALE\"",
            "A3 Sales History must lookup stock movements by sale reference.");
        RequireContains(
            salesHistoryText,
            "ReferenceId: sale.Id.ToString(\"D\")",
            "A3 Sales History must lookup stock movements by selected sale id.");
        RequireContains(
            salesHistoryText,
            "StockMovementType.VoidSale",
            "A3 Sales History must build void-sale stock reversal movements.");
        RequireContains(
            salesHistoryText,
            ".MarkVoidedAsync(",
            "A3 Sales History must use ISaleWriteRepository.MarkVoidedAsync.");
        RequireDoesNotContain(
            salesHistoryText,
            "Microsoft.Data.Sqlite",
            "A3 Sales History UI must not reference SQLite directly.");
        RequireDoesNotContain(
            salesHistoryText,
            "SqliteConnection",
            "A3 Sales History UI must not create SQLite connections.");
        RequireDoesNotContain(
            salesHistoryText,
            "INSERT ",
            "A3 Sales History UI must not issue direct insert SQL.");
        RequireDoesNotContain(
            salesHistoryText,
            "UPDATE ",
            "A3 Sales History UI must not issue direct update SQL.");
        RequireDoesNotContain(
            salesHistoryText,
            "DELETE ",
            "A3 Sales History UI must not issue direct delete SQL.");

        RequireContains(
            reasonFormText,
            "Void reason is required.",
            "A3 void prompt must require a reason.");
        RequireDoesNotContain(
            reasonFormText,
            "Microsoft.Data.Sqlite",
            "A3 void prompt must not reference SQLite directly.");

        RequireContains(
            appServicesText,
            "public ISaleWriteRepository SaleWriteRepository { get; }",
            "A3 AppServices must expose the existing sale write repository boundary.");
        RequireContains(
            appServicesText,
            "repositories.CreateSaleWriteRepository();",
            "A3 AppServices must construct sale write repository through the repository factory.");

        RequireContains(
            shellText,
            "new SalesHistoryForm(_services, _session)",
            "A3 Shell must pass current session into Sales History when available.");

        RequireContains(
            inventoryReadModelText,
            "string? ReferenceType = null",
            "A3 stock movement filter must support reference type.");
        RequireContains(
            inventoryReadModelText,
            "string? ReferenceId = null",
            "A3 stock movement filter must support reference id.");
        RequireContains(
            inventoryRepositoryText,
            "sm.reference_type = $referenceType",
            "A3 inventory read repository must filter by stock movement reference type.");
        RequireContains(
            inventoryRepositoryText,
            "sm.reference_id = $referenceId",
            "A3 inventory read repository must filter by stock movement reference id.");

        RequireContains(
            coreSmokeText,
            "PosStabA3SalesVoidChecks.Run();",
            "CoreSmoke must run POS-STAB-A3 sales void checks.");
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

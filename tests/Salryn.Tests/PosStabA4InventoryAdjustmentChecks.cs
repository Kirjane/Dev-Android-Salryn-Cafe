namespace Salryn.Tests;

public static class PosStabA4InventoryAdjustmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string inventoryFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Inventory",
            "InventoryForm.cs");
        string adjustmentFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Inventory",
            "InventoryAdjustmentForm.cs");
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
            inventoryFormText,
            "_adjustStockButton",
            "A4 Inventory must expose a guarded Adjust Stock action.");
        RequireContains(
            inventoryFormText,
            "InventoryAdjustmentForm",
            "A4 Inventory must launch the adjustment workflow form.");
        RequireContains(
            inventoryFormText,
            "SalrynPermissions.InventoryAdjust",
            "A4 Inventory adjustment must be permission-gated.");
        RequireContains(
            inventoryFormText,
            "_services.InventoryWriteRepository",
            "A4 Inventory must pass the existing inventory write repository boundary.");
        RequireDoesNotContain(
            inventoryFormText,
            "Microsoft.Data.Sqlite",
            "A4 Inventory UI must not reference SQLite directly.");
        RequireDoesNotContain(
            inventoryFormText,
            "SqliteConnection",
            "A4 Inventory UI must not create SQLite connections.");

        RequireContains(
            adjustmentFormText,
            "ProductSearchFilter",
            "A4 adjustment workflow must search products through product read repository.");
        RequireContains(
            adjustmentFormText,
            "TrackStock",
            "A4 adjustment workflow must guard stock-tracked products.");
        RequireContains(
            adjustmentFormText,
            "StockMovementType.AdjustmentAdd",
            "A4 adjustment workflow must support positive stock adjustments.");
        RequireContains(
            adjustmentFormText,
            "StockMovementType.AdjustmentRemove",
            "A4 adjustment workflow must support negative stock adjustments.");
        RequireContains(
            adjustmentFormText,
            "Adjustment reason is required.",
            "A4 adjustment workflow must require a reason.");
        RequireContains(
            adjustmentFormText,
            ".AddStockMovementAsync(",
            "A4 adjustment workflow must write through IInventoryWriteRepository.");
        RequireContains(
            adjustmentFormText,
            "ReferenceType = \"INVENTORY_ADJUSTMENT\"",
            "A4 adjustment workflow must mark manual adjustment reference type.");
        RequireDoesNotContain(
            adjustmentFormText,
            "Microsoft.Data.Sqlite",
            "A4 adjustment workflow must not reference SQLite directly.");
        RequireDoesNotContain(
            adjustmentFormText,
            "SqliteConnection",
            "A4 adjustment workflow must not create SQLite connections.");
        RequireDoesNotContain(
            adjustmentFormText,
            "INSERT ",
            "A4 adjustment workflow must not issue direct insert SQL.");
        RequireDoesNotContain(
            adjustmentFormText,
            "UPDATE ",
            "A4 adjustment workflow must not issue direct update SQL.");
        RequireDoesNotContain(
            adjustmentFormText,
            "DELETE ",
            "A4 adjustment workflow must not issue direct delete SQL.");

        RequireContains(
            shellText,
            "new InventoryForm(_services, _session)",
            "A4 Shell must pass current session into Inventory when available.");

        RequireContains(
            coreSmokeText,
            "PosStabA4InventoryAdjustmentChecks.Run();",
            "CoreSmoke must run POS-STAB-A4 inventory adjustment checks.");
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

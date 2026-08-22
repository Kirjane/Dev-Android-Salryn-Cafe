namespace Salryn.Tests;

public static class PosStabB3ProductMaintenanceChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string managementFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Products",
            "ProductManagementForm.cs");
        string createFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Products",
            "ProductCreateForm.cs");
        string editFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Products",
            "ProductEditForm.cs");
        string presenterText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Products",
            "ProductManagementPresenter.cs");
        string updateProductMethodText = SliceBetween(
            presenterText,
            "public async Task<ProductEditOperationResult> UpdateProductAsync",
            "public async Task<ProductEditOperationResult> DeactivateProductAsync");
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

        RequireContains(managementFormText, "_editButton", "B3 Product Management must expose an Edit action.");
        RequireContains(managementFormText, "_deactivateButton", "B3 Product Management must expose a Deactivate action.");
        RequireContains(managementFormText, "OpenEditFormAsync", "B3 Product Management must open the edit workflow.");
        RequireContains(managementFormText, "ProductEditForm", "B3 Product Management must launch ProductEditForm.");
        RequireContains(managementFormText, "DeactivateSelectedProductAsync", "B3 Product Management must own the visible deactivate action.");
        RequireContains(managementFormText, "PromptDeactivateReason", "B3 Product deactivate must require an operator reason.");
        RequireContains(managementFormText, "Products.DeactivateReasonRequired", "B3 Product deactivate must surface required reason validation.");
        RequireContains(managementFormText, "_session.UserId", "B3 Product deactivate must use the authenticated user id.");
        RequireContains(managementFormText, "_presenter.DeactivateProductAsync", "B3 Product deactivate must go through the presenter.");
        RequireContains(managementFormText, "Add Product", "B3 Product Management must preserve Add Product.");

        RequireContains(createFormText, "ProductCreateForm", "B3 Product Maintenance must keep the existing add-product form.");
        RequireContains(createFormText, "CreateProductAsync", "B3 Product Maintenance must keep add-product presenter flow.");

        RequireContains(editFormText, "ProductEditForm", "B3 Product edit UI must exist.");
        RequireContains(editFormText, "Stock On Hand (read-only)", "B3 Product edit must label stock as read-only.");
        RequireContains(editFormText, "_stockOnHandTextBox.ReadOnly = true;", "B3 Product edit stock field must be read-only.");
        RequireContains(editFormText, "StockOnHand: null", "B3 Product edit must not save stock input from the UI.");
        RequireContains(editFormText, "UpdateProductAsync", "B3 Product edit must save through ProductManagementPresenter.");
        RequireDoesNotContain(editFormText, "StockOnHand: _stockOnHandTextBox.Text", "B3 Product edit must not pass stock textbox input into save.");

        RequireContains(presenterText, "LoadEditFormAsync", "B3 Product presenter must keep backend-driven edit form loading.");
        RequireContains(presenterText, "UpdateProductAsync", "B3 Product presenter must keep update workflow ownership.");
        RequireContains(presenterText, "DeactivateProductAsync", "B3 Product presenter must own deactivate workflow invocation.");
        RequireContains(presenterText, "_productWrites.UpdateAsync", "B3 Product update must go through product write repository.");
        RequireContains(presenterText, "_productWrites.DeactivateAsync", "B3 Product deactivate must go through product write repository.");
        RequireContains(presenterText, "FindDetailByIdAsync(request.ProductId", "B3 Product edit must re-read existing product detail before saving.");
        RequireContains(presenterText, "StockOnHand = existingDetail.StockOnHand", "B3 Product edit must preserve existing stock quantity.");
        RequireDoesNotContain(updateProductMethodText, "TryParseQuantity(request.StockOnHand", "B3 Product edit must not parse edited stock quantity.");

        RequireContains(shellText, "new ProductManagementForm(", "B3 Shell must still route product management through ProductManagementForm.");
        RequireContains(shellText, "_services.ProductWriteRepository", "B3 Shell must pass the product write repository through the presenter.");
        RequireContains(shellText, "_session);", "B3 Shell must pass the current session to product management.");

        RequireNoDirectSql(managementFormText, "Product Management form");
        RequireNoDirectSql(editFormText, "Product Edit form");
        RequireNoDirectSql(createFormText, "Product Create form");
        RequireNoForbiddenMarkers(managementFormText, "Product Management form");
        RequireNoForbiddenMarkers(editFormText, "Product Edit form");
        RequireNoForbiddenMarkers(presenterText, "Product presenter");

        RequireContains(
            coreSmokeText,
            "PosStabB3ProductMaintenanceChecks.Run();",
            "CoreSmoke must run POS-STAB-B3 product maintenance checks.");
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

    private static string SliceBetween(
        string text,
        string startMarker,
        string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"Required marker was not found: {startMarker}");
        }

        int end = text.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            throw new InvalidOperationException($"Required marker was not found: {endMarker}");
        }

        return text[start..end];
    }

    private static void RequireNoDirectSql(string text, string surface)
    {
        RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"B3 {surface} must not reference SQLite directly.");
        RequireDoesNotContain(text, "SqliteConnection", $"B3 {surface} must not create SQLite connections.");
        RequireDoesNotContain(text, "INSERT ", $"B3 {surface} must not issue direct insert SQL.");
        RequireDoesNotContain(text, "UPDATE ", $"B3 {surface} must not issue direct update SQL.");
        RequireDoesNotContain(text, "DELETE ", $"B3 {surface} must not issue direct delete SQL.");
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B3 {surface} must not introduce forbidden marker: {marker}");
        }
    }

    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "raw " + "USB",
            "scan" + "ner",
            "hard" + "ware"
        };
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

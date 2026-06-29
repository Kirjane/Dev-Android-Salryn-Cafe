namespace Salryn.Tests;

// UI-A1R-P3: Sales History / Void + Users Admin visual alignment guard.
// Proves the admin/history table screens adopt the SalrynUi ledger console system while
// preserving required behavior (void reason-required, users flows, self-deactivation guard)
// and without forbidden references.

public static class UiA1RAdminTablesVisualAlignmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string ui = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string sales = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Sales", "SalesHistoryForm.cs");
        string voidForm = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Sales", "SalesVoidReasonForm.cs");
        string users = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Users", "UsersAdminForm.cs");
        string presenter = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Users", "UsersAdminPresenter.cs");
        string coreSmoke = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        // Shared SalrynUi helpers used by P3.
        RequireContains(ui, "ApplyLedgerGridStyle", "SalrynUi must expose ledger grid styling that preserves bound columns.");
        RequireContains(ui, "GetStatusColor", "SalrynUi must expose a semantic status color helper.");
        RequireContains(ui, "ApplyStatusCellColor", "SalrynUi must expose a status cell coloring helper.");

        // Sales History uses SalrynUi visual system + ledger grid + status coloring.
        RequireContains(sales, "SalrynUi.", "SalesHistoryForm must use SalrynUi.");
        RequireContains(sales, "CreatePageHeader", "SalesHistoryForm must use SalrynUi page header.");
        RequireContains(sales, "CreateCommandBar", "SalesHistoryForm must group filters in a command bar.");
        RequireContains(sales, "ApplyLedgerGridStyle", "SalesHistoryForm grid must use ledger styling.");
        RequireContains(sales, "ApplyStatusCellColor", "SalesHistoryForm must color status values from loaded rows.");
        RequireContains(sales, "ConfigureDangerButton", "SalesHistoryForm Void Sale must be styled as a danger action.");
        RequireContains(sales, "CreateDangerSeparator", "SalesHistoryForm must visually separate the Void Sale action.");
        RequireContains(sales, "ConfigureFooterStatus", "SalesHistoryForm status must use a styled footer.");

        // Sales status semantics retained.
        RequireContains(sales, "Completed", "Sales status must retain Completed semantics.");
        RequireContains(sales, "Voided", "Sales status must retain Voided semantics.");
        RequireContains(sales, "SalrynPermissions.SalesVoid", "Sales void must stay permission-gated.");
        RequireContains(sales, "SalesVoidReasonForm", "Sales void must still use the reason prompt form.");

        // Void dialog uses SalrynUi/dialog helpers and stays reason-required + dangerous.
        RequireContains(voidForm, "SalrynUi.", "SalesVoidReasonForm must use SalrynUi.");
        RequireContains(voidForm, "ConfigureDangerButton", "SalesVoidReasonForm Void must be styled as a danger action.");
        RequireContains(voidForm, "Void reason is required.", "SalesVoidReasonForm must keep reason-required validation.");

        // Users Admin uses SalrynUi visual system + ledger grid + semantic actions.
        RequireContains(users, "SalrynUi.", "UsersAdminForm must use SalrynUi.");
        RequireContains(users, "CreatePageHeader", "UsersAdminForm must use SalrynUi page header.");
        RequireContains(users, "ApplyLedgerGridStyle", "UsersAdminForm grid must use ledger styling.");
        RequireContains(users, "ApplyStatusCellColor", "UsersAdminForm must color status values from loaded rows.");
        RequireContains(users, "ConfigureDangerButton", "UsersAdminForm Deactivate must be styled as a danger action.");
        RequireContains(users, "ConfigureWarningButton", "UsersAdminForm Reset Password must be styled as a warning action.");
        RequireContains(users, "ConfigureFooterStatus", "UsersAdminForm status must use a styled footer.");

        // Users flows remain present.
        RequireContains(users, "ResetPasswordAsync", "UsersAdminForm must keep the reset password flow.");
        RequireContains(users, "DeactivateUserAsync", "UsersAdminForm must keep the deactivate flow.");
        RequireContains(users, "ReactivateUserAsync", "UsersAdminForm must keep the reactivate flow.");
        RequireContains(users, "PromptDeactivateReason", "UsersAdminForm must keep the deactivate reason prompt.");

        // Self-deactivation guard retained in presenter.
        RequireContains(presenter, "userId == _session.UserId", "Users presenter must keep the self-deactivation guard.");
        RequireContains(presenter, "Users.SelfDeactivateBlocked", "Users presenter must keep the self-deactivation guard message.");
        RequireContains(presenter, "Users.DeactivateReasonRequired", "Users presenter must keep deactivate reason requirement.");

        // No direct SQLite or AppServices construction in the touched UI forms.
        foreach (string t in new[] { sales, voidForm, users })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "Sales/Users UI must not reference SQLite directly.");
            RequireNotContains(t, "SqliteConnection", "Sales/Users UI must not create SQLite connections.");
            RequireNotContains(t, "new SalrynAppServices", "Sales/Users forms must not construct AppServices.");
        }

        // No forbidden domain markers anywhere in the touched surfaces.
        foreach (string t in new[] { ui, sales, voidForm, users })
        {
            RequireNotContains(t, "CheckoutPresenter", "No checkout presenter references.");
            RequireNotContains(t, "RMO", "No RMO references.");
            RequireNotContains(t, "OfficialOutput", "No official output references.");
            RequireNotContains(t, "printer", "No printer references.");
            RequireNotContains(t, "scanner", "No scanner references.");
            RequireNotContains(t, "hardware", "No hardware references.");
            RequireNotContains(t, "PDF", "No PDF references.");
            RequireNotContains(t, "Crystal", "No Crystal references.");
            RequireNotContains(t, "ESC/POS", "No ESC/POS references.");
            RequireNotContains(t, "LAN support", "No LAN references.");
            RequireNotContains(t, "add LAN", "No add LAN references.");

            // Hard ban: no product image/photo/media/upload support.
            RequireNotContains(t, "Image", "No image support.");
            RequireNotContains(t, "Photo", "No photo support.");
            RequireNotContains(t, "Picture", "No picture support.");
            RequireNotContains(t, "Media", "No media support.");
            RequireNotContains(t, "Upload", "No upload support.");
            RequireNotContains(t, "blob", "No blob storage.");
            RequireNotContains(t, "file picker", "No file picker.");

            // No third-party UI toolkits.
            RequireNotContains(t, "DevExpress", "No third-party UI (DevExpress).");
            RequireNotContains(t, "Telerik", "No third-party UI (Telerik).");
            RequireNotContains(t, "Syncfusion", "No third-party UI (Syncfusion).");
            RequireNotContains(t, "Infragistics", "No third-party UI (Infragistics).");
        }

        // Guard must be wired into CoreSmoke.
        RequireContains(coreSmoke, "UiA1RAdminTablesVisualAlignmentChecks.Run();", "CoreSmoke must run the P3 admin tables visual alignment checks.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;
        foreach (string p in pathParts) path = Path.Combine(path, p);
        if (!File.Exists(path)) throw new InvalidOperationException("Required file not found: " + path);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }
}
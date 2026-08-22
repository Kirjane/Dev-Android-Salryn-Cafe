namespace Salryn.Tests;

// UI-A1R-P4: Settings + Backup + Audit/Error Log visual alignment guard.
// Proves the settings, backup, and audit screens adopt the SalrynUi ledger console system while
// preserving required behavior (settings persistence path, backup service path, audit read-only)
// and without forbidden references.

public static class UiA1RSettingsBackupAuditVisualAlignmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string ui = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string settings = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Settings", "StoreSettingsForm.cs");
        string backup = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Backup", "BackupForm.cs");
        string audit = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Audit", "AuditLogViewerForm.cs");
        string coreSmoke = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        // Shared SalrynUi helpers reused by P4.
        RequireContains(ui, "CreatePageHeader", "SalrynUi must expose a page header helper.");
        RequireContains(ui, "CreateCommandBar", "SalrynUi must expose a command bar helper.");
        RequireContains(ui, "CreateSectionTitle", "SalrynUi must expose a section title helper.");
        RequireContains(ui, "CreateFieldLabel", "SalrynUi must expose a field label helper.");
        RequireContains(ui, "ApplyLedgerGridStyle", "SalrynUi must expose ledger grid styling.");
        RequireContains(ui, "ConfigureFooterStatus", "SalrynUi must expose a footer status helper.");

        // Store Settings uses SalrynUi page/header/section/field/status helpers.
        RequireContains(settings, "SalrynUi.", "StoreSettingsForm must use SalrynUi.");
        RequireContains(settings, "SalrynUi.CreatePageHeader", "StoreSettingsForm must use a SalrynUi page header.");
        RequireContains(settings, "SalrynUi.CreateSectionTitle", "StoreSettingsForm must use a SalrynUi section title.");
        RequireContains(settings, "SalrynUi.CreateFieldLabel", "StoreSettingsForm must use SalrynUi field labels.");
        RequireContains(settings, "SalrynUi.ConfigureSuccessButton", "StoreSettingsForm Save must be styled as a success action.");
        RequireContains(settings, "SalrynUi.SetStatus", "StoreSettingsForm must use SalrynUi status helpers.");
        RequireContains(settings, "SalrynUi.ConfigureFooterStatus", "StoreSettingsForm status must use a styled footer.");
        // Settings persistence flow still calls the existing AppServices write repository path.
        RequireContains(settings, "_services.StoreSettingsWriteRepository.UpdateAsync", "StoreSettingsForm must save through the existing AppServices write repository.");
        RequireContains(settings, "StoreSettingsUpdateRequest", "StoreSettingsForm must keep the typed update request.");

        // Backup uses SalrynUi page/header/command/grid/status helpers.
        RequireContains(backup, "SalrynUi.", "BackupForm must use SalrynUi.");
        RequireContains(backup, "SalrynUi.CreatePageHeader", "BackupForm must use a SalrynUi page header.");
        RequireContains(backup, "SalrynUi.CreateCommandBar", "BackupForm must group actions in a command bar.");
        RequireContains(backup, "SalrynUi.ApplyLedgerGridStyle", "BackupForm history grid must use ledger styling.");
        RequireContains(backup, "SalrynUi.ConfigureSuccessButton", "BackupForm Create Backup must be styled as a success action.");
        RequireContains(backup, "SalrynUi.ConfigureFooterStatus", "BackupForm status must use a styled footer.");
        RequireContains(backup, "SalrynUi.SetStatus", "BackupForm must use SalrynUi status helpers.");
        // Backup still uses the existing AppServices backup creation path.
        RequireContains(backup, "_services.CreateLocalDatabaseBackup()", "BackupForm must keep the existing AppServices backup creation path.");
        RequireContains(backup, "Verify Backup", "BackupForm must keep backup verification before recovery.");

        // Audit uses SalrynUi page/header/command/grid/status helpers and keeps read-only MVP grids.
        RequireContains(audit, "SalrynUi.", "AuditLogViewerForm must use SalrynUi.");
        RequireContains(audit, "SalrynUi.CreatePageHeader", "AuditLogViewerForm must use a SalrynUi page header.");
        RequireContains(audit, "SalrynUi.CreateCommandBar", "AuditLogViewerForm must group filters in a command bar.");
        RequireContains(audit, "SalrynUi.ConfigureFooterStatus", "AuditLogViewerForm status must use a styled footer.");
        RequireContains(audit, "SalrynUi.SetStatus", "AuditLogViewerForm must use SalrynUi status helpers.");
        RequireContains(audit, "J20MvpUi.Grid()", "AuditLogViewerForm must keep read-only MVP grids.");
        // Audit and error grids both use ledger styling.
        RequireContains(audit, "SalrynUi.ApplyLedgerGridStyle(_auditGrid)", "AuditLogViewerForm audit grid must use ledger styling.");
        RequireContains(audit, "SalrynUi.ApplyLedgerGridStyle(_errorGrid)", "AuditLogViewerForm error grid must use ledger styling.");
        // Error severity colored only from already-loaded row values.
        RequireContains(audit, "SalrynUi.Colors.Danger", "AuditLogViewerForm must color error severity from loaded values.");
        RequireContains(audit, "J20MvpUi.StateText.NothingToShow", "AuditLogViewerForm must keep standardized empty-state copy.");

        // Audit remains read-only: read repositories only, no write/mutation paths in this form.
        RequireContains(audit, "_services.AuditReadRepository", "AuditLogViewerForm must read audit rows through AppServices.");
        RequireContains(audit, "_services.ErrorLogReadRepository", "AuditLogViewerForm must read error rows through AppServices.");
        RequireNotContains(audit, "WriteRepository", "AuditLogViewerForm must not use a write repository.");
        RequireNotContains(audit, "UpdateAsync", "AuditLogViewerForm must not mutate via update.");
        RequireNotContains(audit, "DeleteAsync", "AuditLogViewerForm must not mutate via delete.");
        RequireNotContains(audit, "InsertAsync", "AuditLogViewerForm must not mutate via insert.");

        // No direct SQLite or AppServices construction in the touched UI forms.
        foreach (string t in new[] { settings, backup, audit })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "Settings/Backup/Audit UI must not reference SQLite directly.");
            RequireNotContains(t, "SqliteConnection", "Settings/Backup/Audit UI must not create SQLite connections.");
            RequireNotContains(t, "new SalrynAppServices", "Settings/Backup/Audit forms must not construct AppServices.");
            RequireNotContains(t, "CreateAuditReadRepository", "Settings/Backup/Audit forms must not build repositories directly.");
        }

        // No forbidden domain markers anywhere in the touched surfaces.
        foreach (string t in new[] { ui, settings, backup, audit })
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
        RequireContains(coreSmoke, "UiA1RSettingsBackupAuditVisualAlignmentChecks.Run();", "CoreSmoke must run the P4 settings/backup/audit visual alignment checks.");
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

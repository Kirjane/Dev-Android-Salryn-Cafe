namespace Salryn.Tests;

public static class J22OfficialOutputTextFileVisibleSelectionSurfaceChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string form = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowForm.cs");
        string shellPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");
        string shellForm = Read(root, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string salesHistory = Read(root, "src", "Salryn.App.WinForms", "Features", "Sales", "SalesHistoryForm.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(form, "public sealed class OfficialOutputTextFileWorkflowForm : Form", "M1-D1 visible official output text-file form must exist.");
        RequireContains(form, "Text = \"Official Output Text File\"", "M1-D1 form title must be Official Output Text File.");
        RequireContains(form, "J20MvpUi.RootLayout", "M1-D1 form must reuse existing app UI layout style.");
        RequireContains(form, "J20MvpUi.Grid()", "M1-D1 form must use the existing grid style.");

        RequireContains(form, "_services.OfficialOutputTextFileSourceIdentity", "M1-D1 form must load candidates through the source identity boundary.");
        RequireContains(form, "OfficialOutputTextFileSourceIdentityListRequest", "M1-D1 form must list official evidence-backed source identities.");
        RequireContains(form, "OfficialEvidenceReadRequest", "M1-D1 form must require official evidence filters before listing.");
        RequireContains(form, "BuildSourceSelectionRequest", "M1-D1 form must convert a selected official evidence candidate into source selection.");
        RequireContains(form, "_services.OfficialOutputTextFileWorkflowUi", "M1-D1 form must run through the guarded workflow UI boundary.");
        RequireContains(form, "TryBuildTriggerRequest(out OfficialOutputTextFileWorkflowUiTriggerRequest? triggerRequest)", "M1-E1 form must build only the UI trigger request before running.");
        RequireContains(form, "TryRunAsync(triggerRequest)", "M1-D1 form must call the guarded workflow UI boundary.");
        RequireDoesNotContain(form, "TryGetPackageAsync(selectedSourceRequest", "M1-E1 form must not acquire package before delegating to workflow UI boundary.");
        RequireDoesNotContain(form, "_services.OfficialOutputTextFilePackageSupply", "M1-E1 form must not call package supply directly.");
        RequireDoesNotContain(form, "_services.OfficialOutputTextFileWorkflowReadiness", "M1-E1 form must not call readiness directly.");
        RequireDoesNotContain(form, "CheckReadiness", "M1-E1 form must leave readiness checks to workflow UI boundary.");

        RequireContains(form, "TryBuildEvidenceRequest", "M1-D1 form must have a source filter validation path.");
        RequireContains(form, "Machine profile id or registration scope is required", "M1-D1 form must require machine profile id or registration scope.");
        RequireContains(form, "Document kind filter is required", "M1-D1 form must require document kind.");
        RequireContains(form, "date range end must be after range start", "M1-D1 form must require a valid date range.");
        RequireContains(form, "GetSelectedCandidate()", "M1-D1 form must require selected candidate before running.");
        RequireContains(form, "Select an official evidence-backed candidate before running.", "M1-D1 form must block run without a selected official evidence candidate.");
        RequireContains(form, "Explicit output root path is required before running.", "M1-D1 form must require explicit output root path.");
        RequireContains(form, "An active app user session is required before running.", "M1-E1 form must require an active app user session.");
        RequireContains(form, "Terminal id is required from official evidence or Local Host context before running.", "M1-E1 form must require a terminal id.");

        RequireContains(form, "JournalEntryId", "M1-D1 form must display trusted journal entry identity.");
        RequireContains(form, "OfficialDocumentNumber", "M1-D1 form must display official document number.");
        RequireContains(form, "DocumentKind", "M1-D1 form must display document kind.");
        RequireContains(form, "BirMachineProfileId", "M1-D1 form must display BIR machine profile id.");
        RequireContains(form, "RegistrationScopeCode", "M1-D1 form must display registration scope when available.");
        RequireContains(form, "TransactionTimestamp", "M1-D1 form must display transaction timestamp when available.");
        RequireContains(form, "TerminalId", "M1-D1 form must display terminal id when available.");

        RequireContains(shellPresenter, "OfficialOutputTextFile", "M1-D1 shell must expose an explicit official output text-file module key.");
        RequireContains(shellPresenter, "OFFICIAL_OUTPUT_TEXT_FILE", "M1-D1 shell module key must be explicit.");
        RequireContains(shellPresenter, "\"Official Output Text File\"", "M1-D1 shell module title must be visible.");
        RequireContains(shellPresenter, "Can(SalrynPermissions.ReportsView)", "M1-D1 shell route must use Reports.View until a dedicated permission exists.");
        RequireContains(shellForm, "using Salryn.App.WinForms.Features.OfficialOutput;", "M1-D1 shell form must reference the OfficialOutput feature namespace.");
        RequireContains(shellForm, "OpenOfficialOutputTextFile", "M1-D1 shell must route to the official output text-file form.");
        RequireContains(shellForm, "new(_services, _session)", "M1-D1 shell must pass AppServices and the current session to the form.");

        RequireDoesNotContain(form, "SaleReadRepository", "M1-D1 form must not use Sales History sale reads as source evidence.");
        RequireDoesNotContain(form, "SalesHistory", "M1-D1 form must stay standalone, not Sales History integration.");
        RequireDoesNotContain(form, "ReceiptNo", "M1-D1 form must not use receipt number as loose source evidence.");
        RequireDoesNotContain(form, "Receipt" + "Preview", "M1-D1 form must not reuse receipt preview state.");
        RequireDoesNotContain(form, "Checkout" + "Presenter", "M1-D1 form must not couple to checkout presenter state.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileWorkflowForm", "M1-D1 must not wire the visible surface into checkout presenter.");
        RequireDoesNotContain(salesHistory, "OfficialOutputTextFileWorkflowForm", "M1-D1 must not wire the visible surface into Sales History.");

        foreach (string forbidden in ForbiddenMarkers())
        {
            RequireDoesNotContain(form, forbidden, "M1-D1 form must not contain forbidden marker: " + forbidden);
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFileVisibleSelectionSurfaceChecks.Run();", "CoreSmoke must run M1-D1 visible surface source guards.");
        RequireDoesNotContain(coreSmoke, "OfficialOutputTextFileWorkflowForm(", "CoreSmoke must not instantiate the visible form or execute output.");
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "File." + "Write";
        yield return "File" + "Stream";
        yield return "Stream" + "Writer";
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

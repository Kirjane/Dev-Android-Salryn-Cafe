namespace Salryn.Tests;

public static class PosStabA2ShiftCloseChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string formText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shifts",
            "CashierShiftGateForm.cs");
        string presenterText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shifts",
            "CashierShiftGatePresenter.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            formText,
            "_countedCashTextBox",
            "A2 shift close UI must collect counted cash.");
        RequireContains(
            formText,
            "Text = \"Close Shift\"",
            "A2 shift close UI must expose a Close Shift action.");
        RequireContains(
            formText,
            "await _presenter.CloseShiftAsync(",
            "A2 shift close UI must delegate close behavior to the presenter.");
        RequireContains(
            formText,
            "Counted cash must be a valid number.",
            "A2 shift close UI must validate counted cash input.");
        RequireContains(
            formText,
            "Variance is shown above.",
            "A2 shift close UI must show variance after close.");
        RequireContains(
            formText,
            "state.CanCloseShift",
            "A2 shift close UI must enable close only from presenter state.");
        RequireDoesNotContain(
            formText,
            "Microsoft.Data.Sqlite",
            "Shift close UI must not reference SQLite directly.");
        RequireDoesNotContain(
            formText,
            "SqliteConnection",
            "Shift close UI must not create SQLite connections.");

        RequireContains(
            presenterText,
            "public async Task<Result<CashierShiftGateViewModel>> CloseShiftAsync(",
            "A2 presenter must expose close-shift workflow.");
        RequireContains(
            presenterText,
            "_cashierSessions.CloseAsync(",
            "A2 presenter must use ICashierSessionService.CloseAsync.");
        RequireContains(
            presenterText,
            "new CloseCashierSessionRequest(",
            "A2 presenter must build the service close request.");
        RequireContains(
            presenterText,
            "Counted cash cannot be negative.",
            "A2 presenter must reject negative counted cash.");
        RequireContains(
            presenterText,
            "BuildClosedState",
            "A2 presenter must render the closed-shift read-back state.");
        RequireContains(
            presenterText,
            "Variance:",
            "A2 presenter must expose variance after close.");
        RequireDoesNotContain(
            presenterText,
            "Microsoft.Data.Sqlite",
            "Shift close presenter must not reference SQLite directly.");
        RequireDoesNotContain(
            presenterText,
            "SqliteConnection",
            "Shift close presenter must not create SQLite connections.");

        RequireContains(
            coreSmokeText,
            "PosStabA2ShiftCloseChecks.Run();",
            "CoreSmoke must run POS-STAB-A2 shift close checks.");
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

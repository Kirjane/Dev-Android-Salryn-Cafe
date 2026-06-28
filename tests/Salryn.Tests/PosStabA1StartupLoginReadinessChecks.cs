namespace Salryn.Tests;

public static class PosStabA1StartupLoginReadinessChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string authStartupText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Auth",
            "AuthStartupForm.cs");
        string programText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Program.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            authStartupText,
            "SetFieldVisible(_displayNameTextBox, true);",
            "First-run setup must show the display-name field container.");
        RequireContains(
            authStartupText,
            "SetFieldVisible(_confirmPasswordTextBox, true);",
            "First-run setup must show the confirm-password field container.");
        RequireContains(
            authStartupText,
            "SetFieldVisible(_displayNameTextBox, false);",
            "Normal login must hide the display-name field container.");
        RequireContains(
            authStartupText,
            "SetFieldVisible(_confirmPasswordTextBox, false);",
            "Normal login must hide the confirm-password field container.");
        RequireContains(
            authStartupText,
            "textBox.Parent is Control parent",
            "Login field visibility must target the field container, not only the text box.");
        RequireContains(
            authStartupText,
            "parent.Visible = visible;",
            "Login field visibility must hide the label/panel row with the text box.");
        RequireNotContains(
            authStartupText,
            "_displayNameTextBox.Visible = false;",
            "Normal login must not hide only the display-name text box.");
        RequireNotContains(
            authStartupText,
            "_confirmPasswordTextBox.Visible = false;",
            "Normal login must not hide only the confirm-password text box.");

        RequireContains(
            programText,
            "BuildStartupFailureMessage",
            "Program startup must centralize operator-facing database failure diagnostics.");
        RequireContains(
            programText,
            "ProgramData\\\\Salryn",
            "Startup diagnostics must mention the production database root.");
        RequireContains(
            programText,
            "use --dev or the Salryn.App.WinForms Development profile",
            "Startup diagnostics must point local operators to the development launch path.");
        RequireContains(
            programText,
            "ShowStartupFailure(BuildStartupFailureMessage",
            "Startup initialization failures must use the expanded diagnostic message.");

        RequireContains(
            coreSmokeText,
            "PosStabA1StartupLoginReadinessChecks.Run();",
            "CoreSmoke must run POS-STAB-A1 startup/login readiness checks.");
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

    private static void RequireNotContains(
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

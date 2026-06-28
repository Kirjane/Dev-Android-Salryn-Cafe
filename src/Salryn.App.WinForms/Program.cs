using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Auth;
using Salryn.App.WinForms.Features.Shell;

namespace Salryn.App.WinForms;

// Kirch file guide: App entry point.
// UI location: starts the WinForms process before the user sees any screen.
// Frontend note: do not put styling here; forms own layout and visual polish.

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        SalrynAppServices services;

        try
        {
            services = SalrynAppLaunchModeResolver.CreateServices(args);
        }
        catch (Exception ex)
        {
            ShowStartupFailure(BuildStartupFailureMessage(ex.Message));
            return;
        }

        var initializationResult = services.DatabaseInitialization.InitializeDatabase();

        if (initializationResult.IsFailure)
        {
            ShowStartupFailure(BuildStartupFailureMessage(initializationResult.Error));
            return;
        }

        using AuthStartupForm authForm = new(services.Authentication);

        DialogResult authResult = authForm.ShowDialog();

        if (authResult != DialogResult.OK || authForm.AuthenticatedSession is null)
        {
            return;
        }

        Application.Run(new SalrynShellForm(
            services,
            authForm.AuthenticatedSession));
    }

    private static string BuildStartupFailureMessage(string detail)
    {
        return string.Join(
            Environment.NewLine,
            "SALRYN could not start the local database.",
            string.Empty,
            detail,
            string.Empty,
            "Check that the SALRYN database folder exists and is writable by the current Windows user.",
            "Production path: ProgramData\\Salryn",
            "Development launch: use --dev or the Salryn.App.WinForms Development profile.");
    }

    private static void ShowStartupFailure(string message)
    {
        MessageBox.Show(
            message,
            "SALRYN Startup Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

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

        SalrynAppServices services = SalrynAppLaunchModeResolver.CreateServices(args);

        var initializationResult = services.DatabaseInitialization.InitializeDatabase();

        if (initializationResult.IsFailure)
        {
            MessageBox.Show(
                initializationResult.Error,
                "SALRYN Startup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

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
}

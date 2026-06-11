using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Auth;
using Salryn.App.WinForms.Features.Shell;

namespace Salryn.App.WinForms;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        SalrynAppServices services = SalrynAppServices.CreateProduction();

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

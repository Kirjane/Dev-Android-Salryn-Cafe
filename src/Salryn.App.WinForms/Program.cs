using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;

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

        Result<CurrentAppSession?> sessionResult =
            AppSessionBootstrapper.LoadFirstActiveUserAsync(
                services.UserReadRepository,
                services.UserPermissionReadRepository).GetAwaiter().GetResult();

        if (sessionResult.IsFailure)
        {
            MessageBox.Show(
                sessionResult.Error,
                "SALRYN Session Load Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return;
        }

        Application.Run(new SalrynShellForm(
            services,
            sessionResult.Value));
    }
}


using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Checkout;

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

        CheckoutPresenter checkoutPresenter = new(services.CheckoutProductSearch);

        Application.Run(new CheckoutShellForm(checkoutPresenter));
    }
}

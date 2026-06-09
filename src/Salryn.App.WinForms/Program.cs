using Salryn.App.WinForms.Composition;

namespace Salryn.App.WinForms;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        SalrynAppServices services = SalrynAppServices.CreateProduction();
        Application.Run(new Form1(services));
    }
}

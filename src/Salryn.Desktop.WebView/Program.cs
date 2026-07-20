using Microsoft.Web.WebView2.Core;

namespace Salryn.Desktop.WebView;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!IsWebView2RuntimeAvailable())
        {
            MessageBox.Show(
                "Salryn POS could not start because Microsoft Edge WebView2 Runtime is missing or damaged.\n" +
                "Re-run the Salryn installer or install WebView2 Runtime, then reopen Salryn.",
                "Salryn POS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var apiManager = new LocalApiProcessManager();

        bool apiReady;
        try
        {
            apiReady = apiManager
                .EnsureRunningAsync(TimeSpan.FromSeconds(40))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            apiReady = false;
        }

        if (!apiReady)
        {
            string apiMessage = DevHostConfig.IsInstalledLayout()
                ? "Salryn POS could not start its local service.\n\n" +
                  "Close any other Salryn windows, then open Salryn POS again from the Start Menu or desktop shortcut.\n" +
                  "If the problem continues, re-run the Salryn installer or contact salryn.info@gmail.com."
                : "The Salryn local API at " + DevHostConfig.ApiBaseUrl + " is not responding.\n\n" +
                  "Start it manually with:\n" +
                  "dotnet run --project .\\src\\Salryn.Api.Local\\Salryn.Api.Local.csproj -- --dev\n\n" +
                  "Open the web view anyway?";

            if (DevHostConfig.IsInstalledLayout())
            {
                MessageBox.Show(
                    apiMessage,
                    "Salryn POS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                apiManager.Dispose();
                return;
            }

            DialogResult choice = MessageBox.Show(
                apiMessage,
                "Salryn POS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (choice != DialogResult.Yes)
            {
                apiManager.Dispose();
                return;
            }
        }

        string webUrl = DevHostConfig.ResolveWebUrl(out string? configurationRejection);

        if (configurationRejection is not null)
        {
            MessageBox.Show(
                configurationRejection + "\n\nFalling back to " + DevHostConfig.DefaultViteUrl + ".",
                "Salryn POS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Application.Run(new MainForm(webUrl, apiManager));
    }

    private static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            // Native WebView2Loader.dll missing from the install layout (packaging defect).
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

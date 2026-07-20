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
                "Microsoft Edge WebView2 Runtime is required to run Salryn POS.\n" +
                "Install the runtime, then restart Salryn.",
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
            DialogResult choice = MessageBox.Show(
                "The Salryn local API at " + DevHostConfig.ApiBaseUrl + " is not responding.\n\n" +
                "Start it manually with:\n" +
                "dotnet run --project .\\src\\Salryn.Api.Local\\Salryn.Api.Local.csproj -- --dev\n\n" +
                "Open the web view anyway?",
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
        catch (Exception)
        {
            return false;
        }
    }
}

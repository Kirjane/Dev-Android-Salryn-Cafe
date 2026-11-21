using Microsoft.Web.WebView2.Core;

namespace Salryn.Desktop.WebView;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: DevHostConfig.DesktopMutexName,
            out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Salryn Cafe is already running on this workstation.",
                DevHostConfig.ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!IsWebView2RuntimeAvailable())
        {
            MessageBox.Show(
                "Salryn Cafe could not start because Microsoft Edge WebView2 Runtime is missing or damaged.\n" +
                "Re-run the Salryn Cafe installer or install WebView2 Runtime, then reopen Salryn Cafe.",
                DevHostConfig.ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!DevHostConfig.TryValidateInstalledBundle(null, out string? missingBundleMessage))
        {
            MessageBox.Show(
                missingBundleMessage,
                DevHostConfig.ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            bool installedPackage = DevHostConfig.IsInstalledPackageContext();
            string apiMessage = installedPackage
                ? "Salryn Cafe could not start or verify its local service (ProductId=Salryn.Cafe).\n\n" +
                  "Close any other Salryn Cafe windows, then open Salryn Cafe again from the Start Menu or desktop shortcut.\n" +
                  "If the problem continues, re-run Salryn-Cafe-Setup.exe or contact salryn.info@gmail.com."
                : "The Salryn Cafe local API at " + DevHostConfig.ResolveApiBaseUrl() + " is not responding with ProductId=Salryn.Cafe.\n\n" +
                  "Start it manually with:\n" +
                  "dotnet run --project .\\src\\Salryn.Api.Local\\Salryn.Api.Local.csproj -- --dev\n\n" +
                  "Open the web view anyway?";

            if (installedPackage)
            {
                MessageBox.Show(
                    apiMessage,
                    DevHostConfig.ProductDisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                apiManager.Dispose();
                return;
            }

            DialogResult choice = MessageBox.Show(
                apiMessage,
                DevHostConfig.ProductDisplayName,
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
                configurationRejection + "\n\nFalling back to " + webUrl + ".",
                DevHostConfig.ProductDisplayName,
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

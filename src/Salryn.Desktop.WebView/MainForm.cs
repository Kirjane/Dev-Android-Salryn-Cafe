using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Salryn.Desktop.WebView;

/// <summary>
/// The native desktop window that hosts the Salryn web frontend inside WebView2.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly string _webUrl;
    private readonly LocalApiProcessManager _apiManager;
    private readonly WebView2 _webView;
    private bool _navigationWarningShown;

    public MainForm(string webUrl, LocalApiProcessManager apiManager)
    {
        _webUrl = webUrl;
        _apiManager = apiManager;

        Text = "Salryn Ledger Console";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1366, 850);
        BackColor = Color.FromArgb(15, 27, 45);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
        };

        Controls.Add(_webView);
        Load += OnLoadAsync;
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Salryn",
                "WebView2");

            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder)
                    .ConfigureAwait(true);

            await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            _webView.CoreWebView2.Navigate(_webUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to start the Salryn web view.\n\n{ex.Message}",
                "Salryn Ledger Console",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (LocalWebOriginPolicy.IsAllowedNavigationUri(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        ShowBlockedNavigationWarning(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        ShowBlockedNavigationWarning(e.Uri);
    }

    private void ShowBlockedNavigationWarning(string uri)
    {
        if (_navigationWarningShown)
        {
            return;
        }

        _navigationWarningShown = true;

        MessageBox.Show(
            this,
            "Salryn blocked navigation to a non-local URL.\n\n" +
            "Only localhost frontend/API origins on ports 5173 and 5299 are allowed.\n\n" +
            "Blocked: " + uri,
            "Salryn Ledger Console",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _apiManager.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            }

            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}

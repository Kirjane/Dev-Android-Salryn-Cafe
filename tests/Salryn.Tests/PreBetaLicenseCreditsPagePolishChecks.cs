namespace Salryn.Tests;

/// <summary>
/// PRE-BETA: licensing + credits two-card polish (Host LicensePage + TerminalActivationPage).
/// Source guards only — does not change crypto or machine binding.
/// </summary>
public static class PreBetaLicenseCreditsPagePolishChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string terminalPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string credits = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx"));

        RequireContains(licensePage, "ProductCreditsCard", "Host license page must render product credits card.");
        RequireContains(licensePage, "lg:grid-cols-2", "Host license page must use two-column desktop layout.");
        RequireContains(licensePage, "Import Signed Host License JSON", "Host import card title required.");
        RequireContains(licensePage, "Copy Host Machine Code", "Host Machine Code copy required.");
        RequireContains(licensePage, "Salryn generates this Machine Code. LicensingOps signs", "Lane B machine-code rule retained.");
        RequireContains(licensePage, "terminal users do not need a Host License ID", "Terminal users must not be asked for Host License ID.");
        RequireContains(licensePage, "Status: Active", "Active license status retained.");
        RequireContains(licensePage, "Show support details", "Host support details must stay collapsed by default.");
        RequireNotContains(licensePage, "how many LAN terminals", "No terminal count copy.");
        RequireNotContains(licensePage, "cloud activation", "No cloud activation claim.");
        RequireNotContains(licensePage, "mock sample", "Build type must not say mock sample.");
        RequireNotContains(licensePage, "RMO", "Host license page must not expose RMO.");
        RequireNotContains(licensePage, "eSales", "Host license page must not expose eSales.");
        RequireNotContains(licensePage, "BIR-approved", "Host license page must not claim BIR-approved.");

        RequireContains(terminalPage, "ProductCreditsCard", "Terminal activation must render product credits card.");
        RequireContains(terminalPage, "lg:grid-cols-2", "Terminal activation must use two-column desktop layout.");
        RequireContains(terminalPage, "Copy Terminal Machine Code", "Terminal Machine Code copy required.");
        RequireContains(terminalPage, "do not need a Host License ID", "Terminal path must not require Host License ID.");
        RequireContains(terminalPage, "Import Signed Terminal License JSON", "Terminal import title required.");
        RequireContains(terminalPage, "Host licenses cannot activate a terminal", "Host license rejection copy retained.");
        RequireContains(terminalPage, "Show support details", "Terminal support details collapsed by default.");
        RequireNotContains(terminalPage, "Include your store name, Host License ID", "Terminal must not ask for Host License ID as a task.");
        RequireNotContains(terminalPage, "RMO", "Terminal page must not expose RMO.");

        RequireContains(credits, "Repository-backed beta build", "Build type must be repository-backed beta build.");
        RequireContains(credits, "by Kirjane Labs", "Credits must show Salryn by Kirjane Labs.");
        RequireContains(credits, "Licensing & Credits", "Credits title required.");
        RequireContains(credits, "Publisher", "Publisher label required.");
        RequireContains(credits, "Kirjane Labs", "Publisher name required.");
        RequireContains(credits, "Products of Kirjane Labs", "Products list header required.");
        RequireContains(credits, "Paul Gamayot", "Business Lead name required.");
        RequireContains(credits, "Business Lead", "Business Lead role required.");
        RequireContains(credits, "Kirch Ivan Balite", "Technology Lead name required.");
        RequireContains(credits, "Technology Lead", "Technology Lead role required.");
        RequireContains(credits, "Jesse Edwin Culas", "Marketing Lead name required.");
        RequireContains(credits, "Marketing Lead", "Marketing Lead role required.");
        RequireContains(credits, "James Karl Tara", "Business Development name required.");
        RequireContains(credits, "Business Development", "Business Development role required.");
        RequireContains(credits, "Osiris Kedigadash Palac", "Dev Ops Lead name required.");
        RequireContains(credits, "Dev Ops Lead", "Dev Ops Lead role required.");
        RequireContains(credits, "© 2026 Kirjane Labs", "Copyright footer required.");
        RequireContains(credits, "Salryn is a local POS product designed for controlled client use", "Footer product statement required.");
        RequireNotContains(credits, "mock sample", "Credits must not say mock sample.");
        RequireNotContains(credits, "Software Development", "Do not rename Technology Lead to Software Development.");
        RequireNotContains(credits, "RMO", "Credits must not expose RMO.");
        RequireNotContains(credits, "eSales", "Credits must not expose eSales.");
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }
}

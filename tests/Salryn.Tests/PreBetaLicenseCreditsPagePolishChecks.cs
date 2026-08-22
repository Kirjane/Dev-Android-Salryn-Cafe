namespace Salryn.Tests;

/// <summary>
/// PRE-BETA: licensing + credits two-card polish (Host LicensePage + TerminalActivationPage).
/// Credits card is credits-first (no admin metadata boxes). Source guards only.
/// </summary>
public static class PreBetaLicenseCreditsPagePolishChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string terminalPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string credits = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx"));

        RequireContains(licensePage, "Licensing &amp; Credits", "Host license page exposes credits.");
        RequireContains(licensePage, "lg:grid-cols-2", "Host license page must use two-column desktop layout.");
        RequireContains(licensePage, "Import Signed Host License JSON", "Host import card title required.");
        RequireContains(licensePage, "Copy Machine Code", "Host Machine Code copy required.");
        RequireContains(licensePage, "Salryn generates this Machine Code", "Lane B machine-code rule retained.");
        RequireContains(licensePage, "Terminal users do not need a Host License ID", "Terminal users must not be asked for Host License ID.");
        RequireContains(licensePage, "Status: Active", "Active license status retained.");
        RequireContains(licensePage, "Show technical details", "Host technical details must stay collapsed by default.");
        RequireNotContains(licensePage, "how many LAN terminals", "No terminal count copy.");
        RequireContains(licensePage, "cloud activation is not required", "Cloud activation boundary.");
        RequireNotContains(licensePage, "mock sample", "Build type must not say mock sample.");
        RequireNotContains(licensePage, "RMO", "Host license page must not expose RMO.");
        RequireContains(licensePage, "eSales transmission: current beta status: not included", "eSales beta boundary.");
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
        // LAN end-user path: no large metadata labels, no Store ID / Host License ID as tasks
        RequireNotContains(terminalPage, "Products of Kirjane Labs", "Terminal page must not show Products list metadata.");
        RequireNotContains(terminalPage, "Build Type", "Terminal page must not show Build Type card.");
        RequireNotContains(terminalPage, "find Host License ID", "Terminal users must not be asked to find Host License ID.");
        RequireNotContains(terminalPage, "find Store ID", "Terminal users must not be asked to find Store ID.");
        RequireNotContains(terminalPage, "you need a Host License ID", "Terminal path must not require Host License ID.");
        RequireNotContains(terminalPage, "you need a Store ID", "Terminal path must not require Store ID.");

        // Credits identity (shared card — Host + Terminal)
        RequireContains(credits, "SALRYN", "Credits must show SALRYN.");
        RequireContains(credits, "by Kirjane Labs", "Credits must show by Kirjane Labs.");
        RequireContains(credits, "Licensing & Credits", "Credits title required.");
        RequireContains(credits, "Products of Kirjane Labs: Salryn", "Product identity line required.");
        RequireContains(credits, "Credits", "Credits section heading required.");

        // People / hierarchy
        RequireContains(credits, "Paul Gamayot", "Business Lead name required.");
        RequireContains(credits, "Business Lead", "Business Lead role required.");
        RequireContains(credits, "Kirch Ivan Balite", "Technology Lead name required.");
        RequireContains(credits, "Technology Lead", "Technology Lead role required.");
        RequireContains(credits, "Jesse Edwin Culas", "Marketing Lead name required.");
        RequireContains(credits, "Marketing Lead", "Marketing Lead role required.");
        RequireContains(credits, "James Karl Tara", "Business Development name required.");
        RequireContains(credits, "Business Development", "Business Development role required.");
        RequireContains(credits, "Osiris Kedigadash Palac", "Dev Ops Lead name required.");
        RequireContains(credits, "DevOps Lead", "DevOps Lead role required.");
        RequireContains(credits, "salryn.info@gmail.com", "Support email required.");
        RequireContains(credits, "© 2026 Kirjane Labs", "Copyright footer required.");

        // Product identity stays client-facing without repository/build metadata.
        RequireContains(credits, "Salryn by Kirjane Labs", "Client-facing product footer retained.");
        RequireNotContains(credits, "Build Type", "Build Type must not be a prominent card label.");
        RequireNotContains(credits, ">Publisher<", "Publisher must not be a large metadata label.");
        RequireNotContains(credits, ">Product<", "Product must not be a large metadata label.");
        RequireNotContains(credits, "Repository-backed beta build", "Repository build wording removed.");
        RequireNotContains(credits, "mock sample", "Credits must not say mock sample.");
        RequireNotContains(credits, "Software Development", "Do not rename Technology Lead to Software Development.");
        RequireNotContains(credits, "RMO", "Credits must not expose RMO.");
        RequireNotContains(credits, "eSales", "Credits must not expose eSales.");

        // Host Terminal Management — cleaned request language (admin IDs allowed)
        string hostLan = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
        RequireContains(hostLan, "License desk details", "Terminal Management collapsed support details required.");
        RequireContains(hostLan, "Copy Terminal License Request", "Terminal Management copy request button required.");
        RequireContains(hostLan, "Terminal licenses are issued from the Host license context", "Terminal Management must state Host-context issuance.");
        RequireContains(hostLan, "Copy this request for the license desk", "Terminal Management must direct the request to the license desk.");
        RequireContains(hostLan, "Each terminal device needs its own Terminal license", "Terminal Management must state one license per terminal device.");
        RequireContains(hostLan, "Host License ID", "Admin request block may show Host License ID.");
        RequireContains(hostLan, "Host Machine Code", "Admin request block must show Host Machine Code.");
        RequireNotContains(hostLan, "RMO", "Terminal Management must not expose RMO.");
        RequireNotContains(hostLan, "eSales", "Terminal Management must not expose eSales.");
        RequireNotContains(hostLan, "BIR-approved", "Terminal Management must not claim BIR-approved.");
        RequireNotContains(hostLan, "Products of Kirjane Labs", "Terminal Management must not duplicate credits products list.");
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

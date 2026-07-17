namespace Salryn.Tests;

/// <summary>
/// PRE-I Host license landing: points at minimalist rebuild contract (obsolete dark SaaS markers removed).
/// Detailed design lock coverage lives in pre-i-license-page-minimalist-rebuild.
/// </summary>
public static class PreILicenseLandingSellUpgradeChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string terminalPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string credits = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx"));

        // Minimalist Host landing (design lock)
        RequireContains(licensePage, "Salryn", "Host landing wordmark.");
        RequireContains(licensePage, "by Kirjane Labs", "Host landing identity.");
        RequireContains(licensePage, "Activate Salryn", "Primary activation title.");
        RequireContains(licensePage, "Import license", "Primary import CTA.");
        RequireContains(licensePage, "Copy Machine Code", "Machine code action.");
        RequireContains(licensePage, "This device needs a signed local license before setup can continue.", "Fresh subtitle.");
        RequireContains(licensePage, "Salryn runs locally on this device.", "Local-first body.");
        RequireContains(licensePage, "Salryn is activated", "Active title.");
        RequireContains(licensePage, "salryn.info@gmail.com", "Infodesk contact.");
        RequireContains(licensePage, "I understand and accept these beta license terms.", "Terms acceptance.");
        RequireContains(licensePage, "licenseText.trim().length > 0 && termsAccepted", "Install remains terms and document gated.");
        RequireContains(licensePage, "Import Signed Host License JSON", "Signed Host import step.");
        RequireContains(licensePage, "Show technical details", "Technical details collapsed.");
        RequireContains(licensePage, "Technology Lead: Kirch Ivan Balite", "Quiet footer credit.");
        RequireContains(licensePage, "bg-[#F4F5F7]", "Light minimalist page background.");

        // Rejected design must stay gone
        RequireNotContains(licensePage, "What Salryn offers", "No product marketing wall.");
        RequireNotContains(licensePage, "Sell clearly", "No value-column marketing.");
        RequireNotContains(licensePage, "bg-[radial-gradient", "No CSS radial launch artwork.");
        RequireNotContains(licensePage, "Sparkles", "No sparkles.");
        RequireNotContains(licensePage, "Already have a license? Import license", "Old CTA removed.");
        RequireNotContains(licensePage, "mock sample", "No sample-build copy.");
        RequireNotContains(licensePage, "Products of Kirjane Labs", "No products metadata block.");
        RequireNotContains(licensePage, "Build Type", "No prominent build metadata.");
        RequireNotContains(licensePage, "cloud activation", "No online activation claim.");
        RequireNotContains(licensePage, "BIR-ready", "No tax readiness claim.");
        RequireNotContains(licensePage, "official receipt accreditation", "No receipt accreditation claim.");
        RequireNotContains(licensePage, "find Store ID", "No Store ID task.");
        RequireNotContains(licensePage, "find Host License ID", "No Host License ID task.");

        // Shared credits component still exists for Terminal (not required on Host minimalist page)
        RequireContains(credits, "Licensing & Credits", "Integrated trust section heading.");
        RequireContains(credits, "A Kirjane Labs product", "Product identity line.");
        RequireContains(credits, "Paul Gamayot", "Business Lead credit.");
        RequireContains(credits, "James Karl Tara", "Business Development credit.");
        RequireContains(credits, "Kirch Ivan Balite", "Technology Lead credit.");
        RequireContains(credits, "Osiris Kedigadash Palac", "Dev Ops Lead credit.");
        RequireContains(credits, "Jesse Edwin Culas", "Marketing Lead credit.");
        RequireNotContains(credits, "Products of Kirjane Labs", "Credits stay free of product metadata boxes.");

        RequireContains(terminalPage, "<ProductCreditsCard />", "Terminal keeps the compatible default shared credits card.");
        RequireContains(terminalPage, "Import Signed Terminal License JSON", "Terminal import behavior remains present.");
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

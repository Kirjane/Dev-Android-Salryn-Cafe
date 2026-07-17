namespace Salryn.Tests;

/// <summary>
/// PRE-I: premium Host license landing, honest product copy, preserved activation gates, and integrated credits.
/// Source guard only; licensing runtime behavior remains covered by the existing licensing checks.
/// </summary>
public static class PreILicenseLandingSellUpgradeChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string terminalPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string credits = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx"));

        RequireContains(licensePage, "SALRYN", "Host landing wordmark.");
        RequireContains(licensePage, "by Kirjane Labs", "Host landing identity.");
        RequireContains(licensePage, "Offline-first local POS", "Bounded local POS positioning.");
        RequireContains(licensePage, "Less manual store work", "Product-facing hero value.");
        RequireContains(licensePage, "What Salryn offers", "Product value section.");
        RequireContains(licensePage, "Checkout and receipts", "Checkout feature chip.");
        RequireContains(licensePage, "Portable backup files", "Backup feature chip.");
        RequireContains(licensePage, "LAN Cashier/Manager terminals", "LAN feature chip.");
        RequireContains(licensePage, "Keyboard-wedge barcode workflow", "Bounded barcode feature chip.");
        RequireContains(licensePage, "Windows print queue receipts", "Bounded receipt feature chip.");
        RequireContains(licensePage, "Activate Salryn", "Primary activation CTA.");
        RequireContains(licensePage, "Import existing license", "Secondary import CTA.");
        RequireContains(licensePage, "Refresh license status", "Integrated utility action.");
        RequireContains(licensePage, "Ready in three steps", "Activation guidance.");
        RequireContains(licensePage, "Copy Host Machine Code", "Machine code action.");
        RequireContains(licensePage, "salryn.info@gmail.com", "Infodesk contact.");
        RequireContains(licensePage, "I have read and accept the Salryn Activation Terms", "Terms acceptance copy.");
        RequireContains(licensePage, "licenseText.trim().length > 0 && termsAccepted", "Install remains terms and document gated.");
        RequireContains(licensePage, "Import Signed Host License JSON", "Signed Host import step.");
        RequireContains(licensePage, "License Active", "Premium active state.");
        RequireContains(licensePage, "Show support details", "Support details collapsed.");
        RequireContains(licensePage, "ProductCreditsCard variant=\"integrated\"", "Credits integrated into landing rhythm.");
        RequireContains(licensePage, "bg-[radial-gradient", "CSS-only landing artwork.");

        RequireNotContains(licensePage, "Already have a license? Import license", "Old CTA removed.");
        RequireNotContains(licensePage, "mock sample", "No sample-build copy.");
        RequireNotContains(licensePage, "Products of Kirjane Labs", "No products metadata block.");
        RequireNotContains(licensePage, "Build Type", "No prominent build metadata.");
        RequireNotContains(licensePage, "cloud activation", "No online activation claim.");
        RequireNotContains(licensePage, "BIR-ready", "No tax readiness claim.");
        RequireNotContains(licensePage, "official receipt accreditation", "No receipt accreditation claim.");
        RequireNotContains(licensePage, "ESC/POS", "No protocol support claim in normal activation copy.");
        RequireNotContains(licensePage, "cash drawer", "No drawer support claim.");
        RequireNotContains(licensePage, "cutter", "No cutter support claim.");
        RequireNotContains(licensePage, "hardware bundle", "No bundle claim.");
        RequireNotContains(licensePage, "find Store ID", "No Store ID task.");
        RequireNotContains(licensePage, "find Host License ID", "No Host License ID task.");

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

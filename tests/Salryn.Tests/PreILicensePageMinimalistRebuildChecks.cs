namespace Salryn.Tests;

/// <summary>
/// PRE-I License page minimalist rebuild: design-lock copy, no AI SaaS chrome, terms + replace gates.
/// </summary>
public static class PreILicensePageMinimalistRebuildChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string license = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));

        // Forbidden old design markers
        RequireNotContains(license, "LandingBackdrop", "No landing backdrop component.");
        RequireNotContains(license, "radial-gradient", "No multi-stop radial gradient hero.");
        RequireNotContains(license, "bg-gradient-to-r", "No gradient text/CTA chrome.");
        RequireNotContains(license, "backdrop-blur", "No glass blur panels.");
        RequireNotContains(license, "Sparkles", "No sparkles icons.");
        RequireNotContains(license, "What Salryn offers", "No feature showcase section.");
        RequireNotContains(license, "Sell clearly", "No marketing value columns.");
        RequireNotContains(license, "HOST_FEATURES", "No feature pill wall.");
        RequireNotContains(license, "ACTIVATION_STEPS", "No three-step marketing rail.");
        RequireNotContains(license, "bg-[#050815]", "No dark AI launch stage background.");

        // Locked minimalist copy
        RequireContains(license, "Activate Salryn", "Fresh title.");
        RequireContains(license, "This device needs a signed local license before setup can continue.", "Fresh subtitle.");
        RequireContains(license, "Salryn runs locally on this device.", "Fresh body.");
        RequireContains(license, "Import license", "Primary CTA.");
        RequireContains(license, "Copy Machine Code", "Secondary machine code action.");
        RequireContains(license, "Salryn is activated", "Active title.");
        RequireContains(license, "This Host is licensed for local use on this device.", "Active body.");
        RequireContains(license, "Replace license", "Replace CTA.");
        RequireContains(license, "bg-[#F4F5F7]", "Light page background token.");
        RequireContains(license, "max-w-[920px]", "Centered max width ~920px.");

        // Machine code + support + footer
        RequireContains(license, "machineCode", "Machine code binding present.");
        RequireContains(license, "salryn.info@gmail.com", "Support email retained.");
        RequireContains(license, "Salryn by Kirjane Labs", "Footer product line.");
        RequireContains(license, "Technology Lead: Kirch Ivan Balite", "Footer technology lead.");
        RequireContains(license, "Show technical details", "Technical IDs collapsed by default.");
        RequireContains(license, "showSupportDetails", "Collapsed technical details state.");

        // Terms sections
        RequireContains(license, "Local license", "Terms: Local license.");
        RequireContains(license, "Device binding", "Terms: Device binding.");
        RequireContains(license, "Data ownership", "Terms: Data ownership.");
        RequireContains(license, "Backups", "Terms: Backups.");
        RequireContains(license, "Hardware limits", "Terms: Hardware limits.");
        RequireContains(license, "Receipts and compliance", "Terms: Receipts and compliance.");
        RequireContains(license, "LAN terminals", "Terms: LAN terminals.");
        RequireContains(license, "Support boundaries", "Terms: Support boundaries.");
        RequireContains(license, "Beta acceptance", "Terms: Beta acceptance.");
        RequireContains(license, "I understand and accept these beta license terms.", "Terms checkbox.");

        // Behavior gates
        RequireContains(license, "canInstallWizard = licenseText.trim().length > 0 && termsAccepted && !busy", "First install terms gate.");
        RequireContains(license, "canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && termsAccepted && !busy", "Replace gate.");
        RequireContains(license, "step === \"import\" ? \"terms\" : step", "Import routes through terms.");
        RequireContains(license, "disabled={!canInstallReplace}", "Replace install disabled without full gate.");
        RequireContains(license, "I have re-read and accept the Salryn Activation Terms for this replacement.", "Replace terms re-ack.");
        RequireContains(license, "I understand this replaces the active local license for this device.", "Replace consequence.");
        RequireContains(license, "Import Signed Host License JSON", "Host import title retained.");
        RequireContains(license, "This Terminal license cannot unlock Host first-run setup", "Host vs Terminal separation.");
        RequireContains(license, "Each LAN terminal needs its own Terminal license", "One license per terminal.");

        RequireNotContains(license, "find Host License ID", "No Host License ID task.");
        RequireNotContains(license, "find Store ID", "No Store ID task.");
        RequireNotContains(license, "cloud activation", "No cloud activation.");
        RequireNotContains(license, "BIR-ready", "No BIR-ready claim.");
        RequireNotContains(license, "official receipt accreditation", "No accreditation claim.");
        RequireNotContains(license, "ProductCreditsCard", "Host page uses quiet footer, not competing credits card.");
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

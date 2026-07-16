namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-H: sealed beta capability matrix exists and contradicts forbidden claim language.
/// </summary>
public static class PreBetaHCapabilityMatrixSealChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string matrix = File.ReadAllText(Path.Combine(root, "00-HANDOFF", "BETA_CAPABILITY_MATRIX.md"));
        string deployment = File.ReadAllText(Path.Combine(root, "09-DEPLOYMENT", "DEPLOYMENT_LOCK.md"));
        string hardware = File.ReadAllText(Path.Combine(root, "06-HARDWARE", "HARDWARE_LOCK.md"));
        string v1 = File.ReadAllText(Path.Combine(root, "00-HANDOFF", "V1_BUILD_ORDER.md"));
        string feature = File.ReadAllText(Path.Combine(root, "01-FEATURE-LOCK", "FEATURE_LOCK.md"));

        RequireContains(matrix, "SUPPORTED", "Matrix must define SUPPORTED.");
        RequireContains(matrix, "CODE-PROVEN", "Matrix must define CODE-PROVEN.");
        RequireContains(matrix, "HARDWARE-PROOF-PENDING", "Matrix must define HARDWARE-PROOF-PENDING.");
        RequireContains(matrix, "NOT SUPPORTED", "Matrix must define NOT SUPPORTED.");
        RequireContains(matrix, "FORBIDDEN CLAIMS", "Matrix must define FORBIDDEN CLAIMS.");
        RequireContains(matrix, "Store logo", "Matrix must include store logo after Lane C.");
        RequireContains(matrix, "Portable backup", "Matrix must include portable backup after Lane D.");
        RequireContains(matrix, "Keyboard-wedge", "Matrix must include keyboard-wedge scanner after Lane F.");
        RequireContains(matrix, "Windows print queue", "Matrix must include Windows print queue after Lane G.");
        RequireContains(matrix, "LAN Cashier/Manager terminal", "Matrix must include LAN terminal after Lane E.");
        RequireContains(matrix, "Cloud activation", "Matrix must list cloud activation as forbidden/not supported.");
        RequireContains(matrix, "ESC/POS", "Matrix must address ESC/POS as not supported/forbidden.");

        RequireContains(deployment, "licensed LAN Cashier/Manager terminals", "DEPLOYMENT_LOCK must acknowledge LAN terminals.");
        RequireContains(deployment, "BETA_CAPABILITY_MATRIX.md", "DEPLOYMENT_LOCK must point at capability matrix.");
        RequireContains(deployment, "single-host / single database", "DEPLOYMENT_LOCK must clarify single-host DB model.");

        RequireContains(hardware, "BETA_CAPABILITY_MATRIX.md", "HARDWARE_LOCK must cross-reference matrix.");
        RequireContains(hardware, "Windows print queue", "HARDWARE_LOCK must keep Windows print queue bound.");
        RequireContains(hardware, "ESC/POS", "HARDWARE_LOCK must ban ESC/POS.");

        RequireContains(v1, "HISTORICAL PLANNING DOCUMENT", "V1_BUILD_ORDER must be marked historical.");
        RequireContains(v1, "OBSOLETE / FORBIDDEN for beta", "V1 ESC/POS step must be marked obsolete.");
        RequireContains(v1, "Windows print queue", "V1 order must redirect to Windows print queue.");

        RequireContains(feature, "Store logo", "FEATURE_LOCK must include store logo.");
        RequireContains(feature, "portable Download/Import", "FEATURE_LOCK must include portable backup.");

        // Positive promise phrases must not appear outside explicit forbidden/not-supported sections.
        // (The matrix may list these strings under FORBIDDEN CLAIMS as things never to claim.)
        RequireContains(matrix, "Never claim in beta materials", "Matrix must frame forbidden items as non-claims.");
        RequireNotContains(matrix, "we support ESC/POS", "Matrix must not positively claim ESC/POS.");
        RequireNotContains(matrix, "we support cash drawer", "Matrix must not positively claim cash drawer.");
        RequireNotContains(matrix, "cloud activation is supported", "Matrix must not positively claim cloud activation.");
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
        if (!text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }
}

using System.Text.RegularExpressions;

namespace Salryn.Tests;

public static class RmoA2OfficialOutputSmokeRoutingChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokeProgram = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string smokeChecks = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialTextFileExternalOutputSmokeChecks.cs");
        string navigation = Read(root, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(root, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string apiProgram = Read(root, "src", "Salryn.Api.Local", "Program.cs");

        RequireContains(coreSmoke, "RmoA2OfficialOutputSmokeRoutingChecks.Run();", "CoreSmoke must run RMO-A2 routing checks.");

        RequireContains(infraSmokeProgram, "case \"official-output-text-file\":", "InfrastructureSmoke must route official-output-text-file through Program.cs.");
        RequireContains(infraSmokeProgram, "OfficialTextFileExternalOutputSmokeChecks.Run(projectRoot);", "InfrastructureSmoke must invoke official-output text-file smoke checks from normal routing.");
        RequireContains(infraSmokeProgram, "official-output-text-file |", "InfrastructureSmoke usage text must list official-output-text-file.");

        RequireNotContains(infraSmokeProgram, "[ModuleInitializer]", "InfrastructureSmoke Program.cs must not use ModuleInitializer routing.");
        RequireNotContains(smokeChecks, "[ModuleInitializer]", "Official-output smoke checks must not use ModuleInitializer routing.");

        string moduleInitializerPath = Path.Combine(
            root.FullName,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "OfficialTextFileExternalOutputSmokeCommand.cs");

        if (File.Exists(moduleInitializerPath))
        {
            string legacyCommand = File.ReadAllText(moduleInitializerPath);
            RequireNotContains(legacyCommand, "[ModuleInitializer]", "OfficialTextFileExternalOutputSmokeCommand must not hijack startup with ModuleInitializer.");
            RequireNotContains(legacyCommand, "Environment.Exit(0)", "Official-output smoke must not exit before Program.cs routing.");
        }

        RequireNotContains(
            Regex.Replace(infraSmokeProgram, @"case\s+""official-output-text-file""[\s\S]*?break;", string.Empty),
            "Environment.Exit(0)",
            "InfrastructureSmoke must not short-circuit official-output-text-file with Environment.Exit.");

        RequireContains(smokeChecks, "Official text-file output smoke checks succeeded.", "Official-output smoke success line must remain stable.");

        RequireNotContains(navigation, "Official Output Text File", "Unsupported official-output work must not appear in store navigation.");
        RequireNotContains(navigation, "enabled: false", "Store navigation must not render disabled developer-only surfaces.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");
        RequireNotContains(apiProgram, "MapGet(\"/api/official-output", "API must not expose official-output routes.");

        foreach (string forbidden in new[]
        {
            "BIR-certified",
            "BIR certified",
            "BIR-accredited",
            "fully RMO-compliant",
            "legally approved",
            "certified printing",
            "CrystalDecisions",
            "ESC/POS",
            "System.Drawing.Printing",
            "PdfDocument",
        })
        {
            RequireNotContains(infraSmokeProgram, forbidden, "InfrastructureSmoke routing must not add forbidden scope: " + forbidden);
            RequireNotContains(smokeChecks, forbidden, "Official-output smoke checks must not add forbidden scope: " + forbidden);
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

using Salryn.Infrastructure.Reports.NativePdf;

namespace Salryn.Tests;

public static class NativePdfM2PackageProbeChecks
{
    public static void Run()
    {
        string root = FindRoot();

        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "REPORTING_CLOSURE_M2_FREE_NATIVE_PDF_PACKAGE_PROBE_REPORT.md")), "Native PDF M2 report required.");

        string infrastructureProject = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Salryn.Infrastructure.csproj"));
        RequireContains(infrastructureProject, "<PackageReference Include=\"PDFsharp-MigraDoc\" Version=\"6.2.4\" />", "PDFsharp-MigraDoc 6.2.4 package reference required.");

        NativePdfPackageProbeResult probe = NativePdfPackageProbe.Read();
        RequireTrue(probe.PackageId == "PDFsharp-MigraDoc", "Probe package ID must be stable.");
        RequireTrue(probe.RequestedPackageVersion == "6.2.4", "Probe requested version must be 6.2.4.");
        RequireNotBlank(probe.PdfSharpTypeName, "PDFsharp type name must be available.");
        RequireNotBlank(probe.PdfSharpAssemblyName, "PDFsharp assembly name must be available.");
        RequireNotBlank(probe.PdfSharpAssemblyVersion, "PDFsharp assembly version must be available.");
        RequireNotBlank(probe.MigraDocTypeName, "MigraDoc type name must be available.");
        RequireNotBlank(probe.MigraDocAssemblyName, "MigraDoc assembly name must be available.");
        RequireNotBlank(probe.MigraDocAssemblyVersion, "MigraDoc assembly version must be available.");
        RequireTrue(!probe.RequiresFileOutput, "Package probe must not require file output.");
        RequireTrue(!probe.IsRenderer, "Package probe is not a renderer.");
        RequireTrue(!probe.ClaimsPdfProof, "Package probe must not claim PDF proof.");

        string probeSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfPackageProbe.cs"));
        RequireContains(probeSource, "typeof(PdfDocument)", "Probe must reference PDFsharp type.");
        RequireContains(probeSource, "typeof(Document)", "Probe must reference MigraDoc type.");
        RequireNotContains(probeSource, "Save(", "Probe must not save a PDF.");
        RequireNotContains(probeSource, "FileStream", "Probe must not open file streams.");
        RequireNotContains(probeSource, "NativePdfReportRenderer", "M2 must not implement the native renderer.");
        RequireNotContains(probeSource, "ProductionReportDatasetEnvelope", "M2 probe must not render Salryn datasets.");

        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "reports-native-pdf-m2-package-probe", "Focused CoreSmoke gate required.");
        RequireContains(coreSmoke, "NativePdfM2PackageProbeChecks.Run()", "Full CoreSmoke must run Native PDF M2 package probe.");
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + expected);
        }
    }

    private static void RequireNotContains(string text, string forbidden, string message)
    {
        if (text.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + forbidden);
        }
    }
}

namespace Salryn.Tests;

public static class PosStabA5ReceiptPrintingChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string receiptText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Sales",
            "ReceiptPreviewForm.cs");
        string officialOutputFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "OfficialOutput",
            "OfficialOutputTextFileWorkflowForm.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            receiptText,
            "using System.Drawing.Printing;",
            "A5 receipt preview must use normal Windows printing APIs.");
        RequireContains(
            receiptText,
            "PrintDocument",
            "A5 receipt preview must create a Windows print document.");
        RequireContains(
            receiptText,
            "PrintDialog",
            "A5 receipt preview must use the Windows print dialog.");
        RequireContains(
            receiptText,
            "PrinterSettings.InstalledPrinters.Count",
            "A5 receipt preview must fail safely when no Windows printer queue exists.");
        RequireContains(
            receiptText,
            "Receipt sent to Windows printer queue.",
            "A5 receipt preview must report successful queue dispatch.");
        RequireContains(
            receiptText,
            "Receipt print cancelled.",
            "A5 receipt preview must report print cancellation safely.");
        RequireContains(
            receiptText,
            "Printing here uses a normal Windows printer queue and is not official BIR/RMO output printing.",
            "A5 receipt preview comments must keep normal receipt printing separate from official output.");
        RequireDoesNotContain(
            receiptText,
            "ESC/POS",
            "A5 receipt printing must not add ESC/POS output.");
        RequireDoesNotContain(
            receiptText,
            "RawPrinter",
            "A5 receipt printing must not add raw printer output.");
        RequireDoesNotContain(
            receiptText,
            "Winspool",
            "A5 receipt printing must not add direct Winspool output.");
        RequireDoesNotContain(
            receiptText,
            "OfficialOutputTextFile",
            "A5 receipt printing must not call the official output text-file workflow.");
        RequireDoesNotContain(
            receiptText,
            "Crystal",
            "A5 receipt printing must not add Crystal output.");
        RequireDoesNotContain(
            receiptText,
            "PdfDocument",
            "A5 receipt printing must not add PDF output.");

        RequireDoesNotContain(
            officialOutputFormText,
            "PrintDocument",
            "A5 must not add printing to official output UI.");
        RequireDoesNotContain(
            officialOutputFormText,
            "System.Drawing.Printing",
            "A5 must not add Windows printing to official output UI.");

        RequireContains(
            coreSmokeText,
            "PosStabA5ReceiptPrintingChecks.Run();",
            "CoreSmoke must run POS-STAB-A5 receipt printing checks.");
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

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

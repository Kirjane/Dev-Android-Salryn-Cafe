namespace Salryn.Tests;

public static class J21Rmo242023OfficialOutputPipelineCompletionSealChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokeProgram = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialOutputExportPackageBuilder.cs"), "OfficialOutputExportPackage", "09L package must exist.");
        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialInMemoryOutputDeliveryAdapter.cs"), "OfficialInMemoryOutputDeliveryAdapter", "09M delivery adapter must exist.");
        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialOutputDeliveryReceiptLedgerBuilder.cs"), "OfficialOutputDeliveryReceiptLedgerBuilder", "09N delivery ledger must exist.");
        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputAdapterContracts.cs"), "OfficialExternalOutputDispatchPlanBuilder", "09O external output contract must exist.");
        RequireContains(Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs"), "OfficialTextFileExternalOutputAdapter", "09P text-file adapter must exist.");
        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputExecutionContracts.cs"), "OfficialExternalOutputExecutionLedgerBuilder", "09Q execution ledger must exist.");
        RequireContains(Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputVerifier.cs"), "OfficialTextFileExternalOutputVerifier", "09R verifier must exist.");
        RequireContains(Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialExternalOutputTextFileWorkflowRunner.cs"), "OfficialExternalOutputTextFileWorkflowRunner", "09S workflow runner must exist.");
        RequireContains(infraSmokeProgram, "case \"official-output-text-file\":", "InfrastructureSmoke must expose official-output-text-file command.");
        RequireContains(infraSmokeProgram, "OfficialTextFileExternalOutputSmokeChecks.Run(projectRoot);", "InfrastructureSmoke must route official-output-text-file to smoke checks.");
        RequireContains(coreSmoke, "J21Rmo242023OfficialOutputPipelineCompletionSealChecks.Run();", "CoreSmoke must run final output pipeline seal.");

        string[] productionFiles =
        {
            Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputExecutionContracts.cs"),
            Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputVerificationContracts.cs"),
            Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputWorkflowContracts.cs"),
            Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileOutputPathPolicy.cs"),
            Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs"),
            Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputVerifier.cs"),
            Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialExternalOutputTextFileWorkflowRunner.cs")
        };

        foreach (string text in productionFiles)
        {
            foreach (string forbidden in new[]
            {
                "CrystalDecisions", ".rpt", "System.Windows.Forms", "Microsoft.Data.Sqlite", "Sqlite", "SQLite",
                "Process.Start", "ESC/POS", "PrintDocument", "PrinterSettings", "System.Drawing.Printing",
                "PdfDocument", "ExportToDisk", "CompleteSale", "OfficialCheckout", "ElectronicJournalAppend",
                "OfficialZReadingService", "FinalizeAsync", "SaveAsync", "INSERT ", "UPDATE ", "DELETE "
            })
            {
                RequireDoesNotContain(text, forbidden, "09T seal found forbidden marker: " + forbidden);
            }
        }

        RequireContains(Read(root, "src", "Salryn.Core", "Services", "OfficialExternalOutputWorkflowContracts.cs"), "OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_VERIFIED", "09T must seal verified workflow marker.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }
}

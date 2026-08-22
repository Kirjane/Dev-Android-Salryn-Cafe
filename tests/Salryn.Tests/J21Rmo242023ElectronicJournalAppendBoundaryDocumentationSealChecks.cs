namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendBoundaryDocumentationSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string sealText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_ELECTRONIC_JOURNAL_APPEND_BOUNDARY_SEAL.md");
        string matrixText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_COMPLIANCE_MATRIX.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string[] handoffs =
        {
            "J21_5G_ELECTRONIC_JOURNAL_APPEND_SERVICE_BOUNDARY.md",
            "J21_5H_ELECTRONIC_JOURNAL_APPEND_ATOMIC_BOUNDARY.md",
            "J21_5I_ELECTRONIC_JOURNAL_APPEND_PERSISTENCE_PORTS.md",
            "J21_5J_ELECTRONIC_JOURNAL_APPEND_ORCHESTRATION_SHELL.md",
            "J21_5K_ELECTRONIC_JOURNAL_APPEND_REGISTRATION_BOUNDARY.md",
            "J21_5L_ELECTRONIC_JOURNAL_APPEND_BOUNDARY_DOCUMENTATION_SEAL.md"
        };

        foreach (string handoff in handoffs)
        {
            string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", handoff);

            RequireContains(handoffText, "## Scope", $"{handoff} must document scope.");
            RequireContains(handoffText, "## RMO Controls", $"{handoff} must document RMO controls.");
            RequireContains(handoffText, "## Not Added", $"{handoff} must document exclusions.");
            RequireContains(handoffText, "No SQLite", $"{handoff} must block SQLite work.");
            RequireContains(handoffText, "No database migration", $"{handoff} must block migration work.");
            RequireContains(handoffText, "No checkout wiring", $"{handoff} must block checkout work.");
            RequireContains(handoffText, "No Crystal Reports wiring", $"{handoff} must block Crystal Reports work.");
            RequireContains(handoffText, "No official output claim", $"{handoff} must block official output claims.");
            RequireDoesNotContain(handoffText, "RMO complete", $"{handoff} must not claim full RMO completion.");
            RequireDoesNotContain(handoffText, "official output implemented", $"{handoff} must not claim output implementation.");
        }

        RequireContains(sealText, "J21-5F defines the append contract shape.", "Seal must include J21-5F.");
        RequireContains(sealText, "J21-5G defines the service boundary and contract validator.", "Seal must include J21-5G.");
        RequireContains(sealText, "J21-5H defines atomic boundary rules.", "Seal must include J21-5H.");
        RequireContains(sealText, "J21-5I defines persistence port contracts.", "Seal must include J21-5I.");
        RequireContains(sealText, "J21-5J defines the Core orchestration shell.", "Seal must include J21-5J.");
        RequireContains(sealText, "J21-5K defines the Core registration boundary.", "Seal must include J21-5K.");
        RequireContains(sealText, "J21-5L seals documentation", "Seal must include J21-5L.");
        RequireContains(sealText, "Crystal Reports output must be a separate reporting/output adapter phase.", "Seal must keep Crystal Reports deferred.");

        RequireContains(matrixText, "## J21-5G Electronic Journal Append Service Boundary", "Matrix must include J21-5G.");
        RequireContains(matrixText, "## J21-5H Electronic Journal Append Atomic Boundary Rules", "Matrix must include J21-5H.");
        RequireContains(matrixText, "## J21-5I Electronic Journal Append Persistence Ports", "Matrix must include J21-5I.");
        RequireContains(matrixText, "## J21-5J Electronic Journal Append Orchestration Shell", "Matrix must include J21-5J.");
        RequireContains(matrixText, "## J21-5K Electronic Journal Append Registration Boundary", "Matrix must include J21-5K.");
        RequireContains(matrixText, "## J21-5L Electronic Journal Append Boundary Documentation Seal", "Matrix must include J21-5L.");

        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendBoundaryDocumentationSealChecks.Run();", "CoreSmoke must run J21-5L.");
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

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

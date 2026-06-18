namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalPlanningChecks
{
    // Kirch strict dev guide:
    // Run() is the CoreSmoke entry point; it reads repo files and fails fast if J21-5A planning guardrails are missing.
    // Syntax note: public static means CoreSmoke can call this without creating an object instance.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string boundaryText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_BOUNDARY.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5A_ELECTRONIC_JOURNAL_AUDIT_JOURNAL_PLANNING.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        string initialSchemaText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "InitialSchemaMigration.cs");

        string snapshotDraftText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Models",
            "OfficialDocumentSnapshotDraft.cs");

        string snapshotBuilderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialDocumentSnapshotDraftBuilder.cs");

        string taxBucketText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Models",
            "OfficialDocumentTaxBucketDraft.cs");

        RequireContains(boundaryText, "Status: planning and guardrails only.", "J21-5A boundary must be planning only.");
        RequireContains(boundaryText, "C08 planning only: electronic journal and audit journal boundary", "J21-5A boundary must map C08 planning.");
        RequireContains(boundaryText, "C07 partial planning: transaction integrity, reprint, void, cancel, and refund guardrails", "J21-5A boundary must map partial C07.");
        RequireContains(boundaryText, "C12 partial planning: preservation, retention, backup, and recovery guardrails", "J21-5A boundary must map partial C12.");

        RequireContains(boundaryText, "Future Electronic Journal Evidence", "J21-5A boundary must define future electronic journal evidence.");
        RequireContains(boundaryText, "official document issuance", "J21-5A boundary must include future issuance evidence.");
        RequireContains(boundaryText, "official document rendered snapshot hash after a renderer exists", "J21-5A boundary must include future snapshot hash evidence.");
        RequireContains(boundaryText, "official document reprint event", "J21-5A boundary must include future reprint evidence.");
        RequireContains(boundaryText, "official document void event", "J21-5A boundary must include future void evidence.");
        RequireContains(boundaryText, "official document cancel event", "J21-5A boundary must include future cancel evidence.");
        RequireContains(boundaryText, "official document refund event", "J21-5A boundary must include future refund evidence.");
        RequireContains(boundaryText, "export or preservation event", "J21-5A boundary must include future export/preservation evidence.");

        RequireContains(boundaryText, "Future Audit Journal Evidence", "J21-5A boundary must define future audit journal evidence.");
        RequireContains(boundaryText, "user/cashier identity", "J21-5A boundary must include user/cashier identity.");
        RequireContains(boundaryText, "action type", "J21-5A boundary must include action type.");
        RequireContains(boundaryText, "affected entity identity", "J21-5A boundary must include affected entity identity.");
        RequireContains(boundaryText, "reason or operator note where required", "J21-5A boundary must include reason/operator note.");
        RequireContains(boundaryText, "timestamp", "J21-5A boundary must include timestamp.");

        RequireContains(boundaryText, "official document number", "J21-5A boundary must include official document number future field.");
        RequireContains(boundaryText, "document kind", "J21-5A boundary must include document kind future field.");
        RequireContains(boundaryText, "machine profile id", "J21-5A boundary must include machine profile id future field.");
        RequireContains(boundaryText, "sale id", "J21-5A boundary must include sale id future field.");
        RequireContains(boundaryText, "cashier/user id", "J21-5A boundary must include cashier/user id future field.");
        RequireContains(boundaryText, "transaction timestamp", "J21-5A boundary must include transaction timestamp future field.");
        RequireContains(boundaryText, "rendered snapshot hash later", "J21-5A boundary must include rendered snapshot hash future field.");
        RequireContains(boundaryText, "reprint marker later", "J21-5A boundary must include reprint marker future field.");
        RequireContains(boundaryText, "void/cancel/refund event later", "J21-5A boundary must include reversal event future field.");
        RequireContains(boundaryText, "export/preservation metadata later", "J21-5A boundary must include export/preservation metadata future field.");

        RequireContains(boundaryText, "J21-5A does not write to sales.receipt_snapshot.", "J21-5A boundary must block receipt snapshot writes.");
        RequireContains(boundaryText, "J21-5A does not use print_jobs as an electronic journal.", "J21-5A boundary must block treating print_jobs as journal.");
        RequireContains(boundaryText, "J21-5A does not upgrade audit_logs into an RMO electronic journal.", "J21-5A boundary must block audit_logs upgrade.");
        RequireContains(boundaryText, "No DB/schema migration", "J21-5A boundary must block schema migration.");
        RequireContains(boundaryText, "new journal tables", "J21-5A boundary must mention new journal tables are not added.");
        RequireContains(boundaryText, "checkout wiring", "J21-5A boundary must block checkout wiring.");
        RequireContains(boundaryText, "official document persistence", "J21-5A boundary must block official document persistence.");
        RequireContains(boundaryText, "renderer", "J21-5A boundary must block renderer.");
        RequireContains(boundaryText, "printer", "J21-5A boundary must block printer.");
        RequireContains(boundaryText, "sequence allocation changes", "J21-5A boundary must block sequence allocation changes.");
        RequireContains(boundaryText, "tax calculator engine", "J21-5A boundary must block tax calculator engine.");
        RequireContains(boundaryText, "X/Z reading", "J21-5A boundary must block X/Z reading.");
        RequireContains(boundaryText, "official receipt/invoice output claim", "J21-5A boundary must block official output claim.");

        RequireContains(handoffText, "Status: planning and guardrails only.", "J21-5A handoff must be planning only.");
        RequireContains(handoffText, "No DB/schema migration.", "J21-5A handoff must block DB/schema migration.");
        RequireContains(handoffText, "No new journal tables.", "J21-5A handoff must block new journal tables.");
        RequireContains(handoffText, "No checkout wiring.", "J21-5A handoff must block checkout wiring.");
        RequireContains(handoffText, "No receipt_snapshot writes.", "J21-5A handoff must block receipt snapshot writes.");
        RequireContains(handoffText, "No official document persistence.", "J21-5A handoff must block official document persistence.");
        RequireContains(handoffText, "No renderer.", "J21-5A handoff must block renderer.");
        RequireContains(handoffText, "No printer.", "J21-5A handoff must block printer.");
        RequireContains(handoffText, "No sequence allocation changes.", "J21-5A handoff must block sequence allocation changes.");
        RequireContains(handoffText, "No tax calculator engine.", "J21-5A handoff must block tax calculator engine.");
        RequireContains(handoffText, "No X/Z reading.", "J21-5A handoff must block X/Z reading.");
        RequireContains(handoffText, "No official receipt/invoice output claim.", "J21-5A handoff must block official output claim.");
        RequireContains(handoffText, "J21-5B must be explicitly confirmed before proceeding.", "J21-5A handoff must block automatic J21-5B.");

        RequireContains(matrixText, "## J21-5A Electronic Journal / Audit Journal Planning Guardrails", "Compliance matrix must include J21-5A section.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalPlanningChecks.Run();", "Core smoke must run J21-5A checks.");

        RequireContains(initialSchemaText, "receipt_snapshot TEXT NULL", "Repo read guard must confirm existing sales receipt_snapshot field.");
        RequireContains(initialSchemaText, "CREATE TABLE print_jobs", "Repo read guard must confirm existing print_jobs boundary.");
        RequireContains(initialSchemaText, "CREATE TABLE audit_logs", "Repo read guard must confirm existing audit_logs boundary.");

        RequireContains(snapshotDraftText, "No renderer, printer, checkout wiring, sequence allocation, database persistence", "J21-5A guard must preserve J21-4A output boundary.");
        RequireContains(snapshotBuilderText, "It must not render, print, persist, allocate sequence numbers, wire checkout", "J21-5A guard must preserve J21-4B builder boundary.");
        RequireContains(taxBucketText, "No calculator engine, renderer, printer, checkout wiring, sale snapshot write", "J21-5A guard must preserve J21-4C tax boundary.");

        RequireDoesNotContain(boundaryText, "CREATE TABLE", "J21-5A boundary must not define schema.");
        RequireDoesNotContain(handoffText, "CREATE TABLE", "J21-5A handoff must not define schema.");
    }

    // Kirch strict dev guide:
    // FindRepositoryRoot() walks upward until it finds Salryn.sln, so checks work from test/bin output or repo root.
    // Syntax note: DirectoryInfo? means the variable may temporarily become null while walking parent folders.
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

    // Kirch strict dev guide:
    // ReadRequiredFile() builds a path from repo root and path parts, then loads text required by the guard.
    // Syntax note: params string[] lets callers pass path pieces like "00-COMPLIANCE", "file.md" without manually creating an array.
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

    // Kirch strict dev guide:
    // RequireContains() proves an expected guard phrase exists; missing text means the phase boundary was weakened.
    // Syntax note: StringComparison.Ordinal makes the check exact and culture-independent.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch strict dev guide:
    // RequireDoesNotContain() blocks forbidden implementation language such as schema SQL inside planning-only docs.
    // Syntax note: throwing InvalidOperationException makes CoreSmoke fail immediately when forbidden text appears.
    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalSchemaPlanningChecks
{
    // Kirch strict dev guide:
    // Run() is the CoreSmoke entry point; it verifies J21-5B is schema planning only and still has no persistence.
    // Syntax note: static lets the smoke tool call this guard without creating a test class instance.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string planText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_SCHEMA_PLAN.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5B_ELECTRONIC_JOURNAL_SCHEMA_PLANNING.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string j21FiveABoundaryText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_BOUNDARY.md");

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

        RequireContains(planText, "Status: planning and guardrails only.", "J21-5B plan must be planning only.");
        RequireContains(planText, "This document is not a schema implementation.", "J21-5B plan must deny schema implementation.");
        RequireContains(planText, "C08 planning only: future electronic journal and audit journal schema shape", "J21-5B plan must map C08.");
        RequireContains(planText, "C07 partial planning: append-only and event integrity planning", "J21-5B plan must map partial C07.");
        RequireContains(planText, "C12 partial planning: retention and preservation planning", "J21-5B plan must map partial C12.");

        RequireContains(planText, "Future Table Candidates Only", "J21-5B plan must define candidate tables only.");
        RequireContains(planText, "future electronic_journal_entries", "J21-5B plan must include future electronic_journal_entries candidate.");
        RequireContains(planText, "future electronic_journal_entry_events", "J21-5B plan must include future electronic_journal_entry_events candidate.");
        RequireContains(planText, "future electronic_journal_exports or preservation batches", "J21-5B plan must include future export/preservation candidate.");
        RequireContains(planText, "These names are future table candidates only. They are not created in J21-5B.", "J21-5B plan must say candidates are not created.");

        RequireContains(planText, "journal entry id", "J21-5B plan must include journal entry id field ownership.");
        RequireContains(planText, "machine profile id", "J21-5B plan must include machine profile id field ownership.");
        RequireContains(planText, "sale id", "J21-5B plan must include sale id field ownership.");
        RequireContains(planText, "official document sequence id", "J21-5B plan must include official sequence id field ownership.");
        RequireContains(planText, "official document number", "J21-5B plan must include official document number field ownership.");
        RequireContains(planText, "document kind", "J21-5B plan must include document kind field ownership.");
        RequireContains(planText, "transaction timestamp", "J21-5B plan must include transaction timestamp field ownership.");
        RequireContains(planText, "cashier/user id", "J21-5B plan must include cashier/user id field ownership.");
        RequireContains(planText, "terminal id", "J21-5B plan must include terminal id field ownership.");
        RequireContains(planText, "snapshot payload/hash", "J21-5B plan must include snapshot payload/hash field ownership.");
        RequireContains(planText, "event type", "J21-5B plan must include event type field ownership.");
        RequireContains(planText, "original/reprint/void/refund/cancel markers", "J21-5B plan must include lifecycle marker field ownership.");
        RequireContains(planText, "created_at", "J21-5B plan must include created_at field ownership.");
        RequireContains(planText, "preserved/exported metadata", "J21-5B plan must include preserved/exported metadata field ownership.");

        RequireContains(planText, "sales.receipt_snapshot remains current MVP placeholder, not RMO journal.", "J21-5B plan must preserve sales.receipt_snapshot boundary.");
        RequireContains(planText, "audit_logs remains generic audit trail, not RMO electronic journal.", "J21-5B plan must preserve audit_logs boundary.");
        RequireContains(planText, "print_jobs remains printer queue boundary, not journal evidence.", "J21-5B plan must preserve print_jobs boundary.");
        RequireContains(planText, "official document draft/builder remains in-memory until future approved persistence phase.", "J21-5B plan must preserve in-memory draft/builder boundary.");
        RequireContains(planText, "sequence allocation must stay separate from journal append until a later atomic design is confirmed.", "J21-5B plan must keep sequence allocation separate.");

        RequireContains(planText, "Future journal records should be append-only in design.", "J21-5B plan must include append-only planning.");
        RequireContains(planText, "Future correction paths should use linked events instead of editing original journal entries.", "J21-5B plan must include event integrity planning.");
        RequireContains(planText, "Future index candidates may include:", "J21-5B plan must include index planning only.");
        RequireContains(planText, "These are index candidates only, not schema.", "J21-5B plan must keep indexes planning-only.");

        RequireContains(planText, "No DB/schema migration", "J21-5B plan must block DB/schema migration.");
        RequireContains(planText, "No journal tables", "J21-5B plan must block journal tables.");
        RequireContains(planText, "No checkout wiring", "J21-5B plan must block checkout wiring.");
        RequireContains(planText, "No receipt_snapshot writes", "J21-5B plan must block receipt_snapshot writes.");
        RequireContains(planText, "No official document persistence", "J21-5B plan must block official persistence.");
        RequireContains(planText, "No renderer", "J21-5B plan must block renderer.");
        RequireContains(planText, "No printer", "J21-5B plan must block printer.");
        RequireContains(planText, "No sequence allocation changes", "J21-5B plan must block sequence allocation changes.");
        RequireContains(planText, "No tax calculator engine", "J21-5B plan must block tax calculator engine.");
        RequireContains(planText, "No X/Z reading", "J21-5B plan must block X/Z reading.");
        RequireContains(planText, "No official receipt/invoice output claim", "J21-5B plan must block official output claim.");

        RequireContains(handoffText, "Status: planning and guardrails only.", "J21-5B handoff must be planning only.");
        RequireContains(handoffText, "These are table candidates only and are not created in J21-5B.", "J21-5B handoff must block table creation.");
        RequireContains(handoffText, "No DB/schema migration.", "J21-5B handoff must block DB/schema migration.");
        RequireContains(handoffText, "No journal tables.", "J21-5B handoff must block journal tables.");
        RequireContains(handoffText, "No checkout wiring.", "J21-5B handoff must block checkout wiring.");
        RequireContains(handoffText, "No receipt_snapshot writes.", "J21-5B handoff must block receipt_snapshot writes.");
        RequireContains(handoffText, "No official document persistence.", "J21-5B handoff must block official persistence.");
        RequireContains(handoffText, "No renderer.", "J21-5B handoff must block renderer.");
        RequireContains(handoffText, "No printer.", "J21-5B handoff must block printer.");
        RequireContains(handoffText, "No sequence allocation changes.", "J21-5B handoff must block sequence changes.");
        RequireContains(handoffText, "No tax calculator engine.", "J21-5B handoff must block tax engine.");
        RequireContains(handoffText, "No X/Z reading.", "J21-5B handoff must block X/Z.");
        RequireContains(handoffText, "No official receipt/invoice output claim.", "J21-5B handoff must block official output claim.");
        RequireContains(handoffText, "J21-5C must be explicitly confirmed before proceeding.", "J21-5B handoff must block automatic J21-5C.");

        RequireContains(matrixText, "## J21-5B Electronic Journal Schema Planning", "Compliance matrix must include J21-5B section.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalSchemaPlanningChecks.Run();", "CoreSmoke must run J21-5B checks.");

        RequireContains(j21FiveABoundaryText, "Do not implement J21-5B schema", "J21-5B check must preserve J21-5A warning context.");
        RequireContains(initialSchemaText, "receipt_snapshot TEXT NULL", "J21-5B check must confirm existing receipt_snapshot placeholder.");
        RequireContains(initialSchemaText, "CREATE TABLE print_jobs", "J21-5B check must confirm existing print_jobs boundary.");
        RequireContains(initialSchemaText, "CREATE TABLE audit_logs", "J21-5B check must confirm existing audit_logs boundary.");

        RequireDoesNotContain(planText, "CREATE TABLE", "J21-5B plan must not define SQL schema.");
        RequireDoesNotContain(handoffText, "CREATE TABLE", "J21-5B handoff must not define SQL schema.");
        RequireDoesNotContain(planText, "SqliteConnection", "J21-5B plan must not include SQLite implementation.");
        RequireDoesNotContain(handoffText, "SqliteConnection", "J21-5B handoff must not include SQLite implementation.");
    }

    // Kirch strict dev guide:
    // FindRepositoryRoot() walks upward until it finds Salryn.sln, making this check stable from bin folders or repo root.
    // Syntax note: DirectoryInfo? allows current to become null when no parent folder remains.
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
    // ReadRequiredFile() joins repo root with path parts and loads a required source or markdown file.
    // Syntax note: params string[] lets the caller pass path segments without manually creating an array.
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
    // RequireContains() enforces that a required planning guard phrase exists in the checked text.
    // Syntax note: StringComparison.Ordinal keeps matching exact and culture-independent.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch strict dev guide:
    // RequireDoesNotContain() blocks forbidden implementation terms from planning-only docs.
    // Syntax note: throwing InvalidOperationException makes CoreSmoke fail immediately on scope creep.
    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
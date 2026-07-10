using Salryn.Core.Services;

namespace Salryn.Tests;

/// <summary>TRAD-M4 document lifecycle source/gate checks.</summary>
public static class RmoBirTradM4DocumentLifecycleChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string host = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialDocumentLifecycleService.cs"));
        string mutation = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialDocumentLifecycleMutationService.cs"));
        string endpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialDocumentLifecycleEndpoints.cs"));
        string factory = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string study = Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M4_DOCUMENT_LIFECYCLE_STUDY.md");

        RequireTrue(File.Exists(study), "M4 study required.");
        RequireContains(host, "SequenceAdvanced: false", "Lifecycle must not claim sequence advance.");
        RequireContains(host, "OriginalImmutable: true", "Original must remain immutable.");
        RequireContains(host, "OfficialDocumentLifecycleAction.Reprint", "Reprint action required.");
        RequireContains(host, "OfficialDocumentLifecycleAction.Void", "Void action required.");
        RequireContains(host, "OfficialDocumentLifecycleAction.Refund", "Refund action required.");
        RequireContains(mutation, "event_type", "Mutation must write event_type.");
        RequireContains(mutation, "void_marker", "Void marker required.");
        RequireContains(mutation, "refund_marker", "Refund marker required.");
        RequireFalse(mutation.Contains("UPDATE bir_electronic_journal_entries", StringComparison.OrdinalIgnoreCase),
            "Must not update original EJ entry.");
        RequireFalse(mutation.Contains("bir_official_document_sequences", StringComparison.OrdinalIgnoreCase),
            "Must not touch sequence table.");
        RequireContains(endpoints, "/api/official/document-lifecycle/status", "Status endpoint required.");
        RequireContains(endpoints, "/api/official/document-lifecycle/action", "Action endpoint required.");
        RequireContains(factory, "MapLocalOfficialDocumentLifecycleEndpoints", "Factory must map lifecycle.");
        RequireContains(coreSmoke, "rmo-bir-trad-document-lifecycle", "CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM4DocumentLifecycleChecks.Run()", "CoreSmoke must run M4.");

        HostOfficialDocumentLifecycleService statusOnly = new();
        var status = statusOnly.GetStatus();
        RequireTrue(status.IsSuccess, "Status must succeed.");
        RequireFalse(status.Value.LiveOfficialPostingAllowed, "Live posting false.");
        RequireFalse(status.Value.ProductionActivationEnabled, "Activation default off.");

        Console.WriteLine("[TRAD-M4] Document lifecycle checks passed.");
    }

    private static string FindRoot()
    {
        DirectoryInfo? d = new(Directory.GetCurrentDirectory());
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, "Salryn.sln")))
            {
                return d.FullName;
            }

            d = d.Parent;
        }

        throw new InvalidOperationException("sln missing");
    }

    private static void RequireTrue(bool c, string m)
    {
        if (!c)
        {
            throw new InvalidOperationException(m);
        }
    }

    private static void RequireFalse(bool c, string m) => RequireTrue(!c, m);

    private static void RequireContains(string h, string n, string m)
    {
        if (!h.Contains(n, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(m + " Missing: " + n);
        }
    }
}

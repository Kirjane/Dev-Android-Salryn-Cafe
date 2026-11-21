namespace Salryn.Tools.CoreSmoke;

internal sealed record CoreSmokeOptions(string Profile, string? Only, string? ProofManifest)
{
    public const string Cafe = "cafe";
    public const string LegacyRetail = "legacy-retail";
    public bool IncludesRetailProcurement => Profile == LegacyRetail;

    public static CoreSmokeOptions Parse(string[] args)
    {
        string profile = Cafe;
        string? only = null;
        string? manifest = null;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i].ToLowerInvariant();
            if (option is not ("--profile" or "--only" or "--proof-manifest"))
                throw new InvalidOperationException("Unknown CoreSmoke option: " + args[i]);
            if (!seen.Add(option)) throw new InvalidOperationException("Duplicate CoreSmoke option: " + option);
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException(option + " requires a value.");
            switch (option)
            {
                case "--profile": profile = args[i].ToLowerInvariant(); break;
                case "--only": only = args[i]; break;
                case "--proof-manifest": manifest = Path.GetFullPath(args[i]); break;
            }
        }
        if (profile is not (Cafe or LegacyRetail))
            throw new InvalidOperationException("CoreSmoke --profile must be cafe or legacy-retail.");
        if (manifest is not null && only is null)
            throw new InvalidOperationException("--proof-manifest requires an explicit --only check.");
        return new(profile, only, manifest);
    }

    public void RequireCompatibleCheck(string checkName)
    {
        if (!IncludesRetailProcurement && IsRetailProcurementCheck(checkName))
            throw new InvalidOperationException(
                "NOT RUN: " + checkName + " is a preserved retail procurement proof. " +
                "Select --profile legacy-retail explicitly; Cafe schema must not acquire retail tables to satisfy this proof.");
    }

    private static bool IsRetailProcurementCheck(string checkName) => checkName.ToLowerInvariant() is
        "lpi" or "lpi-procure-to-pay" or "lpi-p1" or "lpi-p1-integrity" or
        "ceo-repro" or "ceo-pr-po-repro" or "r3-a1a" or "r3-a1a-pr-po-authority" or
        "ceo-orders-quotes" or "lpi-evidence" or "lpi-acceptance" or "lpi-real-host";
}

internal static class CoreSmokeProfileChecks
{
    public static void Run()
    {
        int assertions = 0;
        void Require(bool ok)
        {
            assertions++;
            if (!ok) throw new InvalidOperationException("CoreSmoke profile boundary assertion failed: " + assertions);
        }
        void Denied(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { Require(true); return; }
            Require(false);
        }

        CoreSmokeOptions cafe = CoreSmokeOptions.Parse([]);
        Require(cafe.Profile == CoreSmokeOptions.Cafe && !cafe.IncludesRetailProcurement);
        foreach (string name in new[] { "lpi", "lpi-procure-to-pay", "lpi-p1", "lpi-p1-integrity", "r3-a1a", "r3-a1a-pr-po-authority" })
            Denied(() => cafe.RequireCompatibleCheck(name));
        CoreSmokeOptions legacy = CoreSmokeOptions.Parse(["--only", "lpi", "--profile", "LEGACY-RETAIL"]);
        Require(legacy.IncludesRetailProcurement && legacy.Only == "lpi");
        legacy.RequireCompatibleCheck("lpi");
        Require(true);
        CoreSmokeOptions selectedCafe = CoreSmokeOptions.Parse(["--profile", "cafe", "--only", "cafe-loopback-boundary"]);
        selectedCafe.RequireCompatibleCheck(selectedCafe.Only!);
        Require(selectedCafe.Only == "cafe-loopback-boundary" && !selectedCafe.IncludesRetailProcurement);
        Denied(() => CoreSmokeOptions.Parse(["--profile", "unknown"]));
        Denied(() => CoreSmokeOptions.Parse(["--only"]));
        Denied(() => CoreSmokeOptions.Parse(["--unknown", "value"]));
        Denied(() => CoreSmokeOptions.Parse(["--profile", "cafe", "--profile", "legacy-retail"]));
        Console.WriteLine($"CoreSmoke profile boundary: PASS ({assertions} assertions; selection only, no product acceptance).");
    }
}

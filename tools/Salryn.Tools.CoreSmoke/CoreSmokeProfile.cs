namespace Salryn.Tools.CoreSmoke;

internal sealed class BlockedExternalException(string message) : Exception(message);

internal sealed record CoreSmokeOptions(
    string Profile,
    string? Only,
    string? ProofManifest,
    string VerificationProfile,
    string? LicenseFixturePath)
{
    public const string Cafe = "cafe";
    public const string LegacyRetail = "legacy-retail";

    public const string ReleaseUnlicensed = "ReleaseUnlicensed";
    public const string DebugSynthetic = "DebugSynthetic";
    public const string ReleaseEntitled = "ReleaseEntitled";

    public const string LicenseFixtureEnvVar = "SALRYN_QA_RELEASE_LICENSE_PATH";

    public bool IncludesRetailProcurement => Profile == LegacyRetail;

    public static string DefaultVerificationProfileForBuild(bool isDebugBuild) =>
        isDebugBuild ? DebugSynthetic : ReleaseUnlicensed;

    public static CoreSmokeOptions Parse(string[] args, bool? isDebugBuildOverride = null)
    {
        bool isDebugBuild = isDebugBuildOverride ??
#if DEBUG
            true;
#else
            false;
#endif
        string profile = Cafe;
        string? only = null;
        string? manifest = null;
        string? verificationProfile = null;
        string? licenseFixture = null;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i].ToLowerInvariant();
            if (option is not ("--profile" or "--only" or "--proof-manifest" or "--verification-profile" or "--license-fixture"))
                throw new InvalidOperationException("Unknown CoreSmoke option: " + args[i]);
            if (!seen.Add(option)) throw new InvalidOperationException("Duplicate CoreSmoke option: " + option);
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException(option + " requires a value.");
            switch (option)
            {
                case "--profile": profile = args[i].ToLowerInvariant(); break;
                case "--only": only = args[i]; break;
                case "--proof-manifest": manifest = Path.GetFullPath(args[i]); break;
                case "--verification-profile": verificationProfile = NormalizeVerificationProfile(args[i]); break;
                case "--license-fixture": licenseFixture = Path.GetFullPath(args[i]); break;
            }
        }
        if (profile is not (Cafe or LegacyRetail))
            throw new InvalidOperationException("CoreSmoke --profile must be cafe or legacy-retail.");
        if (manifest is not null && only is null)
            throw new InvalidOperationException("--proof-manifest requires an explicit --only check.");
        string resolvedVerification = verificationProfile ?? DefaultVerificationProfileForBuild(isDebugBuild);
        return new(profile, only, manifest, resolvedVerification, licenseFixture);
    }

    public string? ValidateExecutionProfile(bool isDebugBuild, Func<string, string?>? getEnv = null, Func<string, bool>? fileExists = null)
    {
        if (VerificationProfile == DebugSynthetic && !isDebugBuild)
            throw new InvalidOperationException(
                "CoreSmoke --verification-profile DebugSynthetic is forbidden on Release builds; #if DEBUG development license mode is compiled out.");
        if (VerificationProfile == ReleaseUnlicensed && isDebugBuild)
            throw new InvalidOperationException(
                "CoreSmoke --verification-profile ReleaseUnlicensed requires a Release build without #if DEBUG development license mode.");
        if (VerificationProfile == ReleaseEntitled && isDebugBuild)
            throw new InvalidOperationException(
                "CoreSmoke --verification-profile ReleaseEntitled requires a Release build.");
        if (VerificationProfile == ReleaseEntitled)
        {
            Func<string, string?> envLookup = getEnv ?? Environment.GetEnvironmentVariable;
            Func<string, bool> existsCheck = fileExists ?? File.Exists;
            string? candidate = LicenseFixturePath ?? envLookup(LicenseFixtureEnvVar);
            if (string.IsNullOrWhiteSpace(candidate) || !existsCheck(candidate))
            {
                throw new BlockedExternalException(
                    "BLOCKED_EXTERNAL (NOT RUN): ReleaseEntitled requires an authorized signed QA license fixture (--license-fixture or SALRYN_QA_RELEASE_LICENSE_PATH). Never fabricate PASS without a signed entitlement.");
            }
            return Path.GetFullPath(candidate);
        }
        return null;
    }

    public void RequireCompatibleCheck(string checkName)
    {
        if (!IncludesRetailProcurement && IsRetailProcurementCheck(checkName))
            throw new InvalidOperationException(
                "NOT RUN: " + checkName + " is a preserved retail procurement proof. " +
                "Select --profile legacy-retail explicitly; Cafe schema must not acquire retail tables to satisfy this proof.");
    }

    private static string NormalizeVerificationProfile(string value) => value.Trim().ToLowerInvariant() switch
    {
        "releaseunlicensed" or "release-unlicensed" => ReleaseUnlicensed,
        "debugsynthetic" or "debug-synthetic" => DebugSynthetic,
        "releaseentitled" or "release-entitled" => ReleaseEntitled,
        _ => throw new InvalidOperationException(
            "CoreSmoke --verification-profile must be ReleaseUnlicensed, DebugSynthetic, or ReleaseEntitled.")
    };

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
        void Denied<TException>(Action action) where TException : Exception
        {
            try { action(); }
            catch (TException) { Require(true); return; }
            Require(false);
        }

        CoreSmokeOptions cafeDebug = CoreSmokeOptions.Parse([], isDebugBuildOverride: true);
        Require(cafeDebug.Profile == CoreSmokeOptions.Cafe && !cafeDebug.IncludesRetailProcurement);
        Require(cafeDebug.VerificationProfile == CoreSmokeOptions.DebugSynthetic);
        Require(cafeDebug.ValidateExecutionProfile(isDebugBuild: true) is null);

        CoreSmokeOptions cafeRelease = CoreSmokeOptions.Parse([], isDebugBuildOverride: false);
        Require(cafeRelease.VerificationProfile == CoreSmokeOptions.ReleaseUnlicensed);
        Require(cafeRelease.ValidateExecutionProfile(isDebugBuild: false) is null);

        foreach (string name in new[] { "lpi", "lpi-procure-to-pay", "lpi-p1", "lpi-p1-integrity", "r3-a1a", "r3-a1a-pr-po-authority" })
            Denied<InvalidOperationException>(() => cafeDebug.RequireCompatibleCheck(name));

        CoreSmokeOptions legacy = CoreSmokeOptions.Parse(["--only", "lpi", "--profile", "LEGACY-RETAIL"], isDebugBuildOverride: true);
        Require(legacy.IncludesRetailProcurement && legacy.Only == "lpi");
        legacy.RequireCompatibleCheck("lpi");
        Require(true);

        CoreSmokeOptions selectedCafe = CoreSmokeOptions.Parse(["--profile", "cafe", "--only", "cafe-loopback-boundary"], isDebugBuildOverride: true);
        selectedCafe.RequireCompatibleCheck(selectedCafe.Only!);
        Require(selectedCafe.Only == "cafe-loopback-boundary" && !selectedCafe.IncludesRetailProcurement);

        // Verification profile separation & fail-closed checks
        CoreSmokeOptions debugExplicit = CoreSmokeOptions.Parse(["--verification-profile", "DebugSynthetic"], isDebugBuildOverride: false);
        Denied<InvalidOperationException>(() => debugExplicit.ValidateExecutionProfile(isDebugBuild: false));

        CoreSmokeOptions releaseUnlicensedExplicit = CoreSmokeOptions.Parse(["--verification-profile", "ReleaseUnlicensed"], isDebugBuildOverride: true);
        Denied<InvalidOperationException>(() => releaseUnlicensedExplicit.ValidateExecutionProfile(isDebugBuild: true));

        CoreSmokeOptions releaseEntitledMissing = CoreSmokeOptions.Parse(["--verification-profile", "ReleaseEntitled"], isDebugBuildOverride: false);
        Denied<InvalidOperationException>(() => releaseEntitledMissing.ValidateExecutionProfile(isDebugBuild: true));
        Denied<BlockedExternalException>(() => releaseEntitledMissing.ValidateExecutionProfile(isDebugBuild: false, getEnv: _ => null, fileExists: _ => false));
        string? entitledPath = releaseEntitledMissing.ValidateExecutionProfile(
            isDebugBuild: false,
            getEnv: _ => Path.Combine(Path.GetTempPath(), "synthetic-entitled-check.lic"),
            fileExists: _ => true);
        Require(!string.IsNullOrWhiteSpace(entitledPath));

        Denied<InvalidOperationException>(() => CoreSmokeOptions.Parse(["--profile", "unknown"]));
        Denied<InvalidOperationException>(() => CoreSmokeOptions.Parse(["--verification-profile", "unknown"]));
        Denied<InvalidOperationException>(() => CoreSmokeOptions.Parse(["--only"]));
        Denied<InvalidOperationException>(() => CoreSmokeOptions.Parse(["--unknown", "value"]));
        Denied<InvalidOperationException>(() => CoreSmokeOptions.Parse(["--profile", "cafe", "--profile", "legacy-retail"]));
        Console.WriteLine($"CoreSmoke profile boundary: PASS ({assertions} assertions; selection & profile gating only, no product acceptance).");
    }
}

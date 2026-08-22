namespace Salryn.Tests.SmokeProof;

/// <summary>
/// HARDEN-A1 catalog for the proof depth of key pre-beta lanes. The catalog is deliberately scoped
/// to the lanes that later beta/cloud planning is most likely to overstate.
/// </summary>
public static class SmokeProofCatalog
{
    private static readonly SmokeProofRecord[] Records =
    {
        new(
            "licensing-m1-local-license-foundation",
            SmokeProofClass.SyntheticBehavior,
            "Local license status contracts, validator decisions, file-store seams, and fail-closed behavior remain represented in smoke coverage.",
            "Does not prove a logged-in operator called /api/license/status in the running local API.",
            "Run authenticated local HTTP status checks against the real local API."),
        new(
            "licensing-m2-signed-license-install-import",
            SmokeProofClass.FileSystemBehavior,
            "Signed install/import, controlled replacement, backup-before-replace, and verifier fail-closed seams remain represented in smoke coverage.",
            "Does not prove a Windows operator install POST completed through loopback auth/permission guards on a real ProgramData path.",
            "Run live /api/license/install auth matrix and Windows replacement tests."),
        new(
            "licensing-m3-local-enforcement-boundaries",
            SmokeProofClass.SyntheticBehavior,
            "Feature decision contracts, denial reasons, passive API guard, and entitlements source seams remain represented in smoke coverage.",
            "Does not prove product workflows are locked by licensing; M3 is a passive boundary unless explicitly applied later.",
            "Run live /api/license/entitlements checks and decide future endpoint gate policy."),
        new(
            "remote-pos-lan-boundary",
            SmokeProofClass.RuntimeComposition,
            "Remote POS authorization source and prior LAN runtime checks represent terminal, handshake, session, and command-matrix boundaries.",
            "Does not prove multi-terminal durability under process restart, network drop, or real client browser timing.",
            "Run a manual two-terminal soak/restart proof pack."),
        new(
            "production-report-export",
            SmokeProofClass.FileSystemBehavior,
            "Native PDF artifact roots, safe relative paths, hash/manifest seams, and permission source seams are represented.",
            "Does not prove operator-facing export/open/print/download workflow or printer behavior.",
            "Run live report export and artifact-open proof with permission denial cases."),
        new(
            "installer-release-packaging",
            SmokeProofClass.ManualProofRequired,
            "Installer lane is protected and documented separately.",
            "Does not prove install/uninstall/update, ProgramData preservation, shortcuts, or release asset quality from this hardening branch.",
            "Run installer lane read-only audit and later real-machine installer proof."),
        new(
            "rmo-bir-esales-external-compliance",
            SmokeProofClass.ManualProofRequired,
            "RMO/BIR engineering candidate seams and non-claim language are present in repo history.",
            "Does not prove BIR approval, eSales production transmission, legal acceptance, or TWG checklist acceptance.",
            "Keep frozen until external checklist/manual review is performed by Kirch.")
    };

    public static IReadOnlyList<SmokeProofRecord> All => Records;

    public static SmokeProofRecord? Find(string checkName) => Records.FirstOrDefault(
        record => string.Equals(record.CheckName, checkName, StringComparison.OrdinalIgnoreCase));
}

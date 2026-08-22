namespace Salryn.Core.Services.Licensing;

/// <summary>
/// Non-executable contract policy for local licensing. It exists so future beta, installer,
/// issuer, and support work do not accidentally convert Salryn into an internet-dependent app.
/// </summary>
public static class LocalLicenseContractPolicy
{
    public const bool RuntimeMustWorkOffline = true;
    public const bool LocalAppRequiresNetworkForVerification = false;
    public const bool PrivateSigningKeyAllowedInRepository = false;
    public const bool LocalRuntimeMayDependOnRecurringBillingState = false;
    public const bool ExternalIssuerMayCreateSignedLicenseFiles = true;

    /// <summary>Fixed application identity required on beta production licenses.</summary>
    public const string RequiredApplicationId = "Salryn.POS";

    /// <summary>Manual beta activation contact (no cloud activation server).</summary>
    public const string BetaActivationContactEmail = "salryn.info@gmail.com";

    public const int BetaLicenseSchemaVersion = 2;

    public const string LocalVerificationPrinciple =
        "The local app verifies signed license files offline and must not require an internet connection to run.";

    public const string IssuerSeparationPrinciple =
        "Future issuer, registry, release metadata, and support operations must stay separate from local runtime enforcement.";

    public const string KeyHygienePrinciple =
        "Private signing material must never be committed to this repository; local verification may use public verification material only.";

    public const string HostTerminalSeparationPrinciple =
        "Host and Terminal licenses are separate machine-bound documents; a host license is not terminal proof and a terminal license does not unlock host POS.";
}

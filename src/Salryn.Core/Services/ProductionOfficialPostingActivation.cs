namespace Salryn.Core.Services;

/// <summary>
/// Production official posting activation switch for Traditional CRM/POS.
/// Default OFF. Enabling this flag is not accreditation, CoA, PTU, or legal authority to issue.
/// </summary>
public static class ProductionOfficialPostingActivation
{
    public const string PolicyVersion = "TRAD-M2-PRODUCTION-ACTIVATION-2026-07-10";

    /// <summary>
    /// Master switch. Remains false until Kirch explicitly productizes activation after accreditation conditions.
    /// Tests must not flip this for production builds; use host facade override only in isolated checks.
    /// </summary>
    public static bool IsEnabled { get; private set; }

    public const string DisabledCode = "AtomicOfficialPosting.ActivationDisabled";

    public const string DisabledMessage =
        "Production official posting activation is OFF. Atomic host posting is blocked. " +
        "This is not BIR accreditation and does not authorize official invoice issuance.";

    public const string NotAComplianceClaimNote =
        "Activation flag is an engineering safety switch only — not compliance, CoA, or PTU.";

    /// <summary>
    /// Test-only override. Production composition must never call this.
    /// Public for CoreSmoke/gate assembly access without InternalsVisibleTo.
    /// </summary>
    public static void SetEnabledForTests(bool enabled) => IsEnabled = enabled;

    /// <summary>
    /// Reset to fail-closed default after tests.
    /// </summary>
    public static void ResetForTests() => IsEnabled = false;
}

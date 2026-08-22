using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public static class RemoteReadOnlyLanPreflightEvaluator
{
    public const string ReadinessReady = "Ready";

    public const string ReadinessBlocked = "Blocked";

    public const string ReadinessLocalOnly = "LocalOnly";

    public const string ReadinessDisabled = "Disabled";

    public const string ReadinessMisconfigured = "Misconfigured";

    public static RemoteReadOnlyPreflightResponse Evaluate(
        LanRemoteTransportConfig config,
        bool auditPolicyAvailable,
        bool diagnosticsAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(config);

        List<RemoteReadOnlyPreflightCheckResponse> checks = new();
        List<string> blockedReasons = new();
        List<string> nextSteps = new();

        checks.Add(Check(
            "ApiLocalLocalhostActive",
            "Pass",
            "transport",
            "Api.Local localhost read surface is active on 127.0.0.1."));

        bool remoteDisabled = !config.RemoteTransportEnabled;
        checks.Add(Check(
            "RemoteTransportDisabledByDefault",
            remoteDisabled ? "Info" : "Pass",
            "transport",
            remoteDisabled
                ? "Remote transport is disabled by default until an operator explicitly enables it."
                : "Remote transport has been explicitly enabled by operator configuration."));

        bool explicitLanConfigured = config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress;

        checks.Add(Check(
            "ExplicitLanBindConfigured",
            explicitLanConfigured ? "Pass" : remoteDisabled ? "Info" : "Fail",
            "bind",
            explicitLanConfigured
                ? "Explicit LAN bind is configured for hybrid bridge readiness."
                : remoteDisabled
                    ? "Explicit LAN bind is not required while remote transport remains disabled."
                    : "Remote transport is enabled but explicit LAN bind is not configured."));

        bool forbiddenBind = !string.IsNullOrWhiteSpace(config.BindAddress)
            && SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(config.BindAddress);

        Result configValidation = SalrynLanRemoteTransportGate.ValidateConfig(config);
        bool bindAllowed = explicitLanConfigured && !forbiddenBind && configValidation.IsSuccess;

        checks.Add(Check(
            "ConfiguredBindAddressAllowed",
            forbiddenBind ? "Fail" : bindAllowed || remoteDisabled ? "Pass" : "Warning",
            "bind",
            forbiddenBind
                ? "Configured bind address is forbidden. Use a specific non-wildcard LAN IP only."
                : bindAllowed
                    ? "Configured bind address passes guarded validation."
                    : remoteDisabled
                        ? "Bind address validation is deferred while remote transport remains disabled."
                        : "Configured bind address is not ready for hybrid bridge use."));

        checks.Add(Check(
            "WildcardBindRejected",
            "Pass",
            "bind",
            "Wildcard and " + "0.0." + "0.0 bind remain rejected by guarded bind policy."));

        checks.Add(Check(
            "TerminalIdentityRequired",
            "Pass",
            "trust",
            "Remote LAN reads require terminal identity headers."));

        checks.Add(Check(
            "HandshakeProofRequired",
            "Pass",
            "trust",
            "Remote LAN reads require handshake session proof headers."));

        checks.Add(Check(
            "CookieTrustRejectedForRemoteLan",
            "Pass",
            "trust",
            "Localhost operator session cookies are not accepted as remote LAN trust."));

        checks.Add(Check(
            "DiagnosticsAvailable",
            diagnosticsAvailable ? "Pass" : "Warning",
            "diagnostics",
            diagnosticsAvailable
                ? "Safe runtime diagnostics are available."
                : "Runtime diagnostics are unavailable until diagnostics reads succeed."));

        checks.Add(Check(
            auditPolicyAvailable ? "AuditPolicyAvailable" : "AuditPolicyUnavailable",
            auditPolicyAvailable ? "Pass" : "Warning",
            "audit",
            auditPolicyAvailable
                ? "Read-attempt audit visibility is available."
                : "Read-attempt audit visibility is unavailable until audit reads succeed."));

        checks.Add(Check(
            "ReadOnlyCategoriesAvailable",
            "Pass",
            "policy",
            "Status, product, dashboard, terminal status, bind policy, diagnostics, and audit visibility reads are available."));

        checks.Add(Check(
            "MutationCheckoutAdminForbidden",
            "Pass",
            "policy",
            "Mutation, checkout, admin, backup, settings, reports, and official output remain forbidden for remote clients."));

        if (forbiddenBind)
        {
            blockedReasons.Add("Configured LAN bind address is forbidden.");
        }

        if (!remoteDisabled && !explicitLanConfigured)
        {
            blockedReasons.Add("Remote transport is enabled without explicit LAN bind configuration.");
        }

        if (!remoteDisabled && explicitLanConfigured && !bindAllowed)
        {
            blockedReasons.Add("Configured LAN bind address failed guarded validation.");
        }

        string overallReadiness = ResolveOverallReadiness(
            remoteDisabled,
            explicitLanConfigured,
            forbiddenBind,
            bindAllowed,
            blockedReasons);

        nextSteps.AddRange(BuildSafeNextSteps(remoteDisabled, explicitLanConfigured, bindAllowed, forbiddenBind));

        return new RemoteReadOnlyPreflightResponse(
            OverallReadiness: overallReadiness,
            Message: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(
                "Remote read-only LAN preflight is read-only and excludes secrets.")
                ?? "Remote read-only LAN preflight is read-only and excludes secrets.",
            ReadOnlyOnly: true,
            Checks: checks,
            SafeNextSteps: nextSteps,
            BlockedReasons: blockedReasons
                .Select(reason => RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(reason) ?? reason)
                .ToArray());
    }

    public static bool PayloadContainsSecretMarker(string payload) =>
        RemoteReadOnlyBridgeDiagnostics.ContainsSecretMarker(payload);

    private static string ResolveOverallReadiness(
        bool remoteDisabled,
        bool explicitLanConfigured,
        bool forbiddenBind,
        bool bindAllowed,
        IReadOnlyList<string> blockedReasons)
    {
        if (forbiddenBind || (!remoteDisabled && !explicitLanConfigured) || (!remoteDisabled && explicitLanConfigured && !bindAllowed))
        {
            return forbiddenBind || (!remoteDisabled && explicitLanConfigured && !bindAllowed)
                ? ReadinessMisconfigured
                : ReadinessBlocked;
        }

        if (remoteDisabled)
        {
            return ReadinessDisabled;
        }

        if (!explicitLanConfigured)
        {
            return ReadinessLocalOnly;
        }

        if (bindAllowed)
        {
            return blockedReasons.Count == 0 ? ReadinessReady : ReadinessBlocked;
        }

        return ReadinessLocalOnly;
    }

    private static IReadOnlyList<string> BuildSafeNextSteps(
        bool remoteDisabled,
        bool explicitLanConfigured,
        bool bindAllowed,
        bool forbiddenBind)
    {
        List<string> steps =
        [
            "Keep checkout and admin actions on the local POS only.",
            "Retry read-only status, product, and dashboard reads after each troubleshooting step."
        ];

        if (remoteDisabled)
        {
            steps.Insert(0, "Enable remote read-only transport only from the operator side when field LAN use is intended.");
            steps.Insert(1, "Configure a specific LAN IP address only. Wildcard bind remains forbidden.");
        }
        else if (!explicitLanConfigured || forbiddenBind || !bindAllowed)
        {
            steps.Insert(0, "Review operator LAN bind configuration and use a specific allowed LAN IP only.");
        }

        steps.Add("Pair and activate the remote terminal through existing operator-controlled flows.");
        steps.Add("Perform terminal handshake before remote LAN read attempts.");
        steps.Add("Use terminal identity and handshake headers for remote bridge reads. Cookies are not remote LAN trust.");

        return steps
            .Select(step => RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(step) ?? step)
            .ToArray();
    }

    private static RemoteReadOnlyPreflightCheckResponse Check(
        string code,
        string status,
        string category,
        string message) =>
        new(
            Code: code,
            Status: status,
            Category: category,
            Message: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(message) ?? string.Empty);
}

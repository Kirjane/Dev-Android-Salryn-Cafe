using System.Collections.Concurrent;
using System.Text;

namespace Salryn.Api.Local.Services;

/// <summary>
/// LAN-M15-E: fail-closed, in-memory hardening for the remote read-only LAN surface.
///
/// This component adds no schema and no new persistent storage. It provides:
/// * bounded validators for hostile / oversized / malformed request material,
/// * an allow-list for safe read categories,
/// * hostile-string sanitization for anything surfaced to operators or audit,
/// * a process-local soft-throttle that <em>counts and flags</em> repeated denials.
///
/// The soft-throttle deliberately never hard-locks a terminal. The authorization
/// decision itself stays fail-closed; the throttle only annotates repeated-denial
/// behavior so a spoofed origin can never be used to lock out a legitimate terminal.
/// </summary>
public static class RemoteReadOnlyAbuseGuard
{
    public const int MaxHeaderLength = 512;

    public const int MaxSanitizedTextLength = 256;

    public const int SoftThrottleThreshold = 20;

    public static readonly TimeSpan SoftThrottleWindow = TimeSpan.FromMinutes(1);

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "product",
        "dashboard",
        "terminal-status",
        "capabilities",
        "bind-policy",
        "audit",
        "diagnostics",
        "preflight"
    };

    private static readonly ConcurrentDictionary<string, DenialWindow> DenialWindows = new(StringComparer.Ordinal);

    public static bool IsHeaderValueWithinLimit(string? value) =>
        value is null || value.Length <= MaxHeaderLength;

    public static bool IsAcceptableCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category) && AllowedCategories.Contains(category.Trim());

    public static bool TryNormalizeTerminalId(string? raw, out Guid terminalId)
    {
        terminalId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return Guid.TryParse(raw.Trim(), out terminalId) && terminalId != Guid.Empty;
    }

    /// <summary>
    /// Removes control characters, collapses whitespace, caps length, and redacts secret markers.
    /// Safe for operator display and audit reasons. Never throws on hostile input.
    /// </summary>
    public static string SanitizeHostileText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        bool previousWhitespace = false;

        foreach (char character in value)
        {
            char safeChar = character;
            if (char.IsControl(safeChar) || char.IsWhiteSpace(safeChar))
            {
                safeChar = ' ';
            }

            if (safeChar == ' ')
            {
                if (previousWhitespace)
                {
                    continue;
                }

                previousWhitespace = true;
            }
            else
            {
                previousWhitespace = false;
            }

            builder.Append(safeChar);
        }

        string collapsed = builder.ToString().Trim();

        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        if (RemoteReadOnlyBridgeDiagnostics.ContainsSecretMarker(collapsed))
        {
            return "[redacted]";
        }

        return collapsed.Length <= MaxSanitizedTextLength
            ? collapsed
            : collapsed[..MaxSanitizedTextLength];
    }

    public static string BuildDenialKey(Guid terminalId, string? origin)
    {
        string terminalPart = terminalId == Guid.Empty ? "anonymous-terminal" : terminalId.ToString();
        string originPart = SanitizeHostileText(origin);
        if (originPart.Length == 0 || originPart == "[redacted]")
        {
            originPart = "unknown-origin";
        }

        return terminalPart + "|" + originPart;
    }

    public static SoftThrottleDecision RegisterDenial(string key, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new SoftThrottleDecision(0, false);
        }

        DenialWindow window = DenialWindows.AddOrUpdate(
            key,
            _ => new DenialWindow(now, 1),
            (_, existing) => existing.WindowStart + SoftThrottleWindow < now
                ? new DenialWindow(now, 1)
                : new DenialWindow(existing.WindowStart, existing.Count + 1));

        return new SoftThrottleDecision(window.Count, window.Count >= SoftThrottleThreshold);
    }

    public static bool IsSoftThrottled(string key, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key) || !DenialWindows.TryGetValue(key, out DenialWindow window))
        {
            return false;
        }

        if (window.WindowStart + SoftThrottleWindow < now)
        {
            return false;
        }

        return window.Count >= SoftThrottleThreshold;
    }

    public static void Reset() => DenialWindows.Clear();

    private readonly record struct DenialWindow(DateTimeOffset WindowStart, int Count);
}

public readonly record struct SoftThrottleDecision(int DenialCount, bool IsSoftThrottled);

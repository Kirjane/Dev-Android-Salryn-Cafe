using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Services;

internal static class LanM15RemoteReadOnlyLanNegativePathHardeningSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15-E REMOTE READ-ONLY NEGATIVE-PATH HARDENING SMOKE CHECKS =====");

        RequireHeaderLengthLimit();
        RequireCategoryAllowList();
        RequireMalformedGuidRejection();
        RequireHostileStringSanitization();
        RequireOversizedAndMalformedBridgeClassification();
        RequireSoftThrottleCountsWithoutHardLock();

        Console.WriteLine("LAN-M15-E remote read-only negative-path hardening smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireHeaderLengthLimit()
    {
        Require(RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(new string('a', RemoteReadOnlyAbuseGuard.MaxHeaderLength)), "A header at the max length must be accepted.");
        Require(!RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(new string('a', RemoteReadOnlyAbuseGuard.MaxHeaderLength + 1)), "An oversized header must be rejected.");
        Require(RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(null), "A missing header must not be treated as oversized.");
    }

    private static void RequireCategoryAllowList()
    {
        foreach (string allowed in new[] { "status", "product", "dashboard", "terminal-status", "capabilities", "bind-policy", "audit", "diagnostics", "preflight" })
        {
            Require(RemoteReadOnlyAbuseGuard.IsAcceptableCategory(allowed), $"Category '{allowed}' must be allowed.");
        }

        foreach (string denied in new[] { "checkout", "mutation", "admin", "backup", "settings", "official", "", "   " })
        {
            Require(!RemoteReadOnlyAbuseGuard.IsAcceptableCategory(denied), $"Category '{denied}' must be rejected.");
        }
    }

    private static void RequireMalformedGuidRejection()
    {
        Require(!RemoteReadOnlyAbuseGuard.TryNormalizeTerminalId("not-a-guid", out _), "A malformed GUID must be rejected.");
        Require(!RemoteReadOnlyAbuseGuard.TryNormalizeTerminalId(Guid.Empty.ToString(), out _), "An empty GUID must be rejected.");
        Require(!RemoteReadOnlyAbuseGuard.TryNormalizeTerminalId(null, out _), "A null terminal id must be rejected.");
        Require(RemoteReadOnlyAbuseGuard.TryNormalizeTerminalId(Guid.NewGuid().ToString(), out Guid parsed) && parsed != Guid.Empty, "A valid GUID must be accepted.");
    }

    private static void RequireHostileStringSanitization()
    {
        string sanitizedControl = RemoteReadOnlyAbuseGuard.SanitizeHostileText("bad\u0000value\r\n\tinjected");
        Require(!sanitizedControl.Contains('\u0000'), "Control characters must be stripped.");
        Require(!sanitizedControl.Contains('\r') && !sanitizedControl.Contains('\n') && !sanitizedControl.Contains('\t'), "Line/tab control characters must be stripped.");

        Require(RemoteReadOnlyAbuseGuard.SanitizeHostileText("token=abc") == "[redacted]", "Secret markers must be redacted.");
        Require(RemoteReadOnlyAbuseGuard.SanitizeHostileText("Data Source=C:/x/store.db") == "[redacted]", "Connection strings must be redacted.");
        Require(RemoteReadOnlyAbuseGuard.SanitizeHostileText(new string('a', 1000)).Length <= RemoteReadOnlyAbuseGuard.MaxSanitizedTextLength, "Oversized text must be capped.");
        Require(RemoteReadOnlyAbuseGuard.SanitizeHostileText(null).Length == 0, "Null text must sanitize to empty.");
    }

    private static void RequireOversizedAndMalformedBridgeClassification()
    {
        DefaultHttpContext oversized = new();
        oversized.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.90");
        oversized.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = new string('a', RemoteReadOnlyAbuseGuard.MaxHeaderLength + 5);
        RemoteReadOnlyBridgeRequestContext oversizedContext = RemoteReadOnlyBridgeRequestContext.FromHttpContext(oversized);
        Require(oversizedContext.HasOversizedHeader, "An oversized terminal header must be classified as oversized.");

        DefaultHttpContext malformed = new();
        malformed.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.91");
        malformed.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = "not-a-guid";
        malformed.Request.Headers[RemoteReadOnlyBridgeHeaders.HandshakeSessionId] = "also-bad";
        RemoteReadOnlyBridgeRequestContext malformedContext = RemoteReadOnlyBridgeRequestContext.FromHttpContext(malformed);
        Require(malformedContext.HasMalformedTerminalId, "A malformed terminal GUID must be classified as malformed.");
        Require(malformedContext.HasMalformedHandshakeId, "A malformed handshake GUID must be classified as malformed.");
        Require(malformedContext.TerminalId == Guid.Empty, "A malformed terminal GUID must resolve to empty.");
        Require(malformedContext.HandshakeSessionId == Guid.Empty, "A malformed handshake GUID must resolve to empty.");

        DefaultHttpContext hostileOrigin = new();
        hostileOrigin.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.92");
        hostileOrigin.Request.Headers[RemoteReadOnlyBridgeHeaders.RemoteOrigin] = "origin\r\nInjected: token=abc";
        RemoteReadOnlyBridgeRequestContext hostileContext = RemoteReadOnlyBridgeRequestContext.FromHttpContext(hostileOrigin);
        Require(!hostileContext.OriginAddress.Contains('\n') && !hostileContext.OriginAddress.Contains('\r'), "A hostile origin header must be sanitized of control characters.");
        Require(hostileContext.OriginAddress == "[redacted]", "A hostile origin header containing a secret marker must be redacted.");
    }

    private static void RequireSoftThrottleCountsWithoutHardLock()
    {
        RemoteReadOnlyAbuseGuard.Reset();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string key = RemoteReadOnlyAbuseGuard.BuildDenialKey(Guid.NewGuid(), "192.168.1.93");

        Require(!RemoteReadOnlyAbuseGuard.IsSoftThrottled(key, now), "A fresh key must not be soft-throttled.");

        SoftThrottleDecision decision = new(0, false);
        for (int attempt = 0; attempt < RemoteReadOnlyAbuseGuard.SoftThrottleThreshold; attempt++)
        {
            decision = RemoteReadOnlyAbuseGuard.RegisterDenial(key, now);
        }

        Require(decision.IsSoftThrottled, "Reaching the denial threshold must flag soft-throttle.");
        Require(RemoteReadOnlyAbuseGuard.IsSoftThrottled(key, now), "Soft-throttle state must be observable within the window.");

        // The soft-throttle only flags; it never hard-locks. A window in the future must clear it.
        Require(!RemoteReadOnlyAbuseGuard.IsSoftThrottled(key, now.Add(RemoteReadOnlyAbuseGuard.SoftThrottleWindow).AddSeconds(1)), "Soft-throttle must expire after the window; there is no hard lock.");

        RemoteReadOnlyAbuseGuard.Reset();
        Require(!RemoteReadOnlyAbuseGuard.IsSoftThrottled(key, now), "Reset must clear soft-throttle state.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

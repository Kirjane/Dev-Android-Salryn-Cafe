using System.Net;
using System.Net.Sockets;
using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Normalizes host LAN bind input to a bare IPv4 address. Rejects wildcards and
/// repairs operator mistakes such as pasting a full URL into the bind field.
/// </summary>
public static class LocalApiLanBindNormalizer
{
    public sealed record NormalizeResult(
        bool Ok,
        string? Ip,
        bool WasRepaired,
        string? ErrorCode,
        string? ErrorMessage,
        string? RawInput);

    public static NormalizeResult NormalizeConfiguredLanBindInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Fail("LanBind.BindAddressRequired", "An explicit LAN bind IP address is required.", input);
        }

        string raw = input.Trim();
        string candidate = raw;
        bool repaired = false;

        // Operators sometimes paste a full client/LAN URL into the bind field.
        if (candidate.Contains("://", StringComparison.Ordinal)
            || candidate.Contains('/', StringComparison.Ordinal)
            || candidate.StartsWith('[')
            || (candidate.Count(c => c == ':') == 1 && candidate.Contains('.', StringComparison.Ordinal)))
        {
            if (TryExtractIpv4FromLooseInput(candidate, out string? extracted))
            {
                repaired = !string.Equals(extracted, raw, StringComparison.OrdinalIgnoreCase);
                candidate = extracted!;
            }
        }

        if (SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(candidate))
        {
            return Fail(
                "LanBind.WildcardForbidden",
                "Wildcard bind addresses are forbidden. Use an explicit LAN IPv4 address assigned to this host.",
                raw);
        }

        if (SalrynHostCommandLoopbackRules.IsLoopbackOrigin(candidate))
        {
            return Fail(
                "LanBind.LoopbackForbidden",
                "LAN client bind must use a non-loopback IPv4 address assigned to this host.",
                raw);
        }

        if (!IPAddress.TryParse(candidate, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return Fail(
                "LanBind.InvalidIpv4",
                "LAN bind must be an explicit IPv4 address assigned to this host (example: 192.168.1.161).",
                raw);
        }

        string ip = parsed.ToString();
        if (!string.Equals(ip, raw, StringComparison.OrdinalIgnoreCase))
        {
            repaired = true;
        }

        return new NormalizeResult(
            Ok: true,
            Ip: ip,
            WasRepaired: repaired,
            ErrorCode: null,
            ErrorMessage: null,
            RawInput: raw);
    }

    public static Result ValidateForSave(string? input)
    {
        NormalizeResult normalized = NormalizeConfiguredLanBindInput(input);
        if (!normalized.Ok || string.IsNullOrWhiteSpace(normalized.Ip))
        {
            return Result.Fail(Error.Validation(
                normalized.ErrorCode ?? "LanBind.Invalid",
                normalized.ErrorMessage ?? "Invalid LAN bind address."));
        }

        return LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind(normalized.Ip);
    }

    public static string? TryNormalizeOrNull(string? input)
    {
        NormalizeResult result = NormalizeConfiguredLanBindInput(input);
        return result.Ok ? result.Ip : null;
    }

    public static bool LooksLikeBareIpv4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return IPAddress.TryParse(trimmed, out IPAddress? parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork
            && !trimmed.Contains(':', StringComparison.Ordinal)
            && !trimmed.Contains('/', StringComparison.Ordinal);
    }

    private static bool TryExtractIpv4FromLooseInput(string input, out string? ipv4)
    {
        ipv4 = null;
        string candidate = input.Trim().TrimEnd('/');

        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? absolute)
            && absolute.HostNameType == UriHostNameType.IPv4)
        {
            ipv4 = absolute.Host;
            return true;
        }

        // Handle host:port without scheme (192.168.1.161:5299)
        if (Uri.TryCreate("http://" + candidate.TrimStart('/'), UriKind.Absolute, out Uri? withScheme)
            && withScheme.HostNameType == UriHostNameType.IPv4)
        {
            ipv4 = withScheme.Host;
            return true;
        }

        // Last resort: first IPv4-looking token
        string[] tokens = candidate.Split(new[] { '/', ' ', '?', '#' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            string hostPart = token.Contains(':', StringComparison.Ordinal)
                ? token.Split(':', 2)[0]
                : token;
            if (IPAddress.TryParse(hostPart, out IPAddress? parsed)
                && parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                ipv4 = parsed.ToString();
                return true;
            }
        }

        return false;
    }

    private static NormalizeResult Fail(string code, string message, string? raw) =>
        new(false, null, false, code, message, raw);
}

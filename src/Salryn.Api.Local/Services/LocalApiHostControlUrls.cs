namespace Salryn.Api.Local.Services;

public sealed class LocalApiHostControlUrls
{
    public LocalApiHostControlUrls(string localUrl)
    {
        LocalUrl = string.IsNullOrWhiteSpace(localUrl)
            ? "http://127.0.0.1:5299"
            : localUrl.Trim();
        Port = ExtractPort(LocalUrl) ?? 5299;
    }

    public string LocalUrl { get; }

    public int Port { get; }

    public string BuildLanUrl(string bindAddress)
    {
        // Always require bare IPv4. Never prefix a value that already contains a scheme
        // (that produced human-confusing http://http://.../:port URLs).
        string? ip = LocalApiLanBindNormalizer.TryNormalizeOrNull(bindAddress)
            ?? (LocalApiLanBindNormalizer.LooksLikeBareIpv4(bindAddress) ? bindAddress.Trim() : null);

        if (ip is null)
        {
            throw new ArgumentException(
                "LAN URL requires a bare IPv4 bind address (example: 192.168.1.161).",
                nameof(bindAddress));
        }

        return $"http://{ip}:{Port}";
    }

    public string BuildClientUrl(string bindAddress) => $"{BuildLanUrl(bindAddress)}/remote-client";

    private static int? ExtractPort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Port > 0
            ? uri.Port
            : null;
    }
}

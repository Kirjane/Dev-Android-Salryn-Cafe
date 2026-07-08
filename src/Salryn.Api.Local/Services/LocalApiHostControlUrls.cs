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

    public string BuildLanUrl(string bindAddress) => $"http://{bindAddress.Trim()}:{Port}";

    public string BuildClientUrl(string bindAddress) => $"{BuildLanUrl(bindAddress)}/remote-client";

    private static int? ExtractPort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Port > 0
            ? uri.Port
            : null;
    }
}

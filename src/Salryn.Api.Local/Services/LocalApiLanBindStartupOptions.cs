namespace Salryn.Api.Local.Services;

public sealed class LocalApiLanBindStartupOptions
{
    public string? LanBindIp { get; init; }

    public int? Port { get; init; }

    public bool HasLanBind => !string.IsNullOrWhiteSpace(LanBindIp);

    public static LocalApiLanBindStartupOptions Parse(string[] args)
    {
        string? lanBindIp = Environment.GetEnvironmentVariable("SALRYN_LAN_BIND_IP")?.Trim();
        string? portText = Environment.GetEnvironmentVariable("SALRYN_PORT")?.Trim();

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index].Trim();
            if (arg.Equals("--lan-bind", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                lanBindIp = args[++index].Trim();
                continue;
            }

            if (arg.StartsWith("--lan-bind=", StringComparison.OrdinalIgnoreCase))
            {
                lanBindIp = arg["--lan-bind=".Length..].Trim();
                continue;
            }

            if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                portText = args[++index].Trim();
                continue;
            }

            if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                portText = arg["--port=".Length..].Trim();
            }
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out int parsedPort) && parsedPort > 0)
        {
            port = parsedPort;
        }

        return new LocalApiLanBindStartupOptions
        {
            LanBindIp = string.IsNullOrWhiteSpace(lanBindIp) ? null : lanBindIp,
            Port = port,
        };
    }

    public static string BuildLocalUrl(int? port)
    {
        return port is > 0
            ? $"http://127.0.0.1:{port.Value}"
            : Salryn.Infrastructure.Paths.SalrynProductIdentity.ResolveLoopbackApiBaseUrl();
    }
}
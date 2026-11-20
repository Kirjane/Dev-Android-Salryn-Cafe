using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Core.Host;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

// Exercises the Cafe runtime contract without opening a database or starting a listener.
public static class CafeLoopbackBoundaryChecks
{
    public static void Run()
    {
        int assertions = 0;
        void Require(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        string? priorUrl = Environment.GetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, null);
            LocalApiApplicationOptions options = new();
            Uri url = new(options.Url);
            Require(SalrynProductIdentity.ProductId == "Salryn.Cafe", "These checks must target Cafe identity.");
            Require(url.Scheme == Uri.UriSchemeHttp && url.Host == "127.0.0.1" && url.Port == 5399,
                "Cafe API must default to HTTP loopback port 5399.");
            Require(!options.EnableLanRemoteTransportForInternalQa, "LAN transport must require explicit opt-in.");

            LanRemoteTransportConfig defaults = LanRemoteTransportConfig.CreateDefault();
            IReadOnlyList<string> urls = LocalApiLanUrlResolver.ResolveListenUrls(options.Url, defaults);
            Require(urls.Count == 1 && urls[0] == options.Url, "Default listen URLs must expose only Cafe loopback.");

            foreach (string rejected in new[]
            {
                "http://127.0.0.1:5299", "http://localhost:5387", "http://0.0.0.0:5399",
                "http://192.0.2.1:5399", "https://127.0.0.1:5399"
            })
            {
                Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, rejected);
                Require(new LocalApiApplicationOptions().Url == options.Url,
                    "Protected retail ports and non-loopback overrides must fall back to Cafe loopback: " + rejected);
            }

            LanRemoteTransportConfig wildcard = defaults with
            {
                RemoteTransportEnabled = true,
                BindMode = LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress = "0.0.0.0"
            };
            IReadOnlyList<string> wildcardUrls = LocalApiLanUrlResolver.ResolveListenUrls(options.Url, wildcard);
            Require(wildcardUrls.Count == 1 && wildcardUrls[0] == options.Url, "Wildcard LAN configuration must not add a listener.");

            DefaultHttpContext context = new();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            Require(LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(context), "Local operator requests must remain available.");
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.2");
            Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(context), "LAN callers must not gain local operator authority.");
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers.Origin = "https://example.invalid";
            Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(context), "Cross-origin requests must not gain local operator authority.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, priorUrl);
        }

        Console.WriteLine($"Cafe loopback boundary: PASS ({assertions} assertions; no database or listener).");
    }
}

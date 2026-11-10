using Microsoft.AspNetCore.HttpOverrides;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Signal;

/// <summary>
/// Trusts the proxies in front of the signalling (Cloudflare, for example) to name the browser behind them.
/// </summary>
internal static class SignalProxies
{
    /// <summary>
    /// Builds the settings of the forwarded headers: only a request that comes from one of
    /// <see cref="SignalServerOptions.TrustedProxies"/> takes its address from <c>X-Forwarded-For</c>, and only the
    /// last entry of it, the one the proxy wrote; the entries before it were written by the browser and may lie.
    /// </summary>
    /// <param name="signal">The settings of the signalling.</param>
    /// <returns>The settings of the middleware.</returns>
    public static ForwardedHeadersOptions CreateOptions(SignalServerOptions signal)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1,
        };

        // The middleware trusts the loopback by default; here only the networks named in the settings are trusted.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in signal.ProxyNetworks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }
}

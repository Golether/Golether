using Golether.Core.Networking;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="SignalAddress"/>.
/// </summary>
public sealed class SignalAddressTests
{
    /// <summary>
    /// The sealed <c>host:port</c> of the project page becomes the WebSocket of the signalling at that server, an
    /// address <see cref="SignalAddress.TryParse"/> accepts.
    /// </summary>
    /// <param name="endpoint">The server.</param>
    /// <param name="expected">The address of its signalling.</param>
    [Theory]
    [InlineData("signal.example.org:8443", "wss://signal.example.org:8443/signal")]
    [InlineData("signal.example.org:443", "wss://signal.example.org/signal")]
    [InlineData("203.0.113.7:47943", "wss://203.0.113.7:47943/signal")]
    [InlineData("[2001:db8::5]:8443", "wss://[2001:db8::5]:8443/signal")]
    public void FromEndpoint_BuildsTheSignalling(string endpoint, string expected)
    {
        var address = SignalAddress.FromEndpoint(PeerEndpoint.Parse(endpoint));

        Assert.Equal(new Uri(expected), address);
        Assert.True(SignalAddress.TryParse(address.AbsoluteUri, out var parsed));
        Assert.Equal(address, parsed);
    }
}

using System.Net;
using Golether.Core.Networking;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of telling the addresses of the Internet from those of home networks, providers and this computer.
/// </summary>
public sealed class AddressScopeTests
{
    /// <summary>
    /// Only addresses reachable from other networks are public; a name counts as public.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="expected">Whether it is public.</param>
    [Theory]
    [InlineData("203.0.113.7", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2001:db8::5", true)]
    [InlineData("[2a00:1450::1]", true)]
    [InlineData("meet.example.org", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.127.255.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    public void IsPublicHost_KnowsTheScopes(string host, bool expected) => Assert.Equal(expected, AddressScope.IsPublicHost(host));

    /// <summary>
    /// The edges of the shared and private blocks are outside them.
    /// </summary>
    [Fact]
    public void BlockEdges_ArePublic()
    {
        Assert.True(AddressScope.IsPublic(IPAddress.Parse("100.63.255.255")));
        Assert.True(AddressScope.IsPublic(IPAddress.Parse("100.128.0.0")));
        Assert.True(AddressScope.IsPublic(IPAddress.Parse("172.15.255.255")));
        Assert.True(AddressScope.IsPublic(IPAddress.Parse("172.32.0.0")));
    }
}

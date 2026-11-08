using System.Security.Cryptography;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Transports.WebRtc;
using Golether.Web.Server.Enums;
using Golether.Web.Server.Options;
using Golether.Web.Server.Signal;

namespace Golether.Web.Server.Tests;

/// <summary>
/// Tests of the signalling of browsers: the signature of a registration, the holding of rooms and the settings.
/// </summary>
public sealed class SignalTests
{
    /// <summary>
    /// A registration signed as WebCrypto signs (P-256, SHA-256, r‖s) names the key that made it; a signature over
    /// another nonce or another room, a damaged signature and a key of another curve are refused.
    /// </summary>
    [Fact]
    public void Signature_ProvesTheKeyOfTheRoom()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var room = RendezvousPoint.CreateRoom();
        var signature = Sign(key, SignalRegistration.Payload("nonce-1", room));

        Assert.True(KeySignature.TryVerify(spki, signature, SignalRegistration.Payload("nonce-1", room), out var owner));
        Assert.Equal(PeerId.FromSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo()), owner);

        Assert.False(KeySignature.TryVerify(spki, signature, SignalRegistration.Payload("nonce-2", room), out _));
        Assert.False(KeySignature.TryVerify(spki, signature, SignalRegistration.Payload("nonce-1", RendezvousPoint.CreateRoom()), out _));
        Assert.False(KeySignature.TryVerify(spki, "AAAA", SignalRegistration.Payload("nonce-1", room), out _));
        Assert.False(KeySignature.TryVerify("not base64!", signature, SignalRegistration.Payload("nonce-1", room), out _));
        Assert.False(KeySignature.TryVerify(null, signature, SignalRegistration.Payload("nonce-1", room), out _));

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var otherSignature = Sign(other, SignalRegistration.Payload("nonce-1", room));
        Assert.False(KeySignature.TryVerify(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), otherSignature, SignalRegistration.Payload("nonce-1", room), out _));
    }

    /// <summary>
    /// A room belongs to the key that took it: the same key takes it again (the host came back) and the earlier
    /// holding is handed back to be closed; another key is refused; a full server takes no new room.
    /// </summary>
    [Fact]
    public void Registry_KeepsRoomsForTheirKeys()
    {
        var registry = new SignalRegistry(new SignalServerOptions { MaxRooms = 1 });
        using var socket = new SignalSocket(new System.Net.WebSockets.ClientWebSocket());
        var owner = PeerId.Parse(new string('a', 64));
        var stranger = PeerId.Parse(new string('b', 64));
        var first = new SignalRoom("room-1", owner, socket);
        var again = new SignalRoom("room-1", owner, socket);

        Assert.Equal(RoomClaim.Registered, registry.Claim(first, out var replaced));
        Assert.Null(replaced);
        Assert.Equal(RoomClaim.Taken, registry.Claim(new SignalRoom("room-1", stranger, socket), out _));
        Assert.Equal(RoomClaim.Full, registry.Claim(new SignalRoom("room-2", owner, socket), out _));
        Assert.Equal(RoomClaim.Registered, registry.Claim(again, out replaced));
        Assert.Same(first, replaced);

        registry.Release(first);
        Assert.Same(again, registry.Find("room-1"));
        registry.Release(again);
        Assert.Null(registry.Find("room-1"));
    }

    /// <summary>
    /// A room has a bounded number of meetings, and a meeting that is over is not found.
    /// </summary>
    [Fact]
    public void Room_BoundsItsMeetings()
    {
        using var socket = new SignalSocket(new System.Net.WebSockets.ClientWebSocket());
        var room = new SignalRoom("room", PeerId.Parse(new string('a', 64)), socket);

        Assert.True(room.TryAddMeeting("m1", socket, 2));
        Assert.True(room.TryAddMeeting("m2", socket, 2));
        Assert.False(room.TryAddMeeting("m3", socket, 2));

        room.RemoveMeeting("m1");
        Assert.Null(room.FindMeeting("m1"));
        Assert.Same(socket, room.FindMeeting("m2"));
        Assert.Null(room.FindMeeting(null));
    }

    /// <summary>
    /// A signal for a host whose socket is already released (the host left while a guest was meeting it) is dropped,
    /// not thrown at the connection of the guest.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ReleasedSocket_DropsSends()
    {
        var socket = new SignalSocket(new System.Net.WebSockets.ClientWebSocket());
        socket.Dispose();

        await socket.SendAsync(new SignalFrame { Type = SignalFrame.SIGNAL, Data = "{}" }, CancellationToken.None);
    }

    /// <summary>
    /// The settings are checked only when the signalling is on; pages not named are refused, a program without an
    /// origin is served.
    /// </summary>
    [Fact]
    public void Options_CheckAddressesAndOrigins()
    {
        Assert.Empty(new SignalServerOptions().Validate());
        Assert.Empty(new SignalServerOptions { Enabled = true }.Validate());
        Assert.Equal(47943, new SignalServerOptions().Port);

        var broken = new SignalServerOptions { Enabled = true, Url = "ftp://x", MaxRooms = 0, MeetingTimeout = TimeSpan.Zero };
        Assert.Equal(3, broken.Validate().Count);
        Assert.Empty((broken with { Enabled = false }).Validate());

        var options = new SignalServerOptions { AllowedOrigins = ["https://golether.github.io"] };
        Assert.True(options.IsAllowedOrigin("https://golether.github.io"));
        Assert.True(options.IsAllowedOrigin("HTTPS://GOLETHER.GITHUB.IO"));
        Assert.False(options.IsAllowedOrigin("https://evil.example"));
        Assert.True(options.IsAllowedOrigin(null));
    }

    /// <summary>
    /// Signs as WebCrypto does.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="payload">The signed bytes.</param>
    /// <returns>The signature in base64.</returns>
    private static string Sign(ECDsa key, byte[] payload)
        => Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}

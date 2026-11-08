using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golether.Transports.WebRtc;

/// <summary>
/// One message of the signalling of WebRTC connections, of browsers and applications alike: UTF-8 JSON in a WebSocket
/// text message. The type is in <c>$t</c>; the other fields are present only where the type needs them (see
/// <c>Docs/En/RendezvousServer.md</c>). The server and the clients share this one definition.
/// </summary>
public sealed record SignalFrame
{
    /// <summary>
    /// The first message of the server on every connection.
    /// </summary>
    public const string SERVER_HELLO = "server-hello";

    /// <summary>
    /// The host takes a room.
    /// </summary>
    public const string REGISTER = "register";

    /// <summary>
    /// The room is taken.
    /// </summary>
    public const string REGISTERED = "registered";

    /// <summary>
    /// A participant asks for a meeting with the host of a room.
    /// </summary>
    public const string CONNECT = "connect";

    /// <summary>
    /// The server tells the participant the number of its meeting.
    /// </summary>
    public const string MEETING = "meeting";

    /// <summary>
    /// The server tells the host that a participant is waiting.
    /// </summary>
    public const string INCOMING = "incoming";

    /// <summary>
    /// A description of the WebRTC connection or a candidate, passed to the other side as it is.
    /// </summary>
    public const string SIGNAL = "signal";

    /// <summary>
    /// The server tells the host that the participant of a meeting left.
    /// </summary>
    public const string CLOSED = "closed";

    /// <summary>
    /// A refusal; the connection is closed after it.
    /// </summary>
    public const string ERROR = "error";

    /// <summary>
    /// The version of the signalling protocol.
    /// </summary>
    public const int PROTOCOL_VERSION = 1;

    /// <summary>
    /// The options of the JSON: camelCase names, no empty fields.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Gets the message type.
    /// </summary>
    [JsonPropertyName("$t")]
    public string? Type { get; init; }

    /// <summary>
    /// Gets the protocol version (<see cref="SERVER_HELLO"/>).
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Gets the random single-use value the host signs (<see cref="SERVER_HELLO"/>).
    /// </summary>
    public string? Nonce { get; init; }

    /// <summary>
    /// Gets the room (<see cref="REGISTER"/>, <see cref="CONNECT"/>).
    /// </summary>
    public string? Room { get; init; }

    /// <summary>
    /// Gets the public key of the host: DER SubjectPublicKeyInfo in base64 (<see cref="REGISTER"/>).
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Gets the signature of the registration in base64 (<see cref="REGISTER"/>).
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Gets the meeting (<see cref="MEETING"/>, <see cref="INCOMING"/>, <see cref="SIGNAL"/> of the host,
    /// <see cref="CLOSED"/>).
    /// </summary>
    public string? Meeting { get; init; }

    /// <summary>
    /// Gets the content passed to the other side (<see cref="SIGNAL"/>); the server does not read it.
    /// </summary>
    public string? Data { get; init; }

    /// <summary>
    /// Gets the reason of a refusal, a name of <see cref="Core.Enums.RendezvousError"/> (<see cref="ERROR"/>).
    /// </summary>
    public string? Error { get; init; }
}

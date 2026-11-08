using System.Text;

namespace Golether.Transports.WebRtc;

/// <summary>
/// What a host signs to take a room in the signalling: the greeting of the server makes every signature fresh.
/// </summary>
public static class SignalRegistration
{
    /// <summary>
    /// The context that keeps this signature apart from every other one made with the device key.
    /// </summary>
    public const string CONTEXT = "golether-signal-v1";

    /// <summary>
    /// Builds the signed bytes: <c>golether-signal-v1\n&lt;nonce&gt;\n&lt;room&gt;</c>.
    /// </summary>
    /// <param name="nonce">The nonce of the greeting of the server.</param>
    /// <param name="room">The room.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] Payload(string nonce, string room) => Encoding.UTF8.GetBytes(CONTEXT + "\n" + nonce + "\n" + room);
}

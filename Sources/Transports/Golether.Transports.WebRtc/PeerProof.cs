using System.Text;
using System.Text.Json;
using Golether.Core.Identity;

namespace Golether.Transports.WebRtc;

/// <summary>
/// The proof each side of a WebRTC connection of a session gives in the <see cref="AUTH_LABEL"/> channel: a signature
/// with its device key over the DTLS fingerprints of both sides and the room. A server that swapped the descriptions
/// would bring other certificates, and the signature would not hold. Written exactly as <c>rtc.js</c> of the browser
/// writes it.
/// </summary>
public static class PeerProof
{
    /// <summary>
    /// The label of the channel of the proofs; the guest opens it first.
    /// </summary>
    public const string AUTH_LABEL = "golether-auth";

    /// <summary>
    /// The context that keeps this signature apart from every other one made with the device key.
    /// </summary>
    public const string CONTEXT = "golether-webrtc-v1";

    /// <summary>
    /// The longest proof message accepted, in characters.
    /// </summary>
    public const int MAX_MESSAGE = 2048;

    /// <summary>
    /// The beginning of a fingerprint line of a description.
    /// </summary>
    private const string FINGERPRINT_LINE = "a=fingerprint:";

    /// <summary>
    /// The JSON options of a proof message.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes the fingerprints of a description the way both sides write them: every fingerprint line, upper case,
    /// sorted, joined with commas.
    /// </summary>
    /// <param name="sdp">The description.</param>
    /// <returns>The fingerprints.</returns>
    public static string Fingerprints(string sdp)
    {
        ArgumentNullException.ThrowIfNull(sdp);
        var values = sdp.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(FINGERPRINT_LINE, StringComparison.Ordinal))
            .Select(line => line[FINGERPRINT_LINE.Length..].Trim().ToUpperInvariant())
            .Order(StringComparer.Ordinal);
        return string.Join(',', values);
    }

    /// <summary>
    /// Builds the signed text: <c>golether-webrtc-v1\n&lt;fingerprints of the host&gt;\n&lt;fingerprints of the
    /// guest&gt;\n&lt;room&gt;</c>.
    /// </summary>
    /// <param name="hostDescription">The description of the host.</param>
    /// <param name="guestDescription">The description of the guest.</param>
    /// <param name="room">The room.</param>
    /// <returns>The text.</returns>
    public static string Text(string hostDescription, string guestDescription, string room)
        => string.Join('\n', CONTEXT, Fingerprints(hostDescription), Fingerprints(guestDescription), room);

    /// <summary>
    /// Writes the proof of this device.
    /// </summary>
    /// <param name="signer">The device key.</param>
    /// <param name="text">The signed text.</param>
    /// <returns>The JSON message <c>{ "key": …, "signature": … }</c>.</returns>
    public static string Create(DeviceSigner signer, string text)
    {
        ArgumentNullException.ThrowIfNull(signer);
        return JsonSerializer.Serialize(new ProofMessage(signer.PublicKey, signer.Sign(Encoding.UTF8.GetBytes(text))), _json);
    }

    /// <summary>
    /// Reads the proof of the other side and checks it.
    /// </summary>
    /// <param name="message">The JSON message.</param>
    /// <param name="text">The text it must sign.</param>
    /// <param name="peer">The identifier of the key that signed.</param>
    /// <returns><see langword="true"/> when the proof holds.</returns>
    public static bool TryVerify(string? message, string text, out PeerId peer)
    {
        peer = default;
        if (string.IsNullOrEmpty(message) || message.Length > MAX_MESSAGE)
        {
            return false;
        }

        ProofMessage? proof;
        try
        {
            proof = JsonSerializer.Deserialize<ProofMessage>(message, _json);
        }
        catch (JsonException)
        {
            return false;
        }

        return proof is not null && KeySignature.TryVerify(proof.Key, proof.Signature, Encoding.UTF8.GetBytes(text), out peer);
    }

    /// <summary>
    /// A proof as it travels.
    /// </summary>
    /// <param name="Key">The SubjectPublicKeyInfo of the device key, base64.</param>
    /// <param name="Signature">The signature, base64.</param>
    private sealed record ProofMessage(string? Key, string? Signature);
}

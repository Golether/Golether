using Golether.Core.Identity;

namespace Golether.Transports.WebRtc;

/// <summary>
/// The device key as the signalling needs it: its public part and a way to sign with it (ECDSA P-256 over SHA-256,
/// r‖s), without the private key leaving its owner.
/// </summary>
public sealed class DeviceSigner
{
    /// <summary>
    /// The signing operation of the owner.
    /// </summary>
    private readonly Func<byte[], byte[]> _sign;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceSigner"/> class.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The DER SubjectPublicKeyInfo of the key.</param>
    /// <param name="sign">Signs bytes with the key, giving r‖s.</param>
    public DeviceSigner(byte[] subjectPublicKeyInfo, Func<byte[], byte[]> sign)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        _sign = sign ?? throw new ArgumentNullException(nameof(sign));
        PublicKey = Convert.ToBase64String(subjectPublicKeyInfo);
        PeerId = PeerId.FromSubjectPublicKeyInfo(subjectPublicKeyInfo);
    }

    /// <summary>
    /// Gets the SubjectPublicKeyInfo in base64, as the signalling carries it.
    /// </summary>
    public string PublicKey { get; }

    /// <summary>
    /// Gets the identifier of the key.
    /// </summary>
    public PeerId PeerId { get; }

    /// <summary>
    /// Signs bytes.
    /// </summary>
    /// <param name="payload">The bytes.</param>
    /// <returns>The signature in base64.</returns>
    public string Sign(byte[] payload) => Convert.ToBase64String(_sign(payload));
}

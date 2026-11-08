using System.Security.Cryptography;
using Golether.Core.Identity;

namespace Golether.Transports.WebRtc;

/// <summary>
/// Checks a signature made with a device key as WebCrypto and the applications make it: ECDSA P-256 over SHA-256,
/// the signature as r‖s, the key as its SubjectPublicKeyInfo, both in base64. The key and the signature come from the
/// network, so their sizes and the curve are checked before anything is computed.
/// </summary>
public static class KeySignature
{
    /// <summary>
    /// The longest SubjectPublicKeyInfo accepted, in bytes.
    /// </summary>
    public const int MAX_KEY = 256;

    /// <summary>
    /// The longest signature accepted, in bytes.
    /// </summary>
    public const int MAX_SIGNATURE = 256;

    /// <summary>
    /// The object identifier of the P-256 curve.
    /// </summary>
    private const string P256_OID = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Checks a signature and names the key that made it.
    /// </summary>
    /// <param name="key">The SubjectPublicKeyInfo of the key, base64.</param>
    /// <param name="signature">The signature, base64.</param>
    /// <param name="payload">The signed bytes.</param>
    /// <param name="owner">The identifier of the key when the signature holds.</param>
    /// <returns><see langword="true"/> when the signature is valid.</returns>
    public static bool TryVerify(string? key, string? signature, ReadOnlySpan<byte> payload, out PeerId owner)
    {
        owner = default;
        if (!TryDecode(key, MAX_KEY, out var spki) || !TryDecode(signature, MAX_SIGNATURE, out var sign))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || ecdsa.ExportParameters(false).Curve.Oid.Value != P256_OID ||
                !ecdsa.VerifyData(payload, sign, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }

        owner = PeerId.FromSubjectPublicKeyInfo(spki);
        return true;
    }

    /// <summary>
    /// Decodes base64 of a bounded size.
    /// </summary>
    /// <param name="text">The base64 text.</param>
    /// <param name="max">The most bytes accepted.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns><see langword="true"/> when the text decodes to 1…<paramref name="max"/> bytes.</returns>
    private static bool TryDecode(string? text, int max, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(text) || text.Length > (max + 2) / 3 * 4)
        {
            return false;
        }

        var buffer = new byte[max];
        if (!Convert.TryFromBase64String(text, buffer, out var written) || written == 0)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}

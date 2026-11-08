using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The device key of the browser (<c>keys.js</c>): ECDSA P-256 of WebCrypto that cannot be exported, kept in
/// IndexedDB. .NET sees only its identifier; the page signs with it itself.
/// </summary>
internal static partial class KeyInterop
{
    /// <summary>
    /// Opens the key, making it on the first visit.
    /// </summary>
    /// <returns>The identifier: lowercase hex SHA-256 of the SubjectPublicKeyInfo, as on the desktop.</returns>
    [JSImport("keys.peerId", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> PeerIdAsync();
}

using Golether.Security.Identity;
using Golether.Transports.WebRtc;

namespace Golether.Session.Hosting;

/// <summary>
/// Turns the device key of an application into the signer of the WebRTC connections of sessions.
/// </summary>
public static class DeviceSigners
{
    /// <summary>
    /// Makes the signer of a device key.
    /// </summary>
    /// <param name="identity">The device key.</param>
    /// <returns>The signer; the private key stays in the identity.</returns>
    public static DeviceSigner From(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new DeviceSigner(identity.Certificate.PublicKey.ExportSubjectPublicKeyInfo(), data => identity.Sign(data));
    }
}

using System.Runtime.InteropServices.JavaScript;
using Golether.Core.Enums;
using Golether.Localization;
using Golether.Transports;

namespace Golether.Browser.Transport;

/// <summary>
/// Turns the failures of the WebRTC side of the page into the exceptions the sessions know. The page fails with a
/// code, never with a text: <see cref="FOREIGN_KEY"/>, or a name of <see cref="RendezvousError"/>.
/// </summary>
internal static class RtcErrors
{
    /// <summary>
    /// The code of a side that proved another key than the invitation names.
    /// </summary>
    public const string FOREIGN_KEY = "ForeignKey";

    /// <summary>
    /// Translates a failure of the page.
    /// </summary>
    /// <param name="error">The failure.</param>
    /// <returns><see cref="PeerAuthenticationException"/> for a foreign key, <see cref="IOException"/> otherwise,
    /// with the reason in the language of the page.</returns>
    public static Exception Translate(JSException error)
    {
        var code = error.Message.Trim();
        if (code.EndsWith(FOREIGN_KEY, StringComparison.Ordinal))
        {
            return new PeerAuthenticationException(Texts.Format("Net.Error.KeyCheckFailed", "WebRTC", Texts.Get("Browser.Error.ForeignKey")), error);
        }

        var reason = Enum.GetValues<RendezvousError>().FirstOrDefault(r => code.EndsWith(r.ToString(), StringComparison.Ordinal), RendezvousError.Unreachable);
        return new IOException(Texts.Get("Rendezvous.Error." + reason), error);
    }
}

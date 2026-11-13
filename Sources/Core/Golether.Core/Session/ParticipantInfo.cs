using Golether.Core.Identity;
using Golether.Localization;

namespace Golether.Core.Session;

/// <summary>
/// A participant of a session.
/// </summary>
/// <param name="PeerId">The device identifier.</param>
/// <param name="DisplayName">The name chosen by the participant.</param>
/// <param name="IsHost">Whether the participant hosts the session and shares the media.</param>
public sealed record ParticipantInfo(PeerId PeerId, string DisplayName, bool IsHost)
{
    /// <summary>
    /// The maximum length of a display name.
    /// </summary>
    public const int MAX_DISPLAY_NAME_LENGTH = 64;

    /// <summary>
    /// Normalizes a display name received from the user or the network.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <returns>The trimmed name without control characters, at most <see cref="MAX_DISPLAY_NAME_LENGTH"/> characters;
    /// the localized default name when nothing is left.</returns>
    public static string NormalizeDisplayName(string? name) => NormalizeName(name, Texts.Get("Session.DefaultParticipantName"));

    /// <summary>
    /// Normalizes the name of a session received from the user or the network: a session is not a participant, so an
    /// empty name becomes the name of a session, not of a person.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <returns>The trimmed name without control characters, at most <see cref="MAX_DISPLAY_NAME_LENGTH"/> characters;
    /// the localized default name of a session when nothing is left.</returns>
    public static string NormalizeSessionName(string? name) => NormalizeName(name, Texts.Get("Session.DefaultSessionName"));

    /// <summary>
    /// Drops control characters and surrounding space and bounds the length of a name.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <param name="fallback">The name used when nothing is left.</param>
    /// <returns>The cleaned name, or <paramref name="fallback"/>.</returns>
    private static string NormalizeName(string? name, string fallback)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MAX_DISPLAY_NAME_LENGTH)
        {
            cleaned = cleaned[..MAX_DISPLAY_NAME_LENGTH].TrimEnd();
        }

        return cleaned.Length == 0 ? fallback : cleaned;
    }
}

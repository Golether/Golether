using System.Text;
using System.Text.Json;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Media.Conference.Browser;
using Golether.Session;

namespace Golether.Browser.Session;

/// <summary>
/// Writes the snapshot of a session as the JSON the page draws from. Texts are worded here, in the language of the
/// page, so the page holds no wording of its own.
/// </summary>
internal static class SnapshotJson
{
    /// <summary>
    /// Writes a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="conference">The cameras of the page.</param>
    /// <param name="speaking">The participants speaking now; <see langword="default"/> stands for this device.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(SessionSnapshot snapshot, BrowserConferenceMedia conference, IReadOnlySet<PeerId> speaking)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(conference);
        ArgumentNullException.ThrowIfNull(speaking);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("isHost", snapshot.IsHost);
            json.WriteString("state", snapshot.State.ToString());
            json.WriteString("sessionName", snapshot.SessionName);
            json.WriteString("status", Status(snapshot));
            json.WriteString("code", snapshot.VerificationCode?.ToString());
            WriteMedia(json, snapshot);
            WritePlayback(json, snapshot);
            json.WriteBoolean("microphoneOn", !conference.MicrophoneMuted);
            json.WriteBoolean("cameraOn", !conference.CameraOff);
            WriteParticipants(json, snapshot, conference, speaking);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes the film.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="snapshot">The snapshot.</param>
    private static void WriteMedia(Utf8JsonWriter json, SessionSnapshot snapshot)
    {
        if (snapshot.Media is not { } media)
        {
            json.WriteNull("media");
            return;
        }

        json.WriteStartObject("media");
        json.WriteString("name", media.FileName);
        json.WriteNumber("length", media.Length);
        json.WriteEndObject();
    }

    /// <summary>
    /// Writes the state of the playback.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="snapshot">The snapshot.</param>
    private static void WritePlayback(Utf8JsonWriter json, SessionSnapshot snapshot)
    {
        if (snapshot.Playback is not { } playback)
        {
            json.WriteNull("playback");
            return;
        }

        json.WriteStartObject("playback");
        json.WriteBoolean("playing", playback.State == PlayState.Playing);
        json.WriteNumber("position", playback.Position.TotalSeconds);
        json.WriteString("cause", playback.Cause.ToString());
        if (snapshot.Local?.StartsIn is { } startsIn)
        {
            json.WriteNumber("startsIn", Math.Ceiling(startsIn.TotalSeconds));
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Writes the participants.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="conference">The cameras of the page.</param>
    /// <param name="speaking">The participants speaking now.</param>
    private static void WriteParticipants(Utf8JsonWriter json, SessionSnapshot snapshot, BrowserConferenceMedia conference, IReadOnlySet<PeerId> speaking)
    {
        var linked = conference.GetPeerDiagnostics().Where(d => d.Verified).Select(d => d.Peer).ToHashSet();
        json.WriteStartArray("participants");
        foreach (var participant in snapshot.Participants)
        {
            var peer = participant.Info.PeerId;
            json.WriteStartObject();
            json.WriteString("peer", peer.Value);
            json.WriteString("name", participant.Info.DisplayName);
            json.WriteBoolean("speaking", speaking.Contains(participant.IsLocal ? default : peer));
            json.WriteBoolean("cameraOff", participant.IsLocal ? conference.CameraOff : participant.Status?.CameraOff != false);
            json.WriteBoolean("microphoneOff", participant.IsLocal ? conference.MicrophoneMuted : participant.Status?.MicrophoneOff != false);
            json.WriteBoolean("linked", participant.IsLocal || linked.Contains(peer));
            json.WriteBoolean("isHost", participant.Info.IsHost);
            json.WriteBoolean("isLocal", participant.IsLocal);
            if (participant.Status?.RoundTripMilliseconds is { } ping)
            {
                json.WriteNumber("ping", ping);
            }

            json.WriteBoolean("buffering", participant.Status?.IsBuffering == true);
            json.WriteNumber("fromPeers", participant.Status?.BytesFromPeers ?? 0);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// Words the line under the video.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The line, empty when there is nothing to say.</returns>
    private static string Status(SessionSnapshot snapshot) => snapshot.State switch
    {
        SessionState.Connecting => Texts.Get("Main.Busy.Connecting"),
        SessionState.AwaitingApproval => Texts.Get("Main.Approval.Title"),
        SessionState.Ended => Texts.Get(snapshot.IsHost ? "Browser.Session.Ended" : "Session.Error.HostEnded"),
        _ when snapshot.Media is null => Texts.Get(snapshot.IsHost ? "Main.Placeholder.ChooseFile" : "Main.Placeholder.HostNotChosen"),
        _ when snapshot.Local?.StartsIn is { } startsIn => Texts.Format("Main.StartsIn", Math.Ceiling(startsIn.TotalSeconds)),
        _ => snapshot.Playback?.Cause switch
        {
            PlaybackCause.WaitingForParticipants => Texts.Get("Main.Waiting.Everybody"),
            PlaybackCause.Ended => Texts.Get("Main.Sync.Ended"),
            _ when snapshot.Playback?.State == PlayState.Paused => Texts.Get("Main.Sync.Paused"),
            _ => string.Empty,
        },
    };
}

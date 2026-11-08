using Golether.Core.Identity;

namespace Golether.Web.Server.Signal;

/// <summary>
/// A room held by a host in a browser: its WebSocket and the meetings with participants that are under way.
/// </summary>
internal sealed class SignalRoom
{
    /// <summary>
    /// The WebSockets of the participants by meeting.
    /// </summary>
    private readonly Dictionary<string, SignalSocket> _meetings = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards <see cref="_meetings"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRoom"/> class.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <param name="owner">The key that holds the room.</param>
    /// <param name="host">The WebSocket of the host.</param>
    public SignalRoom(string name, PeerId owner, SignalSocket host)
    {
        Name = name;
        Owner = owner;
        Host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the key that holds the room.
    /// </summary>
    public PeerId Owner { get; }

    /// <summary>
    /// Gets the WebSocket of the host.
    /// </summary>
    public SignalSocket Host { get; }

    /// <summary>
    /// Adds a meeting unless the room already has as many as it may.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    /// <param name="participant">The WebSocket of the participant.</param>
    /// <param name="limit">The number of meetings the room may have.</param>
    /// <returns><see langword="true"/> when the meeting was added.</returns>
    public bool TryAddMeeting(string meeting, SignalSocket participant, int limit)
    {
        lock (_gate)
        {
            if (_meetings.Count >= limit)
            {
                return false;
            }

            _meetings[meeting] = participant;
            return true;
        }
    }

    /// <summary>
    /// Removes a meeting.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    public void RemoveMeeting(string meeting)
    {
        lock (_gate)
        {
            _meetings.Remove(meeting);
        }
    }

    /// <summary>
    /// Finds the participant of a meeting.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    /// <returns>The WebSocket of the participant, or <see langword="null"/> when the meeting is over.</returns>
    public SignalSocket? FindMeeting(string? meeting)
    {
        if (meeting is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _meetings.GetValueOrDefault(meeting);
        }
    }
}

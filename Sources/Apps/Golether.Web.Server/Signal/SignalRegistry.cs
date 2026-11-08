using Golether.Web.Server.Enums;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Signal;

/// <summary>
/// The rooms of hosts in browsers. A room belongs to the key that took it first; the same key may take it again
/// (the host came back), another key may not.
/// </summary>
internal sealed class SignalRegistry
{
    /// <summary>
    /// The number of rooms held at once.
    /// </summary>
    private readonly int _maxRooms;

    /// <summary>
    /// The rooms by name.
    /// </summary>
    private readonly Dictionary<string, SignalRoom> _rooms = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards <see cref="_rooms"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRegistry"/> class.
    /// </summary>
    /// <param name="options">The settings.</param>
    public SignalRegistry(SignalServerOptions options)
    {
        _maxRooms = (options ?? throw new ArgumentNullException(nameof(options))).MaxRooms;
    }

    /// <summary>
    /// Gets the number of rooms held.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _rooms.Count;
            }
        }
    }

    /// <summary>
    /// Takes a room for a host.
    /// </summary>
    /// <param name="room">The room with the key and the WebSocket of the host.</param>
    /// <param name="replaced">The earlier holding of the same key, to be closed; <see langword="null"/> when there
    /// was none.</param>
    /// <returns>Whether the room was taken.</returns>
    public RoomClaim Claim(SignalRoom room, out SignalRoom? replaced)
    {
        ArgumentNullException.ThrowIfNull(room);
        replaced = null;
        lock (_gate)
        {
            if (_rooms.TryGetValue(room.Name, out var current))
            {
                if (current.Owner != room.Owner)
                {
                    return RoomClaim.Taken;
                }

                replaced = current;
            }
            else if (_rooms.Count >= _maxRooms)
            {
                return RoomClaim.Full;
            }

            _rooms[room.Name] = room;
            return RoomClaim.Registered;
        }
    }

    /// <summary>
    /// Releases a room, unless it has been taken again since.
    /// </summary>
    /// <param name="room">The holding to release.</param>
    public void Release(SignalRoom room)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(room.Name, out var current) && ReferenceEquals(current, room))
            {
                _rooms.Remove(room.Name);
            }
        }
    }

    /// <summary>
    /// Finds a room.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <returns>The room, or <see langword="null"/> when nobody holds it.</returns>
    public SignalRoom? Find(string name)
    {
        lock (_gate)
        {
            return _rooms.GetValueOrDefault(name);
        }
    }
}

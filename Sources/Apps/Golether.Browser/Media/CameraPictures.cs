using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Golether.Browser.Interop;
using Golether.Client.ViewModels;
using Golether.Core.Identity;

namespace Golether.Browser.Media;

/// <summary>
/// Copies the pictures of the cameras into the tiles of the participants, as the desktop draws the frames of its
/// cameras. The browser plays each camera in an element of its own; a tile shows a copy of its current picture, so
/// the tile scrolls and is drawn over like any control.
/// </summary>
internal sealed class CameraPictures
{
    /// <summary>
    /// The width of the box a picture is scaled into: twice a tile of the participants column.
    /// </summary>
    private const int MAX_WIDTH = 480;

    /// <summary>
    /// The height of the box a picture is scaled into.
    /// </summary>
    private const int MAX_HEIGHT = 270;

    /// <summary>
    /// The bytes of one picture as RGBA.
    /// </summary>
    private const int BYTES_PER_PIXEL = 4;

    /// <summary>
    /// The buffer the page copies a picture into.
    /// </summary>
    private readonly byte[] _buffer = new byte[MAX_WIDTH * MAX_HEIGHT * BYTES_PER_PIXEL];

    /// <summary>
    /// The two pictures of every participant: a tile shows one while the other is filled, so the tile sees a new
    /// picture and draws it.
    /// </summary>
    private readonly Dictionary<PeerId, (WriteableBitmap? First, WriteableBitmap? Second, bool UseSecond)> _pictures = [];

    /// <summary>
    /// Copies the current pictures into the tiles.
    /// </summary>
    /// <param name="participants">The tiles.</param>
    public void Update(IReadOnlyList<ParticipantItemViewModel> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);

        foreach (var participant in participants)
        {
            participant.CameraImage = participant.CameraOff ? null : Copy(participant);
        }

        var present = participants.Select(p => p.PeerId).ToHashSet();
        foreach (var gone in _pictures.Keys.Where(p => !present.Contains(p)).ToArray())
        {
            _pictures.Remove(gone);
        }

        StageInterop.KeepCameras([.. participants.Where(p => !p.IsLocal).Select(p => p.PeerId.Value)]);
    }

    /// <summary>
    /// Forgets every picture.
    /// </summary>
    public void Clear()
    {
        _pictures.Clear();
        StageInterop.KeepCameras([]);
    }

    /// <summary>
    /// Copies the current picture of one camera.
    /// </summary>
    /// <param name="participant">The tile.</param>
    /// <returns>The picture, or <see langword="null"/> when the camera has none.</returns>
    private WriteableBitmap? Copy(ParticipantItemViewModel participant)
    {
        var size = StageInterop.CameraFrame(participant.IsLocal ? string.Empty : participant.PeerId.Value, MAX_WIDTH, MAX_HEIGHT, _buffer);
        var width = size >> 16;
        var height = size & 0xFFFF;
        if (width <= 0 || height <= 0 || width > MAX_WIDTH || height > MAX_HEIGHT)
        {
            return null;
        }

        _pictures.TryGetValue(participant.PeerId, out var pair);
        var target = pair.UseSecond ? pair.Second : pair.First;
        if (target is null || target.PixelSize.Width != width || target.PixelSize.Height != height)
        {
            target?.Dispose();
            target = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Premul);
        }

        using (var locked = target.Lock())
        {
            var rowBytes = width * BYTES_PER_PIXEL;
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(_buffer, row * rowBytes, locked.Address + (row * locked.RowBytes), rowBytes);
            }
        }

        _pictures[participant.PeerId] = pair.UseSecond ? (pair.First, target, false) : (target, pair.Second, true);
        return target;
    }
}

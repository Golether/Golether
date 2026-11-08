using Golether.Media.Streaming.Files;

namespace Golether.Browser.Media;

/// <summary>
/// Gives the files chosen on the page a path the sessions understand (<see cref="OpenedFiles"/>), as the phone does
/// with the files of its system pickers, and finds the file of such a path again for the player.
/// </summary>
internal static class BrowserFiles
{
    /// <summary>
    /// The numbers of the files on the page by their paths.
    /// </summary>
    private static readonly Dictionary<string, int> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a chosen file.
    /// </summary>
    /// <param name="file">The number of the file on the page.</param>
    /// <param name="name">The name of the file.</param>
    /// <param name="length">The size of the file.</param>
    /// <returns>The path to share.</returns>
    public static string Add(int file, string name, long length)
    {
        var path = OpenedFiles.Add(name, () => new BrowserFileStream(file, length));
        _files[Path.GetFullPath(path)] = file;
        return path;
    }

    /// <summary>
    /// Finds the file of a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="file">The number of the file on the page.</param>
    /// <returns><see langword="true"/> when the path belongs to a chosen file.</returns>
    public static bool TryFind(string path, out int file) => _files.TryGetValue(Path.GetFullPath(path), out file);

    /// <summary>
    /// Forgets every file: the session that showed them is over.
    /// </summary>
    public static void Clear()
    {
        _files.Clear();
        OpenedFiles.Clear();
    }
}

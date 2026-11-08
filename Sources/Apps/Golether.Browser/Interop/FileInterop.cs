using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The files the user chose on the page (<c>files.js</c>). The browser reads them in place: nothing is copied or
/// uploaded anywhere.
/// </summary>
internal static partial class FileInterop
{
    /// <summary>
    /// Reads a part of a file.
    /// </summary>
    /// <param name="file">The number of the file.</param>
    /// <param name="offset">The position of the part.</param>
    /// <param name="count">The length of the part.</param>
    /// <returns>The bytes as a JavaScript <c>Uint8Array</c>; shorter at the end of the file.</returns>
    [JSImport("files.read", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>]
    public static partial Task<JSObject> ReadAsync(int file, double offset, int count);

    /// <summary>
    /// Copies the bytes of a <c>Uint8Array</c> into .NET memory.
    /// </summary>
    /// <param name="bytes">The array.</param>
    /// <param name="target">The memory; at least as long as the array.</param>
    /// <returns>The number of bytes copied.</returns>
    [JSImport("files.copy", JsModule.NAME)]
    public static partial int Copy(JSObject bytes, [JSMarshalAs<JSType.MemoryView>] Span<byte> target);

    /// <summary>
    /// Gives the address the <c>&lt;video&gt;</c> element plays a file from.
    /// </summary>
    /// <param name="file">The number of the file.</param>
    /// <returns>A <c>blob:</c> address valid on this page.</returns>
    [JSImport("files.objectUrl", JsModule.NAME)]
    public static partial string ObjectUrl(int file);
}

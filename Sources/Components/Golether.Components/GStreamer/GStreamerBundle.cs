using Golether.Components.Pe;
using Golether.Localization;

namespace Golether.Components.GStreamer;

/// <summary>
/// The part of a GStreamer installation Golether needs, and how to copy it.
/// </summary>
/// <remarks>
/// Layout of a bundle (same as the GStreamer installation, reduced):
/// <c>bin/*.dll</c>, <c>lib/gstreamer-1.0/&lt;plugins&gt;.dll</c>, <c>libexec/gstreamer-1.0/gst-plugin-scanner.exe</c>.
/// </remarks>
public static class GStreamerBundle
{
    /// <summary>
    /// The oldest GStreamer version with the webrtcbin features Golether relies on.
    /// </summary>
    public static readonly Version MinimumVersion = new(1, 24);

    /// <summary>
    /// The plugins used by the conferencing pipelines.
    /// </summary>
    public static readonly IReadOnlyList<string> Plugins =
    [
        "gstcoreelements", "gstapp", "gstautodetect", "gstplayback", "gsttypefindfunctions",
        "gstvideoconvertscale", "gstvideorate", "gstvideofilter", "gstvideotestsrc",
        "gstaudioconvert", "gstaudioresample", "gstaudiotestsrc", "gstvolume", "gstlevel",
        "gstwebrtc", "gstnice", "gstdtls", "gstsrtp", "gstsctp", "gstrtp", "gstrtpmanager",
        "gstvpx", "gstopus", "gstwebrtcdsp", "gstaudiomixer",
        "gstmediafoundation", "gstwasapi2", "gstdirectsound",
    ];

    /// <summary>
    /// The core libraries Golether calls directly.
    /// </summary>
    public static readonly IReadOnlyList<string> CoreLibraries =
    [
        "gstreamer-1.0-0.dll", "gstbase-1.0-0.dll", "gstapp-1.0-0.dll", "gstvideo-1.0-0.dll", "gstaudio-1.0-0.dll",
        "gstwebrtc-1.0-0.dll", "gstsdp-1.0-0.dll", "gobject-2.0-0.dll", "glib-2.0-0.dll",
    ];

    /// <summary>
    /// The relative path of the plugin directory.
    /// </summary>
    public static readonly string PluginDirectory = Path.Combine("lib", "gstreamer-1.0");

    /// <summary>
    /// The relative path of the plugin scanner.
    /// </summary>
    public static readonly string ScannerPath = Path.Combine("libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");

    /// <summary>
    /// The libraries of the Microsoft Visual C++ runtime that the official (MSVC) build of GStreamer imports. They are
    /// not part of Windows and the GStreamer installer does not carry them, so the bundle does: on a computer without
    /// the Visual C++ Redistributable not even GLib would load otherwise.
    /// </summary>
    public static readonly IReadOnlyList<string> CompilerRuntime = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];

    /// <summary>
    /// The core library that tells which compiler built the bundle: everything else stands on it.
    /// </summary>
    private const string BASE_LIBRARY = "glib-2.0-0.dll";

    /// <summary>
    /// Copies the needed plugins, the core libraries, the plugin scanner and all DLLs they depend on, with the Visual
    /// C++ runtime taken from the system folder of this computer.
    /// </summary>
    /// <param name="installRoot">The GStreamer installation.</param>
    /// <param name="targetRoot">The bundle directory (created).</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="FileNotFoundException">A required plugin or library is missing in the installation, or the
    /// Visual C++ runtime it needs is missing on this computer.</exception>
    public static long Copy(string installRoot, string targetRoot) => Copy(installRoot, targetRoot, Environment.SystemDirectory);

    /// <summary>
    /// Copies the needed plugins, the core libraries, the plugin scanner and all DLLs they depend on. A library of the
    /// Visual C++ runtime that the installation does not have is taken from the system folder.
    /// </summary>
    /// <param name="installRoot">The GStreamer installation.</param>
    /// <param name="targetRoot">The bundle directory (created).</param>
    /// <param name="systemDirectory">The system folder that holds the Visual C++ runtime.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="FileNotFoundException">A required plugin or library is missing in the installation, or the
    /// Visual C++ runtime it needs is missing in the system folder.</exception>
    public static long Copy(string installRoot, string targetRoot, string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);
        var binSource = Path.Combine(installRoot, "bin");
        var available = Directory.EnumerateFiles(binSource, "*.dll")
            .ToDictionary(p => Path.GetFileName(p), p => p, StringComparer.OrdinalIgnoreCase);

        var roots = new List<(string Source, string Target)>();
        foreach (var plugin in Plugins)
        {
            var source = Path.Combine(installRoot, PluginDirectory, plugin + ".dll");
            roots.Add((Require(source), Path.Combine(targetRoot, PluginDirectory, plugin + ".dll")));
        }

        roots.Add((Require(Path.Combine(installRoot, ScannerPath)), Path.Combine(targetRoot, ScannerPath)));
        foreach (var library in CoreLibraries)
        {
            roots.Add((Require(Path.Combine(binSource, library)), Path.Combine(targetRoot, "bin", library)));
        }

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        var needsCompilerRuntime = false;
        long bytes = 0;
        foreach (var (source, target) in roots)
        {
            bytes += CopyFile(source, target);
            pending.Enqueue(source);
        }

        while (pending.Count > 0)
        {
            foreach (var dependency in PeImports.Read(pending.Dequeue()))
            {
                if (available.TryGetValue(dependency, out var path) && copied.Add(dependency))
                {
                    bytes += CopyFile(path, Path.Combine(targetRoot, "bin", Path.GetFileName(path)));
                    pending.Enqueue(path);
                }
                else if (!available.ContainsKey(dependency) && IsCompilerRuntime(dependency))
                {
                    needsCompilerRuntime = true;
                }

                // Everything else comes from Windows (kernel32, the universal C runtime, …).
            }
        }

        // A build of Visual C++ gets its whole runtime, the way Microsoft deploys it next to a program. The runtime
        // imports nothing but Windows itself.
        if (needsCompilerRuntime)
        {
            foreach (var name in CompilerRuntime)
            {
                var runtime = RequireCompilerRuntime(name, systemDirectory);
                bytes += CopyFile(runtime, Path.Combine(targetRoot, "bin", name));
            }
        }

        return bytes;
    }

    /// <summary>
    /// Checks that a bundle has the files Golether needs.
    /// </summary>
    /// <param name="root">The bundle or installation.</param>
    /// <returns><see langword="true"/> when the core library and all plugins are present.</returns>
    public static bool IsComplete(string root)
        => File.Exists(Path.Combine(root, "bin", "gstreamer-1.0-0.dll")) &&
           Plugins.All(p => File.Exists(Path.Combine(root, PluginDirectory, p + ".dll")));

    /// <summary>
    /// Finds a library of the Visual C++ runtime a bundle built with Visual C++ needs and that neither the bundle nor
    /// the system folder has. A bundle without it is there, but cannot run on this computer.
    /// </summary>
    /// <param name="root">The bundle or installation.</param>
    /// <param name="systemDirectory">The system folder of this computer.</param>
    /// <returns>The name of the missing library, or <see langword="null"/> when nothing is missing.</returns>
    public static string? FindMissingCompilerRuntime(string root, string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);

        if (!NeedsCompilerRuntime(root))
        {
            return null;
        }

        return CompilerRuntime.FirstOrDefault(name =>
            !File.Exists(Path.Combine(root, "bin", name)) &&
            !File.Exists(Path.Combine(systemDirectory, name)));
    }

    /// <summary>
    /// Checks that a bundle runs on any Windows by itself: a build of Visual C++ carries the whole runtime.
    /// </summary>
    /// <param name="root">The bundle or installation.</param>
    /// <returns><see langword="true"/> when the bundle needs nothing of Visual C++ from the computer.</returns>
    public static bool CarriesCompilerRuntime(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        return !NeedsCompilerRuntime(root) || CompilerRuntime.All(name => File.Exists(Path.Combine(root, "bin", name)));
    }

    /// <summary>
    /// Checks whether a bundle was built with Visual C++: GLib, the base of everything, imports its runtime. A build of
    /// another compiler needs no Visual C++ at all.
    /// </summary>
    /// <param name="root">The bundle or installation.</param>
    /// <returns><see langword="true"/> when the bundle needs the Visual C++ runtime.</returns>
    private static bool NeedsCompilerRuntime(string root)
    {
        var baseLibrary = Path.Combine(root, "bin", BASE_LIBRARY);
        return File.Exists(baseLibrary) && ReadImports(baseLibrary).Any(IsCompilerRuntime);
    }

    /// <summary>
    /// Checks whether a library belongs to the Visual C++ runtime.
    /// </summary>
    /// <param name="name">The file name of the library.</param>
    /// <returns><see langword="true"/> for one of <see cref="CompilerRuntime"/>.</returns>
    private static bool IsCompilerRuntime(string name) => CompilerRuntime.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds a library of the Visual C++ runtime in the system folder.
    /// </summary>
    /// <param name="name">The file name of the library.</param>
    /// <param name="systemDirectory">The system folder.</param>
    /// <returns>The path of the library.</returns>
    /// <exception cref="FileNotFoundException">The library is not there.</exception>
    private static string RequireCompilerRuntime(string name, string systemDirectory)
    {
        var path = Path.Combine(systemDirectory, name);
        return File.Exists(path) ? path : throw new FileNotFoundException(Texts.Format("Install.Error.CompilerRuntimeMissing", name), path);
    }

    /// <summary>
    /// Reads the libraries a file imports; a file that cannot be read imports nothing that could be checked.
    /// </summary>
    /// <param name="path">The library.</param>
    /// <returns>The imported names.</returns>
    private static IReadOnlyList<string> ReadImports(string path)
    {
        try
        {
            return PeImports.Read(path);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Throws when a file is missing.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The same path.</returns>
    /// <exception cref="FileNotFoundException">The file is missing.</exception>
    private static string Require(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException($"The GStreamer installation has no '{Path.GetFileName(path)}'.", path);

    /// <summary>
    /// Copies a file, creating the directory.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    /// <returns>The file size.</returns>
    private static long CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return new FileInfo(target).Length;
    }
}

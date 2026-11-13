using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;
using Golether.Components.Installation;
using Golether.Components.Pe;
using Golether.Core.Data.Enums;

namespace Golether.Components.Tests;

/// <summary>
/// Tests of the component catalog, installation hints, PE imports, the installer and the locator.
/// </summary>
public sealed class ComponentTests : IDisposable
{
    /// <summary>
    /// The temporary working directory.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-components-");

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// Every catalog entry is pinned: HTTPS, SHA-256 and size.
    /// </summary>
    [Fact]
    public void Catalog_EntriesArePinned()
    {
        foreach (var package in ComponentCatalog.Packages)
        {
            Assert.Equal(Uri.UriSchemeHttps, package.Url.Scheme);
            Assert.Matches("^[0-9a-f]{64}$", package.Sha256);
            Assert.True(package.Size > 1_000_000);
        }

        Assert.NotNull(ComponentCatalog.Find(ComponentId.Video, "win-x64"));
        Assert.NotNull(ComponentCatalog.Find(ComponentId.Conference, "WIN-ARM64"));
        Assert.Null(ComponentCatalog.Find(ComponentId.Video, "linux-x64"));
        Assert.Matches("^(win|linux|osx)-", ComponentCatalog.CurrentRuntimeIdentifier);
    }

    /// <summary>
    /// Linux distributions are recognised through ID and ID_LIKE.
    /// </summary>
    /// <param name="osRelease">The os-release content.</param>
    /// <param name="expected">The expected family.</param>
    [Theory]
    [InlineData("NAME=\"Ubuntu\"\nID=ubuntu\nID_LIKE=debian", "debian")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", "debian")]
    [InlineData("ID=\"fedora\"", "fedora")]
    [InlineData("ID=rocky\nID_LIKE=\"rhel centos fedora\"", "fedora")]
    [InlineData("ID=manjaro\nID_LIKE=arch", "arch")]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"", "suse")]
    [InlineData("ID=nixos", null)]
    [InlineData("", null)]
    public void Advice_DetectsDistribution(string osRelease, string? expected)
        => Assert.Equal(expected, InstallAdvice.DetectDistribution(osRelease));

    /// <summary>
    /// The hints contain the right package manager commands.
    /// </summary>
    [Fact]
    public void Advice_GivesCommands()
    {
        Assert.Equal("sudo apt install libmpv2", InstallAdvice.For(ComponentId.Video, OsFamily.Linux, "ID=debian").Command);
        Assert.Contains("gstreamer1.0-nice", InstallAdvice.For(ComponentId.Conference, OsFamily.Linux, "ID=ubuntu").Command, StringComparison.Ordinal);
        Assert.Equal("brew install gstreamer", InstallAdvice.For(ComponentId.Conference, OsFamily.MacOS, null).Command);
        Assert.Null(InstallAdvice.For(ComponentId.Video, OsFamily.Linux, "ID=nixos").Command);
    }

    /// <summary>
    /// The imports of a real Windows executable include the Windows kernel.
    /// </summary>
    [Fact]
    public void PeImports_ReadsRealExecutable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs a Windows executable.");

        var imports = PeImports.Read(Environment.ProcessPath!);

        Assert.Contains(imports, name => name.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<BadImageFormatException>(() => PeImports.Read(new MemoryStream(new byte[512])));
    }

    /// <summary>
    /// A verified download is unpacked, marked and found by the locator; a second installation uses the cache.
    /// </summary>
    [Fact]
    public async Task Installer_InstallsVerifiedPackage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var content = RandomNumberGenerator.GetBytes(4096);
        var handler = new StaticHandler(content);
        var package = Package(content, PackageFormat.SevenZip);
        var tools = new FakeTools();
        var installer = CreateInstaller(handler, tools, new FakeGStreamerFinder());

        var target = await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);
        await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(target, "libmpv-2.dll")));
        Assert.True(File.Exists(Path.Combine(target, ComponentMarker.FILE_NAME)));
        Assert.Equal(1, handler.Requests);
        Assert.Contains(tools.Calls, call => call.EndsWith("tar.exe", StringComparison.OrdinalIgnoreCase));

        var locator = new ComponentLocator(Path.Combine(_directory.FullName, "no-bundle"), installer.Root, _ => false);
        var status = locator.GetStatus(ComponentId.Video);
        Assert.Equal(ComponentSource.Installed, status.Source);
        Assert.Equal(Path.Combine(target, "libmpv-2.dll"), status.Path);
    }

    /// <summary>
    /// A download with a wrong checksum or size is rejected and nothing is installed.
    /// </summary>
    [Fact]
    public async Task Installer_RejectsTamperedDownloads()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var content = RandomNumberGenerator.GetBytes(4096);
        var installer = CreateInstaller(new StaticHandler(content), new FakeTools(), new FakeGStreamerFinder());

        var wrongHash = Package(content, PackageFormat.SevenZip) with { Sha256 = new string('0', 64) };
        var tooSmall = Package(content, PackageFormat.SevenZip) with { Size = 100 };

        var hashError = await Assert.ThrowsAsync<ComponentInstallException>(() => installer.InstallAsync(wrongHash, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ComponentInstallException>(() => installer.InstallAsync(tooSmall, null, TestContext.Current.CancellationToken));

        Assert.Contains("контрольная сумма", hashError.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(installer.GetInstallDirectory(wrongHash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(installer.Root, "downloads")));
    }

    /// <summary>
    /// A complete GStreamer on the computer is reused without downloading; the bundle contains the dependency closure.
    /// </summary>
    [Fact]
    public async Task Installer_ReusesExistingGStreamer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows executables.");
        var existing = CreateFakeGStreamer();
        var handler = new StaticHandler([]);
        var installer = CreateInstaller(handler, new FakeTools(), new FakeGStreamerFinder(new ExistingGStreamer(existing, new Version(1, 26), true)));

        var target = await installer.InstallAsync(Package([1], PackageFormat.InnoSetup), null, TestContext.Current.CancellationToken);

        var bundle = Path.Combine(target, "gstreamer");
        Assert.Equal(0, handler.Requests);
        Assert.True(GStreamerBundle.IsComplete(bundle));
        Assert.True(File.Exists(Path.Combine(bundle, "bin", "KERNEL32.dll")), "Imported libraries that ship with GStreamer are copied.");
        Assert.False(File.Exists(Path.Combine(bundle, "bin", "unused.dll")), "Libraries nobody imports are skipped.");
        Assert.Equal(ComponentSource.Installed, new ComponentLocator(_directory.FullName, installer.Root, _ => false).GetStatus(ComponentId.Conference).Source);
    }

    /// <summary>
    /// An incomplete registered GStreamer is not taken over by the silent installer.
    /// </summary>
    [Fact]
    public async Task Installer_RefusesToOverwriteRegisteredGStreamer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var handler = new StaticHandler([]);
        var finder = new FakeGStreamerFinder(new ExistingGStreamer(_directory.FullName, new Version(1, 20), false)) { Registered = true };
        var installer = CreateInstaller(handler, new FakeTools(), finder);

        var error = await Assert.ThrowsAsync<ComponentInstallException>(() =>
            installer.InstallAsync(Package([1], PackageFormat.InnoSetup), null, TestContext.Current.CancellationToken));

        Assert.Contains("уже установлен GStreamer", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    /// <summary>
    /// A download cut off on the way is continued from the bytes already received, after a pause, and the joined file
    /// is the verified package.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Download_ContinuesWhereTheConnectionBroke()
    {
        var content = RandomNumberGenerator.GetBytes(200_000);
        var handler = new BreakingHandler(content);
        var time = new FakeTimeProvider();
        var installer = new ComponentInstaller(Path.Combine(_directory.FullName, "components"), new HttpClient(handler), new FakeTools(), time, existingGStreamer: new FakeGStreamerFinder());

        var file = await RunWithTimeAsync(installer.DownloadAsync(Package(content, PackageFormat.SevenZip), null, TestContext.Current.CancellationToken), time);

        Assert.Equal(content, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Ranges.Count);
        Assert.Null(handler.Ranges[0]);
        Assert.Equal(BreakingHandler.SENT_BEFORE_BREAK, handler.Ranges[1]?.Ranges.Single().From);
    }

    /// <summary>
    /// A file the server does not have is not asked for again: only a broken connection or a busy server is.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Download_DoesNotRetryAMissingFile()
    {
        var content = RandomNumberGenerator.GetBytes(1000);
        var handler = new StatusHandler(HttpStatusCode.NotFound);
        var installer = CreateInstaller(handler, new FakeTools(), new FakeGStreamerFinder());

        await Assert.ThrowsAsync<ComponentInstallException>(() => installer.DownloadAsync(Package(content, PackageFormat.SevenZip), null, TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.Requests);
    }

    /// <summary>
    /// Bundled components win over installed ones; a damaged marker is ignored.
    /// </summary>
    [Fact]
    public void Locator_PrefersBundledAndIgnoresDamagedMarkers()
    {
        var bundled = Directory.CreateDirectory(Path.Combine(_directory.FullName, "native")).FullName;
        var installed = Directory.CreateDirectory(Path.Combine(_directory.FullName, "components", "video", "1")).FullName;
        File.WriteAllText(Path.Combine(installed, ComponentLocator.VideoLibraryNames[0]), "x");
        File.WriteAllText(Path.Combine(installed, ComponentMarker.FILE_NAME), "{ broken");
        var locator = new ComponentLocator(bundled, Path.Combine(_directory.FullName, "components"), _ => false, () => "ID=debian");

        var missing = locator.GetStatus(ComponentId.Video);
        File.WriteAllText(Path.Combine(bundled, ComponentLocator.VideoLibraryNames[0]), "x");
        var found = locator.GetStatus(ComponentId.Video);

        Assert.False(missing.IsAvailable);
        Assert.Equal(ComponentSource.Bundled, found.Source);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(missing.CanInstall);
        }
        else
        {
            Assert.NotNull(missing.Advice);
        }
    }

    /// <summary>
    /// Creates a package for the given content.
    /// </summary>
    /// <param name="content">The download content.</param>
    /// <param name="format">The format.</param>
    /// <returns>The package.</returns>
    private static ComponentPackage Package(byte[] content, PackageFormat format)
        => new(format == PackageFormat.SevenZip ? ComponentId.Video : ComponentId.Conference, "test", "win-x64",
            new Uri("https://example.org/package.bin"), Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, format);

    /// <summary>
    /// Creates an installer over the fakes.
    /// </summary>
    /// <param name="handler">The HTTP handler.</param>
    /// <param name="tools">The tool runner.</param>
    /// <param name="finder">The GStreamer finder.</param>
    /// <returns>The installer.</returns>
    private ComponentInstaller CreateInstaller(HttpMessageHandler handler, IToolRunner tools, IExistingGStreamerFinder finder)
        => new(Path.Combine(_directory.FullName, "components"), new HttpClient(handler), tools, new FakeTimeProvider(), existingGStreamer: finder);

    /// <summary>
    /// Waits for a task that pauses on a fake clock, moving the clock on until the task is done.
    /// </summary>
    /// <typeparam name="T">The result of the task.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="time">The fake clock the task waits on.</param>
    /// <returns>The result of the task.</returns>
    private static async Task<T> RunWithTimeAsync<T>(Task<T> task, FakeTimeProvider time)
    {
        while (!task.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return await task;
    }

    /// <summary>
    /// Builds a fake GStreamer installation out of copies of a real executable.
    /// </summary>
    /// <returns>The installation root.</returns>
    private string CreateFakeGStreamer()
    {
        var root = Path.Combine(_directory.FullName, "gstreamer-existing");
        var executable = Environment.ProcessPath!;
        void Put(string relative) => Copy(executable, Path.Combine(root, relative));
        foreach (var plugin in GStreamerBundle.Plugins)
        {
            Put(Path.Combine(GStreamerBundle.PluginDirectory, plugin + ".dll"));
        }

        foreach (var library in GStreamerBundle.CoreLibraries)
        {
            Put(Path.Combine("bin", library));
        }

        Put(GStreamerBundle.ScannerPath);
        Put(Path.Combine("bin", "KERNEL32.dll"));
        Put(Path.Combine("bin", "unused.dll"));
        return root;
    }

    /// <summary>
    /// A GStreamer built with Visual C++ goes into the bundle with the whole Visual C++ runtime, taken from the system
    /// folder: the GStreamer installer does not carry it, and a computer without it could not load even GLib.
    /// </summary>
    [Fact]
    public void Bundle_CarriesTheVisualCppRuntime()
    {
        var install = CreateMsvcGStreamer();
        var system = CreateSystemFolder(GStreamerBundle.CompilerRuntime);
        var bundle = Path.Combine(_directory.FullName, "bundle");

        GStreamerBundle.Copy(install, bundle, system);

        Assert.All(GStreamerBundle.CompilerRuntime, name => Assert.True(File.Exists(Path.Combine(bundle, "bin", name)), name));
        Assert.True(File.Exists(Path.Combine(bundle, "bin", "intl-8.dll")), "The libraries of GStreamer itself are copied as before.");
        Assert.False(File.Exists(Path.Combine(bundle, "bin", "KERNEL32.dll")), "Windows itself is not copied.");
        Assert.Null(GStreamerBundle.FindMissingCompilerRuntime(bundle, CreateSystemFolder([])));
        Assert.True(GStreamerBundle.CarriesCompilerRuntime(bundle));
        Assert.False(GStreamerBundle.CarriesCompilerRuntime(install), "The installation itself lacks the runtime.");

        // Without the runtime on the computer that builds the bundle, the bundle is not made, and the reason is told.
        var error = Assert.Throws<FileNotFoundException>(() =>
            GStreamerBundle.Copy(install, Path.Combine(_directory.FullName, "bundle-2"), CreateSystemFolder(["vcruntime140.dll"])));
        Assert.Contains("vcruntime140_1.dll", error.Message, StringComparison.Ordinal);
        Assert.Contains("Visual C++", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bundle of Visual C++ without its runtime, on a computer without it either, is not called available: cameras
    /// and voice would fail to start. A runtime of Windows serves as well, and a build of another compiler needs none.
    /// </summary>
    [Fact]
    public void Locator_DoesNotOfferABundleThatCannotRun()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Visual C++ runtime matters on Windows only.");
        var native = Path.Combine(_directory.FullName, "native");
        var bundle = Path.Combine(native, "gstreamer");
        Directory.CreateDirectory(native);
        Directory.Move(CreateMsvcGStreamer(), bundle);
        var empty = CreateSystemFolder([]);
        var components = Path.Combine(_directory.FullName, "components");

        Assert.Equal("vcruntime140.dll", GStreamerBundle.FindMissingCompilerRuntime(bundle, empty));
        Assert.Equal(ComponentSource.Missing, new ComponentLocator(native, components, _ => false, systemDirectory: empty).GetStatus(ComponentId.Conference).Source);

        var system = CreateSystemFolder(GStreamerBundle.CompilerRuntime);
        Assert.Equal(ComponentSource.Bundled, new ComponentLocator(native, components, _ => false, systemDirectory: system).GetStatus(ComponentId.Conference).Source);

        foreach (var name in GStreamerBundle.CompilerRuntime)
        {
            File.WriteAllBytes(Path.Combine(bundle, "bin", name), FakeLibrary());
        }

        Assert.Equal(ComponentSource.Bundled, new ComponentLocator(native, components, _ => false, systemDirectory: empty).GetStatus(ComponentId.Conference).Source);

        // A build of another compiler needs no Visual C++ at all.
        foreach (var name in GStreamerBundle.CompilerRuntime)
        {
            File.Delete(Path.Combine(bundle, "bin", name));
        }

        File.WriteAllBytes(Path.Combine(bundle, "bin", "glib-2.0-0.dll"), FakeLibrary("KERNEL32.dll"));
        Assert.Null(GStreamerBundle.FindMissingCompilerRuntime(bundle, empty));
    }

    /// <summary>
    /// Builds a fake GStreamer installation of Visual C++: GLib imports the runtime, a plugin the C++ library, and
    /// everything imports Windows.
    /// </summary>
    /// <returns>The installation root.</returns>
    private string CreateMsvcGStreamer()
    {
        var root = Path.Combine(_directory.FullName, "gstreamer-msvc-" + Guid.NewGuid().ToString("N"));
        void Put(string relative, params string[] imports)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, FakeLibrary(imports));
        }

        foreach (var plugin in GStreamerBundle.Plugins)
        {
            Put(Path.Combine(GStreamerBundle.PluginDirectory, plugin + ".dll"), "KERNEL32.dll", plugin == "gstwebrtcdsp" ? "MSVCP140.dll" : "glib-2.0-0.dll");
        }

        foreach (var library in GStreamerBundle.CoreLibraries)
        {
            Put(Path.Combine("bin", library), library == "glib-2.0-0.dll" ? ["VCRUNTIME140.dll", "intl-8.dll", "KERNEL32.dll"] : ["glib-2.0-0.dll"]);
        }

        Put(Path.Combine("bin", "intl-8.dll"), "VCRUNTIME140.dll");
        Put(GStreamerBundle.ScannerPath, "KERNEL32.dll");
        return root;
    }

    /// <summary>
    /// Makes a stand-in for the system folder of Windows with the given files.
    /// </summary>
    /// <param name="names">The files.</param>
    /// <returns>The folder.</returns>
    private string CreateSystemFolder(IEnumerable<string> names)
    {
        var folder = Path.Combine(_directory.FullName, "system-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var name in names)
        {
            File.WriteAllBytes(Path.Combine(folder, name), FakeLibrary());
        }

        return folder;
    }

    /// <summary>
    /// Makes the smallest 64-bit Windows library that imports the given libraries: the headers, one section with the
    /// import descriptors and the names. Nothing runs it; it is read the way <see cref="PeImports"/> reads a real one.
    /// </summary>
    /// <param name="imports">The names of the imported libraries.</param>
    /// <returns>The bytes of the file.</returns>
    private static byte[] FakeLibrary(params string[] imports)
    {
        const int FILE_ALIGNMENT = 0x200;
        const int SECTION_RVA = 0x1000;
        const int OPTIONAL_HEADER_SIZE = 240;
        const int DESCRIPTOR_SIZE = 20;

        var image = new byte[FILE_ALIGNMENT * 2];
        using var writer = new BinaryWriter(new MemoryStream(image));

        // The DOS header points at the PE header.
        writer.Write((ushort)0x5A4D);
        writer.Seek(0x3C, SeekOrigin.Begin);
        writer.Write(0x40);

        // The PE signature and the file header: x64, one section, a DLL.
        writer.Seek(0x40, SeekOrigin.Begin);
        writer.Write(0x00004550);
        writer.Write((ushort)0x8664);
        writer.Write((ushort)1);
        writer.Write(0L);
        writer.Write(0);
        writer.Write((ushort)OPTIONAL_HEADER_SIZE);
        writer.Write((ushort)0x2022);

        // The optional header of PE32+, with the import directory as the only data directory.
        var optional = 0x40 + 4 + 20;
        writer.Write((ushort)0x20B);
        writer.Seek(optional + 24, SeekOrigin.Begin);
        writer.Write(0x180000000L);
        writer.Write(SECTION_RVA);
        writer.Write(FILE_ALIGNMENT);
        writer.Seek(optional + 56, SeekOrigin.Begin);
        writer.Write(SECTION_RVA * 2);
        writer.Write(FILE_ALIGNMENT);
        writer.Seek(optional + 108, SeekOrigin.Begin);
        writer.Write(16);
        writer.Seek(optional + 112 + 8, SeekOrigin.Begin);
        writer.Write(imports.Length == 0 ? 0 : SECTION_RVA);
        writer.Write((imports.Length + 1) * DESCRIPTOR_SIZE);

        // The one section, which holds the imports.
        writer.Seek(optional + OPTIONAL_HEADER_SIZE, SeekOrigin.Begin);
        writer.Write(".idata\0\0"u8);
        writer.Write(FILE_ALIGNMENT);
        writer.Write(SECTION_RVA);
        writer.Write(FILE_ALIGNMENT);
        writer.Write(FILE_ALIGNMENT);
        writer.Write(0L);
        writer.Write(0);
        writer.Write(0xC0000040);

        // The descriptors, ended by an empty one, and the names after them.
        var name = (imports.Length + 1) * DESCRIPTOR_SIZE;
        for (var i = 0; i < imports.Length; i++)
        {
            writer.Seek(FILE_ALIGNMENT + i * DESCRIPTOR_SIZE + 12, SeekOrigin.Begin);
            writer.Write(SECTION_RVA + name);
            writer.Seek(FILE_ALIGNMENT + name, SeekOrigin.Begin);
            writer.Write(System.Text.Encoding.ASCII.GetBytes(imports[i] + "\0"));
            name += imports[i].Length + 1;
        }

        return image;
    }

    /// <summary>
    /// Copies a file, creating the directory.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>
    /// Serves fixed content and counts requests.
    /// </summary>
    /// <param name="content">The content.</param>
    private sealed class StaticHandler(byte[] content) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the number of requests.
        /// </summary>
        public int Requests { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    /// <summary>
    /// Answers every request with one status and counts the requests.
    /// </summary>
    /// <param name="status">The status.</param>
    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the number of requests.
        /// </summary>
        public int Requests { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    /// <summary>
    /// Breaks the first answer off on the way, as a server far away does, and answers a range with the rest.
    /// </summary>
    /// <param name="content">The whole file.</param>
    private sealed class BreakingHandler(byte[] content) : HttpMessageHandler
    {
        /// <summary>
        /// How many bytes the first answer carries before the connection breaks.
        /// </summary>
        public const int SENT_BEFORE_BREAK = 100_000;

        /// <summary>
        /// Gets the ranges asked for, one per request; <see langword="null"/> for the whole file.
        /// </summary>
        public List<RangeHeaderValue?> Ranges { get; } = [];

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ranges.Add(request.Headers.Range);
            if (request.Headers.Range?.Ranges.Single().From is not { } from)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(content[..SENT_BEFORE_BREAK])) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content[(int)from..]) });
        }
    }

    /// <summary>
    /// Gives its bytes, then fails as a connection that broke.
    /// </summary>
    /// <param name="head">The bytes that arrive before the break.</param>
    private sealed class BreakingStream(byte[] head) : MemoryStream(head)
    {
        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
            => Check(base.Read(buffer, offset, count));

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Check(await base.ReadAsync(buffer, cancellationToken));

        /// <summary>
        /// Turns the end of the bytes into the break of the connection.
        /// </summary>
        /// <param name="read">The bytes read.</param>
        /// <returns>The same number while there are bytes.</returns>
        /// <exception cref="IOException">The bytes are over.</exception>
        private static int Check(int read)
            => read > 0 ? read : throw new IOException("The response ended prematurely.");
    }

    /// <summary>
    /// Pretends to extract archives by creating the expected file.
    /// </summary>
    private sealed class FakeTools : IToolRunner
    {
        /// <summary>
        /// Gets the programs that were run.
        /// </summary>
        public List<string> Calls { get; } = [];

        /// <inheritdoc />
        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(fileName);
            var output = arguments[arguments.ToList().IndexOf("-C") + 1];
            File.WriteAllText(Path.Combine(output, arguments[^1]), "library");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Returns preset GStreamer installations.
    /// </summary>
    /// <param name="installations">The installations.</param>
    private sealed class FakeGStreamerFinder(params ExistingGStreamer[] installations) : IExistingGStreamerFinder
    {
        /// <summary>
        /// Gets or sets a value indicating whether an installer registration exists.
        /// </summary>
        public bool Registered { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ExistingGStreamer> Find(string runtimeIdentifier) => installations;

        /// <inheritdoc />
        public bool HasInstallerRegistration() => Registered;
    }
}

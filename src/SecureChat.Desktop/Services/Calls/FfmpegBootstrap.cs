using SIPSorceryMedia.FFmpeg;

namespace SecureChat.Desktop.Services.Calls;

internal static class FfmpegBootstrap
{
    public const string Vp8EncoderName = "libvpx";
    public const string Vp8DecoderName = "libvpx";

    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            var probeDirectories = GetProbeDirectories().ToArray();
            var ffmpegDirectory = probeDirectories.FirstOrDefault(ContainsFfmpegLibraries);
            if (ffmpegDirectory is not null)
            {
                PrependDirectoryToProcessPath(ffmpegDirectory);
            }

            try
            {
                FFmpegInit.Initialise(null, ffmpegDirectory ?? string.Empty, null!);
                _initialized = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new InvalidOperationException(
                    $"FFmpeg 媒体运行库未就绪。请将 FFmpeg Shared 8.1 的 DLL 放到应用目录旁边，或加入 PATH 后重试。已探测目录: {string.Join("; ", probeDirectories)}",
                    ex);
            }
        }
    }

    private static IEnumerable<string> GetProbeDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
        {
            foreach (var candidate in GetCandidateDirectories(current.FullName))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> GetCandidateDirectories(string root)
    {
        yield return root;
        yield return Path.Combine(root, "ffmpeg");
        yield return Path.Combine(root, "ffmpeg", "bin");
        yield return Path.Combine(root, "FFmpeg");
        yield return Path.Combine(root, "FFmpeg", "bin");
        yield return Path.Combine(root, "runtimes", GetRuntimeIdentifier(), "native");
    }

    private static string GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        }

        if (OperatingSystem.IsMacOS())
        {
            return Environment.Is64BitProcess ? "osx-x64" : "osx-x86";
        }

        return Environment.Is64BitProcess ? "linux-x64" : "linux-x86";
    }

    private static bool ContainsFfmpegLibraries(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        return HasLibrary(directory, "avcodec")
            && HasLibrary(directory, "avformat")
            && HasLibrary(directory, "avutil");
    }

    private static bool HasLibrary(string directory, string libraryBaseName)
    {
        var patterns = OperatingSystem.IsWindows()
            ? new[] { $"{libraryBaseName}*.dll" }
            : OperatingSystem.IsMacOS()
                ? new[] { $"lib{libraryBaseName}*.dylib" }
                : new[] { $"lib{libraryBaseName}*.so", $"lib{libraryBaseName}*.so.*" };

        return patterns.Any(pattern => Directory.EnumerateFiles(directory, pattern).Any());
    }

    private static void PrependDirectoryToProcessPath(string directory)
    {
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = currentPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (entries.Any(entry => string.Equals(entry, directory, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var updatedPath = string.IsNullOrWhiteSpace(currentPath)
            ? directory
            : string.Join(Path.PathSeparator, new[] { directory }.Concat(entries));

        Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
    }
}
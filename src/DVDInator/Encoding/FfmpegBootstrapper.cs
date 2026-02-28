using Spectre.Console;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace DVDInator.Encoding;

/// <summary>
/// Ensures FFmpeg binaries are available, downloading them automatically if needed.
/// Uses Xabe.FFmpeg.Downloader to fetch binaries to a local directory.
/// </summary>
public static class FfmpegBootstrapper
{
    private static readonly string FfmpegDirectory =
        Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>
    /// Ensures FFmpeg is available. Downloads it if not found.
    /// Configures both Xabe.FFmpeg and FFMpegCore with the correct path.
    /// Returns the directory containing the FFmpeg binaries.
    /// </summary>
    public static async Task<string> EnsureFfmpegAsync(CancellationToken ct = default)
    {
        // Check if FFmpeg is already in our local directory
        var ffmpegExe = Path.Combine(FfmpegDirectory, "ffmpeg.exe");
        var ffprobeExe = Path.Combine(FfmpegDirectory, "ffprobe.exe");

        if (File.Exists(ffmpegExe) && File.Exists(ffprobeExe))
        {
            ConfigurePaths(FfmpegDirectory);
            return FfmpegDirectory;
        }

        // Check if FFmpeg is on PATH
        var pathFfmpeg = FindOnPath("ffmpeg");
        var pathFfprobe = FindOnPath("ffprobe");

        if (pathFfmpeg is not null && pathFfprobe is not null)
        {
            var dir = Path.GetDirectoryName(pathFfmpeg)!;
            ConfigurePaths(dir);
            return dir;
        }

        // Download FFmpeg
        AnsiConsole.MarkupLine("[yellow]FFmpeg not found. Downloading FFmpeg binaries...[/]");
        AnsiConsole.MarkupLine($"[grey]Downloading to: {FfmpegDirectory}[/]");

        Directory.CreateDirectory(FfmpegDirectory);

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Downloading FFmpeg[/]");
                task.IsIndeterminate = true;

                await FFmpegDownloader.GetLatestVersion(
                    FFmpegVersion.Official,
                    FfmpegDirectory,
                    new Progress<ProgressInfo>(p =>
                    {
                        if (p.TotalBytes > 0)
                        {
                            task.IsIndeterminate = false;
                            task.MaxValue = p.TotalBytes;
                            task.Value = p.DownloadedBytes;
                        }
                    }));

                task.Value = task.MaxValue;
                task.StopTask();
            });

        if (!File.Exists(ffmpegExe))
            throw new FileNotFoundException(
                "FFmpeg download completed but ffmpeg.exe was not found. " +
                "Please install FFmpeg manually and add it to your PATH.");

        AnsiConsole.MarkupLine("[green]FFmpeg downloaded successfully![/]");
        ConfigurePaths(FfmpegDirectory);
        return FfmpegDirectory;
    }

    private static void ConfigurePaths(string directory)
    {
        // Configure Xabe.FFmpeg
        FFmpeg.SetExecutablesPath(directory);

        // Configure FFMpegCore
        FFMpegCore.GlobalFFOptions.Configure(options =>
        {
            options.BinaryFolder = directory;
        });
    }

    private static string? FindOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return null;

        var extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat"]
            : new[] { "" };

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, executable + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }
}

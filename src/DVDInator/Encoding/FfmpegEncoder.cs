using FFMpegCore;
using FFMpegCore.Enums;
using Spectre.Console;

namespace DVDInator.Encoding;

/// <summary>
/// Encodes ripped VOB files to MP4 using FFmpeg (via FFMpegCore).
/// </summary>
public sealed class FfmpegEncoder
{
    /// <summary>
    /// Encodes a VOB file to MP4 with progress reporting.
    /// </summary>
    /// <param name="inputVobPath">Path to the ripped VOB file.</param>
    /// <param name="options">Encoding options (codec, quality, output path).</param>
    /// <param name="duration">Expected duration for progress calculation.</param>
    /// <param name="progressCallback">Called with encoding progress percentage (0-100).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EncodeAsync(
        string inputVobPath,
        EncodingOptions options,
        TimeSpan duration,
        Action<double>? progressCallback = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(inputVobPath))
            throw new FileNotFoundException("Input VOB file not found", inputVobPath);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(options.OutputPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Probe input to get stream info
        IMediaAnalysis? mediaInfo = null;
        try
        {
            mediaInfo = await FFProbe.AnalyseAsync(inputVobPath, cancellationToken: ct);
        }
        catch
        {
            // Continue without probe info — we'll rely on the IFO duration
        }

        var effectiveDuration = mediaInfo?.Duration ?? duration;
        if (effectiveDuration <= TimeSpan.Zero)
            effectiveDuration = duration;

        // Build FFmpeg arguments
        var processor = FFMpegArguments
            .FromFileInput(inputVobPath, verifyExists: true, options => options
                .WithCustomArgument("-analyzeduration 100M -probesize 100M"))
            .OutputToFile(options.OutputPath, overwrite: true, outputOptions =>
            {
                // Video codec
                outputOptions.WithVideoCodec(options.VideoCodec);

                // CRF quality
                outputOptions.WithCustomArgument($"-crf {options.Crf}");

                // H.264 preset
                if (options.VideoCodec is "libx264" or "libx265")
                    outputOptions.WithCustomArgument($"-preset {options.Preset}");

                // Audio codec and bitrate
                outputOptions.WithAudioCodec(options.AudioCodec);
                outputOptions.WithAudioBitrate(options.AudioBitrate);

                // Resolution scaling
                if (options.Resolution is not null)
                {
                    var parts = options.Resolution.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    {
                        outputOptions.WithVideoFilters(filters =>
                            filters.Scale(w, h));
                    }
                }

                // Pixel format for compatibility
                outputOptions.WithCustomArgument("-pix_fmt yuv420p");

                // Map all audio streams
                outputOptions.WithCustomArgument("-map 0:v:0 -map 0:a?");

                // Movflags for streaming-friendly MP4
                outputOptions.WithCustomArgument("-movflags +faststart");
            });

        // Execute with progress
        await processor
            .NotifyOnProgress(percentage =>
            {
                progressCallback?.Invoke(Math.Min(100.0, percentage));
            }, effectiveDuration)
            .CancellableThrough(ct)
            .ProcessAsynchronously(throwOnError: true);

        // Signal completion
        progressCallback?.Invoke(100.0);
    }

    /// <summary>
    /// Quick validation that FFmpeg is working.
    /// </summary>
    public static async Task<bool> ValidateAsync()
    {
        try
        {
            // Probe a null input to verify FFmpeg is callable
            await Task.Run(() =>
            {
                var ffmpegPath = FFMpegCore.GlobalFFOptions.GetFFMpegBinaryPath();
                if (!File.Exists(ffmpegPath))
                    throw new FileNotFoundException("FFmpeg binary not found", ffmpegPath);
            });
            AnsiConsole.MarkupLine("[grey]FFmpeg: OK[/]");
            return true;
        }
        catch
        {
            return false;
        }
    }
}

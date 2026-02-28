using System.CommandLine;

namespace DVDInator.Cli;

/// <summary>
/// Defines CLI options and commands using System.CommandLine.
/// </summary>
public static class CliOptions
{
    public static readonly Option<string?> DriveOption = new("--drive", "-d")
    {
        Description = "DVD drive letter (e.g. D:). If omitted, auto-detects."
    };

    public static readonly Option<string> OutputOption = new("--output", "-o")
    {
        Description = "Output directory for the MP4 file(s).",
        DefaultValueFactory = _ => Environment.CurrentDirectory
    };

    public static readonly Option<int?> TitleOption = new("--title", "-t")
    {
        Description = "Title number to rip. If omitted, shows interactive selection."
    };

    public static readonly Option<string?> ChaptersOption = new("--chapters", "-c")
    {
        Description = "Chapter range to rip, e.g. '1-5' or '3'. If omitted, rips all chapters."
    };

    public static readonly Option<bool> DecryptOption = new("--decrypt")
    {
        Description = "Enable CSS decryption via libdvdcss. Requires libdvdcss-2.dll.",
        DefaultValueFactory = _ => false
    };

    public static readonly Option<int> QualityOption = new("--quality", "-q")
    {
        Description = "CRF quality value (0-51). Lower = better quality, larger file.",
        DefaultValueFactory = _ => 20
    };

    public static readonly Option<string?> ResolutionOption = new("--resolution", "-r")
    {
        Description = "Output resolution, e.g. '1920x1080'. Keep original if omitted."
    };

    public static readonly Option<string> PresetOption = new("--preset")
    {
        Description = "H.264 encoding preset (ultrafast, fast, medium, slow, veryslow).",
        DefaultValueFactory = _ => "medium"
    };

    public static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("DVDInator - DVD ripper and MP4 encoder")
        {
            DriveOption,
            OutputOption,
            TitleOption,
            ChaptersOption,
            DecryptOption,
            QualityOption,
            ResolutionOption,
            PresetOption
        };

        return rootCommand;
    }

    /// <summary>
    /// Parses a chapter range string like "1-5" or "3" into start/end tuple.
    /// </summary>
    public static (int start, int end)? ParseChapterRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return null;

        var parts = range.Split('-');
        if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out var single))
            return (single, single);

        if (parts.Length == 2
            && int.TryParse(parts[0].Trim(), out var start)
            && int.TryParse(parts[1].Trim(), out var end))
        {
            return (Math.Min(start, end), Math.Max(start, end));
        }

        throw new ArgumentException($"Invalid chapter range: '{range}'. Use format: '3' or '1-5'.");
    }
}

/// <summary>
/// Parsed CLI parameters passed to the main handler.
/// </summary>
public sealed class RipOptions
{
    public string? DriveLetter { get; init; }
    public required string OutputDirectory { get; init; }
    public int? TitleNumber { get; init; }
    public (int start, int end)? ChapterRange { get; init; }
    public bool Decrypt { get; init; }
    public int Crf { get; init; } = 20;
    public string? Resolution { get; init; }
    public string Preset { get; init; } = "medium";
}

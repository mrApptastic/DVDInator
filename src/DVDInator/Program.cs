using System.CommandLine;
using DVDInator.Cli;
using DVDInator.Decryption;
using DVDInator.Drive;
using DVDInator.Encoding;
using DVDInator.Ifo;
using DVDInator.Ripping;
using Spectre.Console;

// ─── DVDInator ──────────────────────────────────────────────────────────────────
// DVD Ripper & MP4 Encoder
// ─────────────────────────────────────────────────────────────────────────────────

var rootCommand = CliOptions.BuildRootCommand();

rootCommand.SetAction(async (parseResult, ct) =>
{
    var options = new RipOptions
    {
        DriveLetter = parseResult.GetValue(CliOptions.DriveOption),
        OutputDirectory = parseResult.GetValue(CliOptions.OutputOption) ?? Environment.CurrentDirectory,
        TitleNumber = parseResult.GetValue(CliOptions.TitleOption),
        ChapterRange = CliOptions.ParseChapterRange(
            parseResult.GetValue(CliOptions.ChaptersOption)),
        Decrypt = parseResult.GetValue(CliOptions.DecryptOption),
        Crf = parseResult.GetValue(CliOptions.QualityOption),
        Resolution = parseResult.GetValue(CliOptions.ResolutionOption),
        Preset = parseResult.GetValue(CliOptions.PresetOption) ?? "medium"
    };

    var exitCode = await RunAsync(options, ct);
    return exitCode;
});

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();

// ─── Main workflow ──────────────────────────────────────────────────────────────

static async Task<int> RunAsync(RipOptions options, CancellationToken ct)
{
    try
    {
        ConsoleUi.ShowBanner();

        // Step 1: Detect DVD drive
        AnsiConsole.MarkupLine("[yellow]Detecting DVD drive...[/]");
        var driveInfo = DvdDriveDetector.DetectDrive(options.DriveLetter);

        if (driveInfo is null)
        {
            ConsoleUi.ShowError("No DVD drive with a disc was detected.",
                new Exception("Insert a DVD and try again, or specify the drive with --drive D:"));
            return 1;
        }

        ConsoleUi.ShowDriveInfo(driveInfo.DriveLetter, driveInfo.VolumeLabel);

        // Step 2: Ensure FFmpeg is available
        AnsiConsole.MarkupLine("[yellow]Checking FFmpeg...[/]");
        await FfmpegBootstrapper.EnsureFfmpegAsync(ct);

        // Step 3: Parse IFO files for title/chapter info
        AnsiConsole.MarkupLine("[yellow]Reading DVD structure...[/]");
        var videoTsReader = new VideoTsReader(driveInfo.VideoTsPath);
        var ifoParser = new IfoParser(videoTsReader);
        var titles = ifoParser.ParseAllTitles();

        if (titles.Count == 0)
        {
            ConsoleUi.ShowError("No titles found on the DVD.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Found {titles.Count} title(s)[/]");

        // Step 4: Select title
        DvdTitle selectedTitle;
        if (options.TitleNumber.HasValue)
        {
            selectedTitle = titles.FirstOrDefault(t => t.TitleNumber == options.TitleNumber.Value)
                ?? throw new ArgumentException($"Title {options.TitleNumber.Value} not found on disc. Available: {string.Join(", ", titles.Select(t => t.TitleNumber))}");
            AnsiConsole.MarkupLine($"[green]Selected:[/] {selectedTitle}");
        }
        else
        {
            selectedTitle = ConsoleUi.SelectTitle(titles);
        }

        // Step 5: Select chapters
        var chapterRange = options.ChapterRange ?? ConsoleUi.SelectChapters(selectedTitle);

        if (chapterRange.HasValue)
            AnsiConsole.MarkupLine($"[green]Chapters:[/] {chapterRange.Value.start}-{chapterRange.Value.end}");
        else
            AnsiConsole.MarkupLine("[green]Chapters:[/] All");

        // Step 6: Prepare output path
        var outputFileName = BuildOutputFileName(driveInfo.VolumeLabel, selectedTitle, chapterRange);
        var outputPath = Path.Combine(options.OutputDirectory, outputFileName);

        if (!Directory.Exists(options.OutputDirectory))
            Directory.CreateDirectory(options.OutputDirectory);

        AnsiConsole.MarkupLine($"[green]Output:[/]   {outputPath}");
        AnsiConsole.WriteLine();

        // Step 7: Create decryptor
        IDvdDecryptor decryptor;
        if (options.Decrypt)
        {
            AnsiConsole.MarkupLine("[yellow]CSS decryption enabled[/]");
            decryptor = new LibDvdCssDecryptor();
            if (!decryptor.Open(driveInfo.DriveLetter))
            {
                ConsoleUi.ShowError("Failed to open DVD drive with libdvdcss.",
                    new Exception("Ensure libdvdcss-2.dll is present alongside DVDInator.exe"));
                return 1;
            }
        }
        else
        {
            decryptor = new PassthroughDecryptor();
            decryptor.Open(driveInfo.DriveLetter);
        }

        using (decryptor)
        {
            // Step 8: Rip & Encode with progress
            var ripper = new VobRipper(videoTsReader, decryptor, options.Decrypt);
            var encoder = new FfmpegEncoder();

            var encodingOptions = new EncodingOptions
            {
                VideoCodec = "libx264",
                Crf = options.Crf,
                AudioCodec = "aac",
                AudioBitrate = 192,
                Resolution = options.Resolution,
                Preset = options.Preset,
                OutputPath = outputPath
            };

            string? tempVobPath = null;

            try
            {
                await ConsoleUi.RunWithProgressAsync(
                    // Rip phase
                    async (progressCallback) =>
                    {
                        tempVobPath = await ripper.RipTitleAsync(
                            selectedTitle, chapterRange, progressCallback, ct);
                        return tempVobPath;
                    },
                    // Encode phase
                    async (vobPath, progressCallback) =>
                    {
                        await encoder.EncodeAsync(
                            vobPath, encodingOptions, selectedTitle.Duration, progressCallback, ct);
                    });

                ConsoleUi.ShowSuccess(outputPath);
                return 0;
            }
            finally
            {
                // Cleanup temp VOB file
                if (tempVobPath is not null && File.Exists(tempVobPath))
                {
                    try { File.Delete(tempVobPath); }
                    catch { /* best effort cleanup */ }
                }
            }
        }
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.MarkupLine("\n[yellow]Operation cancelled.[/]");
        return 1;
    }
    catch (Exception ex)
    {
        ConsoleUi.ShowError("An unexpected error occurred.", ex);
        return 1;
    }
}

// ─── Helpers ────────────────────────────────────────────────────────────────────

static string BuildOutputFileName(string volumeLabel, DvdTitle title, (int start, int end)? chapterRange)
{
    // Sanitize volume label for use as filename
    var safeName = string.Join("_", volumeLabel.Split(Path.GetInvalidFileNameChars()));
    if (string.IsNullOrWhiteSpace(safeName))
        safeName = "DVD";

    var chapterSuffix = chapterRange.HasValue
        ? $"_ch{chapterRange.Value.start}-{chapterRange.Value.end}"
        : "";

    return $"{safeName}_Title{title.TitleNumber}{chapterSuffix}.mp4";
}



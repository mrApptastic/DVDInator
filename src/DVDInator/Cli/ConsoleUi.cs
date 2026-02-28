using DVDInator.Ifo;
using Spectre.Console;

namespace DVDInator.Cli;

/// <summary>
/// Interactive console UI using Spectre.Console for title/chapter selection and progress.
/// </summary>
public static class ConsoleUi
{
    /// <summary>
    /// Displays the application banner.
    /// </summary>
    public static void ShowBanner()
    {
        AnsiConsole.Write(new FigletText("DVDInator")
            .Color(Color.Red)
            .Centered());

        AnsiConsole.Write(new Rule("[grey]DVD Ripper & MP4 Encoder[/]").RuleStyle("red dim"));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a table of all titles found on the DVD and lets the user pick one.
    /// </summary>
    public static DvdTitle SelectTitle(List<DvdTitle> titles)
    {
        if (titles.Count == 0)
            throw new InvalidOperationException("No titles found on the DVD.");

        if (titles.Count == 1)
        {
            AnsiConsole.MarkupLine($"[green]Found 1 title:[/] {titles[0]}");
            return titles[0];
        }

        // Display title table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold yellow]DVD Titles[/]")
            .AddColumn(new TableColumn("[bold]#[/]").Centered())
            .AddColumn(new TableColumn("[bold]Duration[/]").Centered())
            .AddColumn(new TableColumn("[bold]Chapters[/]").Centered())
            .AddColumn(new TableColumn("[bold]Audio[/]"))
            .AddColumn(new TableColumn("[bold]Angles[/]").Centered())
            .AddColumn(new TableColumn("[bold]Subtitles[/]").Centered());

        foreach (var title in titles)
        {
            var audioInfo = string.Join(", ", title.AudioStreams.Select(a =>
                $"{a.Language} ({a.Format}, {a.Channels}ch)"));
            if (string.IsNullOrEmpty(audioInfo)) audioInfo = "-";

            table.AddRow(
                $"[bold]{title.TitleNumber}[/]",
                $"[cyan]{title.Duration:hh\\:mm\\:ss}[/]",
                title.Chapters.Count.ToString(),
                audioInfo,
                title.AngleCount.ToString(),
                title.SubtitleStreams.Count.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Interactive selection
        return AnsiConsole.Prompt(
            new SelectionPrompt<DvdTitle>()
                .Title("[bold yellow]Select a title to rip:[/]")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up/down to see more titles)[/]")
                .AddChoices(titles)
                .UseConverter(t => $"Title {t.TitleNumber} - {t.Duration:hh\\:mm\\:ss} ({t.Chapters.Count} chapters, {string.Join("/", t.AudioStreams.Select(a => a.Language))})"));
    }

    /// <summary>
    /// Lets the user optionally select a chapter range from the chosen title.
    /// </summary>
    public static (int start, int end)? SelectChapters(DvdTitle title)
    {
        if (title.Chapters.Count <= 1)
            return null;

        // Show chapter table
        var table = new Table()
            .Border(TableBorder.Simple)
            .Title($"[bold]Chapters for Title {title.TitleNumber}[/]")
            .AddColumn(new TableColumn("[bold]#[/]").Centered())
            .AddColumn(new TableColumn("[bold]Start[/]").Centered())
            .AddColumn(new TableColumn("[bold]Duration[/]").Centered());

        foreach (var ch in title.Chapters)
        {
            table.AddRow(
                ch.ChapterNumber.ToString(),
                $"[grey]{ch.StartTime:hh\\:mm\\:ss}[/]",
                $"[cyan]{ch.Duration:mm\\:ss}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var ripAll = AnsiConsole.Confirm("[yellow]Rip all chapters?[/]", defaultValue: true);
        if (ripAll)
            return null;

        var start = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Start chapter:[/]")
                .DefaultValue(1)
                .Validate(v => v >= 1 && v <= title.Chapters.Count
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Must be between 1 and {title.Chapters.Count}")));

        var end = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]End chapter:[/]")
                .DefaultValue(title.Chapters.Count)
                .Validate(v => v >= start && v <= title.Chapters.Count
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Must be between {start} and {title.Chapters.Count}")));

        return (start, end);
    }

    /// <summary>
    /// Runs the rip and encode process with dual progress bars.
    /// </summary>
    public static async Task RunWithProgressAsync(
        Func<Action<long, long>, Task<string>> ripFunc,
        Func<string, Action<double>, Task> encodeFunc)
    {
        string? tempVobPath = null;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new TransferSpeedColumn(),
                new SpinnerColumn(),
                new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                // Phase 1: Ripping
                var ripTask = ctx.AddTask("[bold green]Ripping DVD[/]", maxValue: 100);

                tempVobPath = await ripFunc((bytesWritten, totalBytes) =>
                {
                    if (totalBytes > 0)
                    {
                        ripTask.MaxValue = 100;
                        ripTask.Value = (double)bytesWritten / totalBytes * 100;
                    }
                });

                ripTask.Value = 100;
                ripTask.StopTask();

                // Phase 2: Encoding
                var encodeTask = ctx.AddTask("[bold blue]Encoding MP4[/]", maxValue: 100);

                await encodeFunc(tempVobPath, percent =>
                {
                    encodeTask.Value = Math.Min(percent, 100);
                });

                encodeTask.Value = 100;
                encodeTask.StopTask();
            });
    }

    /// <summary>
    /// Shows a success message with output file info.
    /// </summary>
    public static void ShowSuccess(string outputPath)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[green]Complete![/]").RuleStyle("green"));

        var fileInfo = new FileInfo(outputPath);
        var sizeStr = fileInfo.Length switch
        {
            > 1024 * 1024 * 1024 => $"{fileInfo.Length / (1024.0 * 1024 * 1024):F2} GB",
            > 1024 * 1024 => $"{fileInfo.Length / (1024.0 * 1024):F1} MB",
            _ => $"{fileInfo.Length / 1024.0:F0} KB"
        };

        AnsiConsole.MarkupLine($"[green]Output:[/] {outputPath}");
        AnsiConsole.MarkupLine($"[green]Size:[/]   {sizeStr}");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Shows an error message in a styled panel.
    /// </summary>
    public static void ShowError(string message, Exception? ex = null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            new Markup($"[red bold]{Markup.Escape(message)}[/]" +
                (ex is not null ? $"\n[grey]{Markup.Escape(ex.Message)}[/]" : "")))
            .Header("[red]Error[/]")
            .Border(BoxBorder.Heavy)
            .BorderColor(Color.Red));
    }

    /// <summary>
    /// Shows drive information.
    /// </summary>
    public static void ShowDriveInfo(string driveLetter, string volumeLabel)
    {
        AnsiConsole.MarkupLine($"[green]DVD Drive:[/] {driveLetter} [grey]({volumeLabel})[/]");
    }
}

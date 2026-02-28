using Spectre.Console;

namespace DVDInator.Drive;

/// <summary>
/// Detects DVD drives with media inserted and validates VIDEO_TS structure.
/// </summary>
public static class DvdDriveDetector
{
    /// <summary>
    /// Finds all DVD drives with a disc inserted that contain a VIDEO_TS folder.
    /// </summary>
    public static List<DvdDriveInfo> DetectDvdDrives()
    {
        var drives = new List<DvdDriveInfo>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.CDRom)
                continue;

            if (!drive.IsReady)
                continue;

            var videoTsPath = Path.Combine(drive.RootDirectory.FullName, "VIDEO_TS");
            if (!Directory.Exists(videoTsPath))
                continue;

            drives.Add(new DvdDriveInfo
            {
                DriveLetter = drive.Name[..2], // "D:"
                VolumeLabel = drive.VolumeLabel,
                VideoTsPath = videoTsPath,
                TotalSize = drive.TotalSize
            });
        }

        return drives;
    }

    /// <summary>
    /// Detects a specific drive by letter, or auto-detects if driveLetter is null.
    /// Returns null if no suitable drive found.
    /// </summary>
    public static DvdDriveInfo? DetectDrive(string? driveLetter = null)
    {
        var drives = DetectDvdDrives();

        if (driveLetter is not null)
        {
            var normalized = driveLetter.TrimEnd(':', '\\') + ":";
            return drives.FirstOrDefault(d =>
                d.DriveLetter.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        return drives.Count switch
        {
            0 => null,
            1 => drives[0],
            _ => PromptForDrive(drives)
        };
    }

    private static DvdDriveInfo PromptForDrive(List<DvdDriveInfo> drives)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<DvdDriveInfo>()
                .Title("[bold yellow]Multiple DVD drives detected. Select one:[/]")
                .AddChoices(drives)
                .UseConverter(d => $"{d.DriveLetter} - {d.VolumeLabel}"));
    }
}

/// <summary>
/// Information about a detected DVD drive.
/// </summary>
public sealed class DvdDriveInfo
{
    public required string DriveLetter { get; init; }
    public required string VolumeLabel { get; init; }
    public required string VideoTsPath { get; init; }
    public required long TotalSize { get; init; }

    public override string ToString() => $"{DriveLetter} ({VolumeLabel})";
}

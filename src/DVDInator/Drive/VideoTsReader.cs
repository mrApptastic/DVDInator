namespace DVDInator.Drive;

/// <summary>
/// Reads and enumerates the VIDEO_TS folder structure from a DVD.
/// </summary>
public sealed class VideoTsReader
{
    private readonly string _videoTsPath;

    public VideoTsReader(string videoTsPath)
    {
        if (!Directory.Exists(videoTsPath))
            throw new DirectoryNotFoundException($"VIDEO_TS directory not found: {videoTsPath}");

        _videoTsPath = videoTsPath;
    }

    /// <summary>
    /// Gets the path to VIDEO_TS.IFO (the main IFO file).
    /// </summary>
    public string? GetMainIfoPath()
    {
        var path = Path.Combine(_videoTsPath, "VIDEO_TS.IFO");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Gets the path to a VTS IFO file, e.g. VTS_01_0.IFO for vtsNumber=1.
    /// </summary>
    public string? GetVtsIfoPath(int vtsNumber)
    {
        var fileName = $"VTS_{vtsNumber:D2}_0.IFO";
        var path = Path.Combine(_videoTsPath, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Gets all VOB file paths for a given VTS number, ordered by part number.
    /// Excludes the menu VOB (VTS_xx_0.VOB) by default.
    /// </summary>
    public List<string> GetVobFiles(int vtsNumber, bool includeMenu = false)
    {
        var pattern = $"VTS_{vtsNumber:D2}_*.VOB";
        var files = Directory.GetFiles(_videoTsPath, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!includeMenu)
        {
            var menuVob = Path.Combine(_videoTsPath, $"VTS_{vtsNumber:D2}_0.VOB");
            files.RemoveAll(f => f.Equals(menuVob, StringComparison.OrdinalIgnoreCase));
        }

        return files;
    }

    /// <summary>
    /// Enumerates all VTS (Video Title Set) numbers found on the disc.
    /// </summary>
    public List<int> GetVtsNumbers()
    {
        var ifoFiles = Directory.GetFiles(_videoTsPath, "VTS_*_0.IFO", SearchOption.TopDirectoryOnly);
        var vtsNumbers = new List<int>();

        foreach (var file in ifoFiles)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // VTS_01_0 → extract "01"
            var parts = name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var vtsNum))
            {
                vtsNumbers.Add(vtsNum);
            }
        }

        return vtsNumbers.OrderBy(n => n).ToList();
    }

    /// <summary>
    /// Gets the total size of all VOB files for a VTS, in bytes.
    /// </summary>
    public long GetVtsTotalSize(int vtsNumber)
    {
        return GetVobFiles(vtsNumber)
            .Select(f => new FileInfo(f))
            .Sum(fi => fi.Length);
    }

    /// <summary>
    /// Gets all files in VIDEO_TS categorized.
    /// </summary>
    public VideoTsContents GetContents()
    {
        return new VideoTsContents
        {
            MainIfo = GetMainIfoPath(),
            VtsNumbers = GetVtsNumbers(),
            VideoTsPath = _videoTsPath
        };
    }
}

public sealed class VideoTsContents
{
    public required string? MainIfo { get; init; }
    public required List<int> VtsNumbers { get; init; }
    public required string VideoTsPath { get; init; }
}

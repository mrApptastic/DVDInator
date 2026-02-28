namespace DVDInator.Ifo;

/// <summary>
/// Represents a single title on a DVD disc.
/// </summary>
public sealed class DvdTitle
{
    public required int TitleNumber { get; init; }
    public required int VtsNumber { get; init; }
    public required int TitleInVts { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int AngleCount { get; init; }
    public required List<DvdChapter> Chapters { get; init; }
    public required List<DvdAudioStream> AudioStreams { get; init; }
    public required List<DvdSubtitleStream> SubtitleStreams { get; init; }
    public required List<DvdCellAddress> CellAddresses { get; init; }

    /// <summary>
    /// VOB file IDs that contain data for this title.
    /// </summary>
    public required List<int> VobIds { get; init; }

    public override string ToString() =>
        $"Title {TitleNumber}: {Duration:hh\\:mm\\:ss} - {Chapters.Count} chapter(s), {AudioStreams.Count} audio, {AngleCount} angle(s)";
}

/// <summary>
/// Represents a chapter within a title.
/// </summary>
public sealed class DvdChapter
{
    public required int ChapterNumber { get; init; }
    public required int ProgramNumber { get; init; }
    public required int FirstCell { get; init; }
    public required int LastCell { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan StartTime { get; init; }
}

/// <summary>
/// Represents an audio stream in a VTS.
/// </summary>
public sealed class DvdAudioStream
{
    public required int StreamIndex { get; init; }
    public required string Language { get; init; }
    public required DvdAudioFormat Format { get; init; }
    public required int Channels { get; init; }
    public required int SampleRate { get; init; }
}

/// <summary>
/// Represents a subtitle stream in a VTS.
/// </summary>
public sealed class DvdSubtitleStream
{
    public required int StreamIndex { get; init; }
    public required string Language { get; init; }
}

/// <summary>
/// Represents a cell address entry from the cell address table.
/// Maps to sector ranges in VOB files.
/// </summary>
public sealed class DvdCellAddress
{
    public required int CellId { get; init; }
    public required int VobId { get; init; }
    public required int Angle { get; init; }
    public required long StartSector { get; init; }
    public required long LastSector { get; init; }

    public long SectorCount => LastSector - StartSector + 1;
}

public enum DvdAudioFormat
{
    Ac3,
    Mpeg1,
    Mpeg2,
    Lpcm,
    Dts,
    Unknown
}

using DVDInator.Drive;

namespace DVDInator.Ifo;

/// <summary>
/// Parses DVD IFO files (VIDEO_TS.IFO and VTS_xx_0.IFO) to extract title, chapter,
/// and stream information. All IFO data is big-endian.
/// </summary>
public sealed class IfoParser
{
    private readonly VideoTsReader _reader;

    public IfoParser(VideoTsReader reader)
    {
        _reader = reader;
    }

    /// <summary>
    /// Parses all titles from the DVD by reading VIDEO_TS.IFO and each VTS IFO file.
    /// </summary>
    public List<DvdTitle> ParseAllTitles()
    {
        var mainIfoPath = _reader.GetMainIfoPath()
            ?? throw new InvalidOperationException("VIDEO_TS.IFO not found on the disc.");

        var titleEntries = ParseTitleTable(mainIfoPath);
        var titles = new List<DvdTitle>();

        foreach (var entry in titleEntries)
        {
            var vtsIfoPath = _reader.GetVtsIfoPath(entry.VtsNumber);
            if (vtsIfoPath is null)
                continue;

            try
            {
                var title = ParseVtsTitle(vtsIfoPath, entry);
                titles.Add(title);
            }
            catch (Exception ex)
            {
                // Skip titles that fail to parse — DVDs can have unusual structures
                Console.Error.WriteLine($"Warning: Failed to parse title {entry.TitleNumber} (VTS {entry.VtsNumber}): {ex.Message}");
            }
        }

        return titles;
    }

    /// <summary>
    /// Parses the Title Table (TT_SRPT) from VIDEO_TS.IFO.
    /// Located at the offset stored at byte offset 0xC4 (4 bytes, sector pointer).
    /// </summary>
    private List<TitleTableEntry> ParseTitleTable(string mainIfoPath)
    {
        var data = File.ReadAllBytes(mainIfoPath);
        var entries = new List<TitleTableEntry>();

        // Validate IFO header: should start with "DVDVIDEO-VMG"
        var header = System.Text.Encoding.ASCII.GetString(data, 0, 12);
        if (header != "DVDVIDEO-VMG")
            throw new InvalidDataException($"Invalid VIDEO_TS.IFO header: {header}");

        // TT_SRPT sector pointer at offset 0xC4
        var ttSrptSector = ReadUInt32BE(data, 0xC4);
        var ttSrptOffset = (int)(ttSrptSector * 2048);

        if (ttSrptOffset == 0 || ttSrptOffset >= data.Length)
            throw new InvalidDataException("Invalid TT_SRPT offset in VIDEO_TS.IFO");

        // Number of title search pointers at TT_SRPT + 0
        var titleCount = ReadUInt16BE(data, ttSrptOffset);

        // Each TT_SRPT entry is 12 bytes, starting at TT_SRPT + 8
        for (int i = 0; i < titleCount; i++)
        {
            var entryOffset = ttSrptOffset + 8 + (i * 12);

            if (entryOffset + 12 > data.Length)
                break;

            var playbackType = data[entryOffset];
            var angleCount = data[entryOffset + 1];
            var chapterCount = ReadUInt16BE(data, entryOffset + 2);
            // Parental management mask at +4 (2 bytes) — skip
            var vtsNumber = data[entryOffset + 6];
            var titleInVts = data[entryOffset + 7];
            var vtsSector = ReadUInt32BE(data, entryOffset + 8);

            entries.Add(new TitleTableEntry
            {
                TitleNumber = i + 1,
                VtsNumber = vtsNumber,
                TitleInVts = titleInVts,
                ChapterCount = chapterCount,
                AngleCount = angleCount,
                VtsSector = vtsSector
            });
        }

        return entries;
    }

    /// <summary>
    /// Parses a VTS IFO file to extract detailed info for a specific title.
    /// </summary>
    private DvdTitle ParseVtsTitle(string vtsIfoPath, TitleTableEntry entry)
    {
        var data = File.ReadAllBytes(vtsIfoPath);

        // Validate VTS IFO header
        var header = System.Text.Encoding.ASCII.GetString(data, 0, 12);
        if (header != "DVDVIDEO-VTS")
            throw new InvalidDataException($"Invalid VTS IFO header: {header}");

        // Parse audio streams
        var audioStreams = ParseAudioStreams(data);

        // Parse subtitle streams
        var subtitleStreams = ParseSubtitleStreams(data);

        // Parse PGC (Program Chain) for duration and cell info
        var pgcInfo = ParsePgc(data, entry.TitleInVts);

        // Parse cell address table
        var cellAddresses = ParseCellAddressTable(data);

        // Map PGC cells to cell addresses
        var titleCells = MapCellsToCellAddresses(pgcInfo.Cells, cellAddresses);

        // Determine VOB IDs from cell addresses
        var vobIds = titleCells.Select(c => c.VobId).Distinct().OrderBy(v => v).ToList();

        // Build chapters from PGC program map
        var chapters = BuildChapters(pgcInfo, entry.ChapterCount);

        return new DvdTitle
        {
            TitleNumber = entry.TitleNumber,
            VtsNumber = entry.VtsNumber,
            TitleInVts = entry.TitleInVts,
            Duration = pgcInfo.Duration,
            AngleCount = entry.AngleCount,
            Chapters = chapters,
            AudioStreams = audioStreams,
            SubtitleStreams = subtitleStreams,
            CellAddresses = titleCells,
            VobIds = vobIds
        };
    }

    /// <summary>
    /// Parses audio stream attributes from VTS IFO.
    /// Audio attributes start at offset 0x200 in the VTS IFO.
    /// </summary>
    private static List<DvdAudioStream> ParseAudioStreams(byte[] data)
    {
        var streams = new List<DvdAudioStream>();

        // Number of audio streams at 0x200 (2 bytes)
        var count = ReadUInt16BE(data, 0x200);
        if (count > 8) count = 8; // DVD supports max 8

        for (int i = 0; i < count; i++)
        {
            var offset = 0x202 + (i * 8);
            if (offset + 8 > data.Length) break;

            var codingMode = (data[offset] >> 5) & 0x07;
            var channels = (data[offset + 1] & 0x07) + 1;
            var sampleRate = ((data[offset + 1] >> 4) & 0x03) == 0 ? 48000 : 96000;

            // Language code at offset + 2 (2 bytes, ISO 639)
            var langByte1 = data[offset + 2];
            var langByte2 = data[offset + 3];
            var language = (langByte1 > 0 && langByte2 > 0)
                ? $"{(char)langByte1}{(char)langByte2}"
                : "und";

            var format = codingMode switch
            {
                0 => DvdAudioFormat.Ac3,
                2 => DvdAudioFormat.Mpeg1,
                3 => DvdAudioFormat.Mpeg2,
                4 => DvdAudioFormat.Lpcm,
                6 => DvdAudioFormat.Dts,
                _ => DvdAudioFormat.Unknown
            };

            streams.Add(new DvdAudioStream
            {
                StreamIndex = i,
                Language = language,
                Format = format,
                Channels = channels,
                SampleRate = sampleRate
            });
        }

        return streams;
    }

    /// <summary>
    /// Parses subtitle stream attributes from VTS IFO.
    /// Subtitle attributes start at offset 0x254.
    /// </summary>
    private static List<DvdSubtitleStream> ParseSubtitleStreams(byte[] data)
    {
        var streams = new List<DvdSubtitleStream>();

        // Number of subtitle streams at 0x254 (2 bytes)
        var count = ReadUInt16BE(data, 0x254);
        if (count > 32) count = 32; // DVD supports max 32

        for (int i = 0; i < count; i++)
        {
            var offset = 0x256 + (i * 6);
            if (offset + 6 > data.Length) break;

            // Language code at offset + 2 (2 bytes, ISO 639)
            var langByte1 = data[offset + 2];
            var langByte2 = data[offset + 3];
            var language = (langByte1 > 0 && langByte2 > 0)
                ? $"{(char)langByte1}{(char)langByte2}"
                : "und";

            streams.Add(new DvdSubtitleStream
            {
                StreamIndex = i,
                Language = language
            });
        }

        return streams;
    }

    /// <summary>
    /// Parses the Program Chain (PGC) for a title within a VTS.
    /// The VTS_PGCI (PGC Information table) sector is at offset 0xCC in the VTS IFO.
    /// </summary>
    private static PgcInfo ParsePgc(byte[] data, int titleInVts)
    {
        // VTS_PGCI sector pointer at 0xCC (4 bytes)
        var pgciSector = ReadUInt32BE(data, 0xCC);
        var pgciOffset = (int)(pgciSector * 2048);

        if (pgciOffset == 0 || pgciOffset >= data.Length)
            throw new InvalidDataException("Invalid VTS_PGCI offset");

        // Number of PGC's at PGCI + 0 (2 bytes)
        var pgcCount = ReadUInt16BE(data, pgciOffset);

        // Find the right PGC for our title
        var pgcIndex = Math.Min(titleInVts, pgcCount) - 1;
        if (pgcIndex < 0) pgcIndex = 0;

        // PGC search pointer: PGCI + 8 + (pgcIndex * 8)
        // Each entry: 1 byte category, 3 bytes padding(?), 4 bytes offset from PGCI start
        var searchOffset = pgciOffset + 8 + (pgcIndex * 8);
        if (searchOffset + 8 > data.Length)
            throw new InvalidDataException("PGC search pointer out of bounds");

        var pgcOffset = pgciOffset + (int)ReadUInt32BE(data, searchOffset + 4);

        if (pgcOffset + 236 > data.Length)
            throw new InvalidDataException("PGC data out of bounds");

        // PGC duration at PGC + 4 (4 bytes, BCD encoded)
        var duration = ParseBcdDuration(data, pgcOffset + 4);

        // Number of programs at PGC + 2
        var programCount = data[pgcOffset + 2];

        // Number of cells at PGC + 3
        var cellCount = data[pgcOffset + 3];

        // Program map offset at PGC + 0xE6 (2 bytes, relative to PGC start)
        var programMapRelOffset = ReadUInt16BE(data, pgcOffset + 0xE6);
        var programMapOffset = pgcOffset + programMapRelOffset;

        // Cell playback info offset at PGC + 0xE8 (2 bytes, relative to PGC start)
        var cellPlaybackRelOffset = ReadUInt16BE(data, pgcOffset + 0xE8);
        var cellPlaybackOffset = pgcOffset + cellPlaybackRelOffset;

        // Parse program map (maps program number → first cell number)
        var programMap = new List<int>();
        for (int i = 0; i < programCount; i++)
        {
            if (programMapOffset + i < data.Length)
                programMap.Add(data[programMapOffset + i]);
        }

        // Parse cell playback info (24 bytes per cell)
        var cells = new List<PgcCell>();
        for (int i = 0; i < cellCount; i++)
        {
            var cpOffset = cellPlaybackOffset + (i * 24);
            if (cpOffset + 24 > data.Length) break;

            var cellType = data[cpOffset];
            var cellDuration = ParseBcdDuration(data, cpOffset + 4);
            var firstVobuStart = ReadUInt32BE(data, cpOffset + 8);
            var firstIlvuEnd = ReadUInt32BE(data, cpOffset + 12);
            var lastVobuStart = ReadUInt32BE(data, cpOffset + 16);
            var lastVobuEnd = ReadUInt32BE(data, cpOffset + 20);

            cells.Add(new PgcCell
            {
                CellNumber = i + 1,
                CellType = cellType,
                Duration = cellDuration,
                FirstSector = firstVobuStart,
                LastSector = lastVobuEnd
            });
        }

        return new PgcInfo
        {
            Duration = duration,
            ProgramCount = programCount,
            CellCount = cellCount,
            ProgramMap = programMap,
            Cells = cells
        };
    }

    /// <summary>
    /// Parses the Cell Address Table (C_ADT) from the VTS IFO.
    /// C_ADT sector pointer is at offset 0xE0 in the VTS IFO.
    /// </summary>
    private static List<DvdCellAddress> ParseCellAddressTable(byte[] data)
    {
        var addresses = new List<DvdCellAddress>();

        // C_ADT sector pointer at 0xE0 (4 bytes)
        var cadtSector = ReadUInt32BE(data, 0xE0);
        var cadtOffset = (int)(cadtSector * 2048);

        if (cadtSector == 0 || cadtOffset >= data.Length)
            return addresses;

        // Number of entries: (end_byte + 1 - 8) / 12
        // End byte at C_ADT + 4 (4 bytes)
        var endByte = ReadUInt32BE(data, cadtOffset + 4);
        var entryCount = (int)((endByte + 1 - 8) / 12);

        for (int i = 0; i < entryCount; i++)
        {
            var entryOffset = cadtOffset + 8 + (i * 12);
            if (entryOffset + 12 > data.Length) break;

            var vobId = ReadUInt16BE(data, entryOffset);
            var cellId = data[entryOffset + 2];
            var angle = data[entryOffset + 3];
            var startSector = ReadUInt32BE(data, entryOffset + 4);
            var lastSector = ReadUInt32BE(data, entryOffset + 8);

            addresses.Add(new DvdCellAddress
            {
                VobId = vobId,
                CellId = cellId,
                Angle = angle,
                StartSector = startSector,
                LastSector = lastSector
            });
        }

        return addresses;
    }

    /// <summary>
    /// Maps PGC cells to cell address table entries by matching sector ranges.
    /// </summary>
    private static List<DvdCellAddress> MapCellsToCellAddresses(
        List<PgcCell> pgcCells, List<DvdCellAddress> cellAddresses)
    {
        var mapped = new List<DvdCellAddress>();

        foreach (var cell in pgcCells)
        {
            // Find the cell address entry that contains this cell's sector range
            var match = cellAddresses.FirstOrDefault(ca =>
                ca.StartSector == cell.FirstSector && ca.LastSector == cell.LastSector);

            if (match is not null)
            {
                mapped.Add(match);
            }
            else
            {
                // Fall back: find by overlapping sector range
                var overlap = cellAddresses.FirstOrDefault(ca =>
                    ca.StartSector <= cell.FirstSector && ca.LastSector >= cell.LastSector);

                if (overlap is not null)
                    mapped.Add(overlap);
                else
                {
                    // Create a synthetic entry from PGC cell data
                    mapped.Add(new DvdCellAddress
                    {
                        CellId = cell.CellNumber,
                        VobId = 1,
                        Angle = 0,
                        StartSector = cell.FirstSector,
                        LastSector = cell.LastSector
                    });
                }
            }
        }

        return mapped;
    }

    /// <summary>
    /// Builds chapter list from PGC program map and cell info.
    /// </summary>
    private static List<DvdChapter> BuildChapters(PgcInfo pgc, int chapterCount)
    {
        var chapters = new List<DvdChapter>();
        var actualCount = Math.Min(chapterCount, pgc.ProgramMap.Count);
        var runningTime = TimeSpan.Zero;

        for (int i = 0; i < actualCount; i++)
        {
            var firstCell = pgc.ProgramMap[i];
            var lastCell = (i + 1 < pgc.ProgramMap.Count)
                ? pgc.ProgramMap[i + 1] - 1
                : pgc.CellCount;

            // Sum duration of cells in this chapter
            var chapterDuration = TimeSpan.Zero;
            for (int c = firstCell; c <= lastCell && c <= pgc.Cells.Count; c++)
            {
                chapterDuration += pgc.Cells[c - 1].Duration;
            }

            chapters.Add(new DvdChapter
            {
                ChapterNumber = i + 1,
                ProgramNumber = i + 1,
                FirstCell = firstCell,
                LastCell = lastCell,
                Duration = chapterDuration,
                StartTime = runningTime
            });

            runningTime += chapterDuration;
        }

        return chapters;
    }

    /// <summary>
    /// Parses a BCD-encoded DVD duration (4 bytes: HH MM SS FF).
    /// Frame rate flag is in the upper 2 bits of the frame byte.
    /// </summary>
    private static TimeSpan ParseBcdDuration(byte[] data, int offset)
    {
        var hours = BcdToByte(data[offset]);
        var minutes = BcdToByte(data[offset + 1]);
        var seconds = BcdToByte(data[offset + 2]);
        var frames = BcdToByte((byte)(data[offset + 3] & 0x3F));
        var fpsFlag = (data[offset + 3] >> 6) & 0x03;
        var fps = fpsFlag == 3 ? 30.0 : 25.0;

        var ms = (int)(frames / fps * 1000);

        return new TimeSpan(0, hours, minutes, seconds, ms);
    }

    private static int BcdToByte(byte bcd)
    {
        return ((bcd >> 4) & 0x0F) * 10 + (bcd & 0x0F);
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
             | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8)
             | data[offset + 3];
    }

    #region Internal Models

    private sealed class TitleTableEntry
    {
        public int TitleNumber { get; init; }
        public int VtsNumber { get; init; }
        public int TitleInVts { get; init; }
        public int ChapterCount { get; init; }
        public int AngleCount { get; init; }
        public long VtsSector { get; init; }
    }

    private sealed class PgcInfo
    {
        public TimeSpan Duration { get; init; }
        public int ProgramCount { get; init; }
        public int CellCount { get; init; }
        public List<int> ProgramMap { get; init; } = [];
        public List<PgcCell> Cells { get; init; } = [];
    }

    private sealed class PgcCell
    {
        public int CellNumber { get; init; }
        public byte CellType { get; init; }
        public TimeSpan Duration { get; init; }
        public long FirstSector { get; init; }
        public long LastSector { get; init; }
    }

    #endregion
}

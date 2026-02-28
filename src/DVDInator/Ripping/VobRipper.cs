using DVDInator.Decryption;
using DVDInator.Drive;
using DVDInator.Ifo;

namespace DVDInator.Ripping;

/// <summary>
/// Rips VOB data for a DVD title, optionally decrypting CSS protection.
/// Writes the raw MPEG-PS data to a temp file for subsequent encoding.
/// </summary>
public sealed class VobRipper
{
    private const int SectorSize = 2048;
    private const int SectorsPerRead = 64; // 128KB per read

    private readonly VideoTsReader _videoTsReader;
    private readonly IDvdDecryptor _decryptor;
    private readonly bool _decrypt;

    public VobRipper(VideoTsReader videoTsReader, IDvdDecryptor decryptor, bool decrypt)
    {
        _videoTsReader = videoTsReader;
        _decryptor = decryptor;
        _decrypt = decrypt;
    }

    /// <summary>
    /// Rips all VOB data for a title to a temporary file.
    /// </summary>
    /// <param name="title">The DVD title to rip.</param>
    /// <param name="chapterRange">Optional chapter range (1-based, inclusive). Null = all chapters.</param>
    /// <param name="progressCallback">Called with (bytesWritten, totalBytes) for progress reporting.</param>
    /// <returns>Path to the temporary VOB file.</returns>
    public async Task<string> RipTitleAsync(
        DvdTitle title,
        (int start, int end)? chapterRange,
        Action<long, long>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dvdinator_title{title.TitleNumber}_{Guid.NewGuid():N}.vob");

        try
        {
            if (_decrypt && _decryptor.SupportsDecryption)
            {
                await RipWithDecryptionAsync(title, chapterRange, tempFile, progressCallback, cancellationToken);
            }
            else
            {
                await RipDirectAsync(title, chapterRange, tempFile, progressCallback, cancellationToken);
            }

            return tempFile;
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            throw;
        }
    }

    /// <summary>
    /// Rips using libdvdcss for CSS decryption. Reads sectors through the decryptor.
    /// </summary>
    private async Task RipWithDecryptionAsync(
        DvdTitle title,
        (int start, int end)? chapterRange,
        string outputPath,
        Action<long, long>? progressCallback,
        CancellationToken ct)
    {
        var cells = GetCellsForChapterRange(title, chapterRange);
        var totalSectors = cells.Sum(c => c.SectorCount);
        var totalBytes = totalSectors * SectorSize;
        long bytesWritten = 0;

        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: SectorSize * SectorsPerRead);

        var buffer = new byte[SectorsPerRead * SectorSize];

        foreach (var cell in cells)
        {
            ct.ThrowIfCancellationRequested();

            // Seek to cell start with key request for decryption
            var seekResult = _decryptor.Seek((int)cell.StartSector, requestKey: true);
            if (seekResult < 0)
                throw new IOException($"Failed to seek to sector {cell.StartSector}");

            var sectorsRemaining = (int)cell.SectorCount;
            while (sectorsRemaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                var toRead = Math.Min(sectorsRemaining, SectorsPerRead);
                var sectorsRead = _decryptor.Read(buffer, toRead, decrypt: true);

                if (sectorsRead <= 0)
                    throw new IOException($"Failed to read sectors (requested {toRead}, got {sectorsRead})");

                var bytesRead = sectorsRead * SectorSize;
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                bytesWritten += bytesRead;
                sectorsRemaining -= sectorsRead;

                progressCallback?.Invoke(bytesWritten, totalBytes);
            }
        }

        await output.FlushAsync(ct);
    }

    /// <summary>
    /// Rips by directly reading VOB files (no decryption). For unencrypted DVDs.
    /// </summary>
    private async Task RipDirectAsync(
        DvdTitle title,
        (int start, int end)? chapterRange,
        string outputPath,
        Action<long, long>? progressCallback,
        CancellationToken ct)
    {
        var vobFiles = _videoTsReader.GetVobFiles(title.VtsNumber);
        if (vobFiles.Count == 0)
            throw new FileNotFoundException($"No VOB files found for VTS {title.VtsNumber}");

        // Calculate total size
        var totalBytes = vobFiles.Sum(f => new FileInfo(f).Length);
        long bytesWritten = 0;

        // If chapter range is specified, we need cell addresses to seek
        var cells = chapterRange.HasValue
            ? GetCellsForChapterRange(title, chapterRange)
            : null;

        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 1024 * 1024);

        var buffer = new byte[1024 * 1024]; // 1MB read buffer

        if (cells is not null)
        {
            // Chapter-specific rip: read sectors from cell address ranges
            totalBytes = cells.Sum(c => c.SectorCount * SectorSize);

            foreach (var cell in cells)
            {
                ct.ThrowIfCancellationRequested();

                // Find the VOB file(s) containing these sectors
                await ReadSectorRangeFromVobsAsync(vobFiles, cell.StartSector, cell.SectorCount,
                    output, buffer, (written) =>
                    {
                        bytesWritten += written;
                        progressCallback?.Invoke(bytesWritten, totalBytes);
                    }, ct);
            }
        }
        else
        {
            // Full title rip: concatenate all VOB files
            foreach (var vobFile in vobFiles)
            {
                ct.ThrowIfCancellationRequested();

                await using var input = new FileStream(vobFile, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: 1024 * 1024);

                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    bytesWritten += bytesRead;
                    progressCallback?.Invoke(bytesWritten, totalBytes);
                }
            }
        }

        await output.FlushAsync(ct);
    }

    /// <summary>
    /// Reads a range of sectors from the VOB file set.
    /// VOB files are split at ~1GB boundaries, so a sector range may span multiple files.
    /// </summary>
    private static async Task ReadSectorRangeFromVobsAsync(
        List<string> vobFiles,
        long startSector,
        long sectorCount,
        FileStream output,
        byte[] buffer,
        Action<long> bytesWrittenCallback,
        CancellationToken ct)
    {
        // Build a map of sector ranges per VOB file
        long currentSector = 0;
        var vobRanges = new List<(string file, long fileStartSector, long fileEndSector)>();

        foreach (var vob in vobFiles)
        {
            var fileSize = new FileInfo(vob).Length;
            var fileSectors = fileSize / SectorSize;
            vobRanges.Add((vob, currentSector, currentSector + fileSectors - 1));
            currentSector += fileSectors;
        }

        var endSector = startSector + sectorCount - 1;

        foreach (var (file, fileStart, fileEnd) in vobRanges)
        {
            // Check if this VOB overlaps with our target range
            if (fileEnd < startSector || fileStart > endSector)
                continue;

            var readStart = Math.Max(startSector, fileStart);
            var readEnd = Math.Min(endSector, fileEnd);
            var offsetInFile = (readStart - fileStart) * SectorSize;
            var bytesToRead = (readEnd - readStart + 1) * SectorSize;

            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 1024 * 1024);
            input.Position = offsetInFile;

            long remaining = bytesToRead;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(remaining, buffer.Length);
                var read = await input.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read == 0) break;

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                remaining -= read;
                bytesWrittenCallback(read);
            }
        }
    }

    /// <summary>
    /// Gets the cell addresses for a specific chapter range within a title.
    /// </summary>
    private static List<DvdCellAddress> GetCellsForChapterRange(
        DvdTitle title, (int start, int end)? chapterRange)
    {
        if (chapterRange is null)
            return title.CellAddresses;

        var (start, end) = chapterRange.Value;
        var chapters = title.Chapters
            .Where(c => c.ChapterNumber >= start && c.ChapterNumber <= end)
            .ToList();

        if (chapters.Count == 0)
            return title.CellAddresses;

        var firstCell = chapters.Min(c => c.FirstCell);
        var lastCell = chapters.Max(c => c.LastCell);

        // Get cell addresses for the cells in the selected chapters
        return title.CellAddresses
            .Where((_, index) => index + 1 >= firstCell && index + 1 <= lastCell)
            .ToList();
    }
}

namespace DVDInator.Decryption;

/// <summary>
/// No-op decryptor for unencrypted DVDs. Reads sectors via standard file I/O.
/// </summary>
public sealed class PassthroughDecryptor : IDvdDecryptor
{
    private const int SectorSize = 2048;

    private FileStream? _stream;
    private string? _devicePath;

    public bool SupportsDecryption => false;
    public bool IsOpen => _stream is not null;

    public bool Open(string devicePath)
    {
        _devicePath = devicePath;
        // For passthrough, we don't open a device-level handle.
        // Instead, we read VOB files directly via the VideoTsReader/VobRipper.
        // This Open() is just for interface consistency.
        return true;
    }

    /// <summary>
    /// Opens a specific file (VOB) for sector-level reading.
    /// </summary>
    public bool OpenFile(string filePath)
    {
        CloseStream();
        try
        {
            _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: SectorSize * 64);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int Seek(int sector, bool requestKey = false)
    {
        if (_stream is null) return -1;

        var position = (long)sector * SectorSize;
        if (position > _stream.Length) return -1;

        _stream.Position = position;
        return sector;
    }

    public int Read(byte[] buffer, int sectorCount, bool decrypt = false)
    {
        if (_stream is null) return -1;

        var bytesToRead = sectorCount * SectorSize;
        if (buffer.Length < bytesToRead)
            throw new ArgumentException($"Buffer too small: need {bytesToRead} bytes, got {buffer.Length}");

        var totalRead = 0;
        while (totalRead < bytesToRead)
        {
            var read = _stream.Read(buffer, totalRead, bytesToRead - totalRead);
            if (read == 0) break; // EOF
            totalRead += read;
        }

        return totalRead / SectorSize;
    }

    public void Dispose()
    {
        CloseStream();
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
    }
}

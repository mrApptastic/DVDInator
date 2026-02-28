namespace DVDInator.Decryption;

/// <summary>
/// Abstraction for reading DVD sectors, with optional decryption.
/// </summary>
public interface IDvdDecryptor : IDisposable
{
    /// <summary>
    /// Opens a DVD device for reading.
    /// </summary>
    /// <param name="devicePath">The device path, e.g. "D:" or "\\.\D:"</param>
    /// <returns>True if the device was opened successfully.</returns>
    bool Open(string devicePath);

    /// <summary>
    /// Seeks to the specified sector.
    /// </summary>
    /// <param name="sector">The logical sector number.</param>
    /// <param name="requestKey">If true, request a title key for this sector (for CSS decryption).</param>
    /// <returns>The sector seeked to, or -1 on error.</returns>
    int Seek(int sector, bool requestKey = false);

    /// <summary>
    /// Reads sectors from the DVD.
    /// </summary>
    /// <param name="buffer">Buffer to read into. Must be at least sectorCount * 2048 bytes.</param>
    /// <param name="sectorCount">Number of sectors to read.</param>
    /// <param name="decrypt">If true, decrypt the data (CSS).</param>
    /// <returns>Number of sectors actually read, or -1 on error.</returns>
    int Read(byte[] buffer, int sectorCount, bool decrypt = false);

    /// <summary>
    /// Whether this decryptor supports CSS decryption.
    /// </summary>
    bool SupportsDecryption { get; }

    /// <summary>
    /// Whether the device is currently open.
    /// </summary>
    bool IsOpen { get; }
}

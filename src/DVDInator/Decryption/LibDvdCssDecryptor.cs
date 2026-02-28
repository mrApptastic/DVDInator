using System.Runtime.InteropServices;

namespace DVDInator.Decryption;

/// <summary>
/// DVD CSS decryption using libdvdcss (libdvdcss-2.dll / libdvdcss.so).
/// Requires libdvdcss-2.dll to be present alongside the application or on PATH.
/// </summary>
public sealed class LibDvdCssDecryptor : IDvdDecryptor
{
    private const int SectorSize = 2048;

    // libdvdcss flags
    private const int DVDCSS_NOFLAGS = 0;
    private const int DVDCSS_READ_DECRYPT = 1;
    private const int DVDCSS_SEEK_MPEG = 1;
    private const int DVDCSS_SEEK_KEY = 2;

    private nint _handle;

    #region P/Invoke

    [DllImport("libdvdcss-2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dvdcss_open")]
    private static extern nint NativeOpen([MarshalAs(UnmanagedType.LPStr)] string device);

    [DllImport("libdvdcss-2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dvdcss_close")]
    private static extern int NativeClose(nint handle);

    [DllImport("libdvdcss-2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dvdcss_seek")]
    private static extern int NativeSeek(nint handle, int sector, int flags);

    [DllImport("libdvdcss-2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dvdcss_read")]
    private static extern int NativeRead(nint handle, nint buffer, int sectors, int flags);

    [DllImport("libdvdcss-2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dvdcss_error")]
    private static extern nint NativeError(nint handle);

    #endregion

    public bool SupportsDecryption => true;
    public bool IsOpen => _handle != nint.Zero;

    public bool Open(string devicePath)
    {
        if (IsOpen)
            Close();

        try
        {
            _handle = NativeOpen(devicePath);
            return _handle != nint.Zero;
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException(
                "libdvdcss-2.dll was not found. To use CSS decryption:\n" +
                "  1. Download libdvdcss from https://www.videolan.org/developers/libdvdcss.html\n" +
                "  2. Place libdvdcss-2.dll in the same directory as DVDInator.exe\n" +
                "  3. Or add its directory to your system PATH");
        }
    }

    public int Seek(int sector, bool requestKey = false)
    {
        EnsureOpen();
        var flags = requestKey ? DVDCSS_SEEK_KEY : DVDCSS_NOFLAGS;
        return NativeSeek(_handle, sector, flags);
    }

    public int Read(byte[] buffer, int sectorCount, bool decrypt = false)
    {
        EnsureOpen();

        if (buffer.Length < sectorCount * SectorSize)
            throw new ArgumentException(
                $"Buffer too small: need {sectorCount * SectorSize} bytes, got {buffer.Length}");

        var flags = decrypt ? DVDCSS_READ_DECRYPT : DVDCSS_NOFLAGS;

        unsafe
        {
            fixed (byte* pBuffer = buffer)
            {
                return NativeRead(_handle, (nint)pBuffer, sectorCount, flags);
            }
        }
    }

    private string GetLastError()
    {
        if (_handle == nint.Zero) return "Not open";
        var errPtr = NativeError(_handle);
        return errPtr != nint.Zero ? Marshal.PtrToStringAnsi(errPtr) ?? "Unknown error" : "Unknown error";
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("DVD device is not open. Call Open() first.");
    }

    public void Dispose()
    {
        Close();
    }

    private void Close()
    {
        if (_handle != nint.Zero)
        {
            NativeClose(_handle);
            _handle = nint.Zero;
        }
    }
}

namespace DVDInator.Encoding;

/// <summary>
/// Options for FFmpeg encoding from VOB to MP4.
/// </summary>
public sealed class EncodingOptions
{
    /// <summary>
    /// Video codec. Default: libx264 (H.264).
    /// </summary>
    public string VideoCodec { get; init; } = "libx264";

    /// <summary>
    /// Constant Rate Factor for quality. Lower = better quality, larger file.
    /// Range: 0–51, default 20.
    /// </summary>
    public int Crf { get; init; } = 20;

    /// <summary>
    /// Audio codec. Default: aac.
    /// </summary>
    public string AudioCodec { get; init; } = "aac";

    /// <summary>
    /// Audio bitrate in kbps. Default: 192.
    /// </summary>
    public int AudioBitrate { get; init; } = 192;

    /// <summary>
    /// Output resolution, e.g. "1920x1080". Null means keep original.
    /// </summary>
    public string? Resolution { get; init; }

    /// <summary>
    /// H.264 encoding preset. Default: "medium".
    /// </summary>
    public string Preset { get; init; } = "medium";

    /// <summary>
    /// Output file path (full path to the .mp4 file).
    /// </summary>
    public required string OutputPath { get; init; }
}

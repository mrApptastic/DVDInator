# DVDInator

A .NET 10 console application that rips DVDs and encodes them to MP4 format. Features interactive title/chapter selection, optional CSS decryption via libdvdcss, and automatic FFmpeg downloading.

## Features

- **DVD Detection** — Auto-detects DVD drives with inserted discs
- **IFO Parsing** — Reads DVD structure to list titles, chapters, audio streams, and subtitles
- **Interactive UI** — Rich console interface with title/chapter selection and progress bars (Spectre.Console)
- **CSS Decryption** — Optional decryption of CSS-protected DVDs via libdvdcss (opt-in with `--decrypt`)
- **MP4 Encoding** — Converts DVD video to H.264/AAC MP4 using FFmpeg
- **Auto FFmpeg Download** — Downloads FFmpeg binaries automatically on first run if not found on PATH
- **Chapter Selection** — Rip specific chapters or entire titles
- **Quality Control** — Configurable CRF, resolution, preset, and audio bitrate

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A physical DVD drive (or a mounted ISO appearing as a CD-ROM drive)
- **(Optional)** [libdvdcss](https://www.videolan.org/developers/libdvdcss.html) — required only for CSS-protected DVDs
  - Download `libdvdcss-2.dll` and place it next to the DVDInator executable or on your system PATH
- **FFmpeg** is required for encoding but will be **downloaded automatically** on first run if not found on PATH

> **Platform support:** Currently tested on Windows. The libdvdcss P/Invoke layer references `libdvdcss-2.dll`; on Linux/macOS, the library name would need adjustment (`libdvdcss.so` / `libdvdcss.dylib`).

## Build

```bash
dotnet build src/DVDInator/DVDInator.csproj
```

## Publish (standalone executable)

```bash
# Framework-dependent (requires .NET 10 runtime on the target machine)
dotnet publish src/DVDInator/DVDInator.csproj -c Release -o publish

# Self-contained single-file (no runtime required, ~70MB)
dotnet publish src/DVDInator/DVDInator.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

After publishing, run directly:
```bash
./publish/DVDInator.exe
```

## Usage

### Interactive mode (recommended)

```bash
dotnet run --project src/DVDInator
```

The app will:
1. Detect your DVD drive
2. Download FFmpeg if needed
3. Parse the DVD structure
4. Show an interactive title selection table
5. Let you choose chapters
6. Rip and encode with progress bars

### Command-line options

```
DVDInator [options]

Options:
  -d, --drive <drive>         DVD drive letter (e.g. D:). Auto-detects if omitted.
  -o, --output <dir>          Output directory for MP4 file(s). Default: current directory.
  -t, --title <number>        Title number to rip. Interactive selection if omitted.
  -c, --chapters <range>      Chapter range, e.g. '1-5' or '3'. All chapters if omitted.
  --decrypt                   Enable CSS decryption via libdvdcss.
  -q, --quality <crf>         CRF quality (0-51, default: 20). Lower = better quality.
  -r, --resolution <WxH>      Output resolution, e.g. '1920x1080'. Keeps original if omitted.
  --preset <preset>           H.264 preset (ultrafast/fast/medium/slow/veryslow, default: medium).
  --help                      Show help and usage information.
  --version                   Show version information.
```

### Output file naming

Output files are named automatically based on the disc's volume label:

```
{VolumeLabel}_Title{N}.mp4              # full title rip
{VolumeLabel}_Title{N}_ch{S}-{E}.mp4    # chapter range rip
```

For example: `MY_MOVIE_Title1_ch2-5.mp4`

### Examples

```bash
# Rip title 1 from drive D: with default quality
dotnet run --project src/DVDInator -- --drive D: --title 1

# Rip chapters 2-5 of title 1, high quality
dotnet run --project src/DVDInator -- --title 1 --chapters 2-5 --quality 18

# Rip with CSS decryption enabled
dotnet run --project src/DVDInator -- --decrypt --title 1

# Custom output directory and resolution
dotnet run --project src/DVDInator -- --output C:\Movies --resolution 1280x720
```

## Project Structure

```
src/DVDInator/
├── Program.cs                   # Entry point, orchestrates the rip workflow
├── Cli/
│   ├── CliOptions.cs            # System.CommandLine option definitions
│   └── ConsoleUi.cs             # Spectre.Console interactive UI
├── Drive/
│   ├── DvdDriveDetector.cs      # Detects DVD drives with VIDEO_TS
│   └── VideoTsReader.cs         # Enumerates IFO/VOB files
├── Ifo/
│   ├── IfoParser.cs             # Parses IFO files (title/chapter/PGC/cell data)
│   └── DvdTitle.cs              # DVD data models (title, chapter, audio, cell)
├── Decryption/
│   ├── IDvdDecryptor.cs         # Decryption abstraction
│   ├── LibDvdCssDecryptor.cs    # CSS decryption via libdvdcss P/Invoke
│   └── PassthroughDecryptor.cs  # No-op for unencrypted DVDs
├── Ripping/
│   └── VobRipper.cs             # Reads/decrypts VOB sectors to temp file
└── Encoding/
    ├── EncodingOptions.cs       # Encoding configuration model
    ├── FfmpegBootstrapper.cs    # Auto-downloads FFmpeg if needed
    └── FfmpegEncoder.cs         # VOB → MP4 encoding via FFMpegCore
```

## How It Works

DVDInator processes DVDs through a multi-stage pipeline:

1. **Drive Detection** — Scans all CD-ROM drives for inserted discs with a `VIDEO_TS` folder
2. **IFO Parsing** — Reads the binary DVD-Video IFO files:
   - `VIDEO_TS.IFO` — Title search pointer table (TT_SRPT) for title count and VTS mapping
   - `VTS_xx_0.IFO` — Per-title-set data: Program Chains (PGC) for duration/cell layout, cell address table (C_ADT) for sector ranges, audio/subtitle stream attributes
   - All IFO data is big-endian; durations are BCD-encoded (HH:MM:SS:FF)
3. **VOB Ripping** — Reads VOB (Video Object) files which contain MPEG-PS multiplexed video/audio:
   - Uses cell address data from IFO to map title/chapter boundaries to sector ranges
   - Optionally decrypts CSS-protected sectors via libdvdcss
   - Writes concatenated sectors to a temporary file in `%TEMP%` (cleaned up after encoding)
4. **MP4 Encoding** — FFmpeg converts the MPEG-PS stream to MP4:
   - Video: H.264 (libx264) with configurable CRF quality and preset
   - Audio: AAC at 192 kbps (all audio streams mapped)
   - Output uses `yuv420p` pixel format and `faststart` flag for streaming compatibility

### Temporary files

During ripping, VOB data is written to a temp file (`%TEMP%/dvdinator_title{N}_{guid}.vob`). This file is automatically deleted after successful encoding, or on failure. Ensure you have sufficient temp disk space — a full DVD title can be 4–8 GB.

### FFmpeg location

FFmpeg is searched in this order:
1. Local `ffmpeg/` directory next to the DVDInator executable
2. System PATH
3. Auto-downloaded via Xabe.FFmpeg.Downloader to the local `ffmpeg/` directory

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [System.CommandLine](https://www.nuget.org/packages/System.CommandLine) | 2.x | CLI argument parsing and help generation |
| [FFMpegCore](https://www.nuget.org/packages/FFMpegCore) | 5.4.0 | FFmpeg process wrapper for VOB → MP4 encoding |
| [Xabe.FFmpeg](https://www.nuget.org/packages/Xabe.FFmpeg) | 6.0.2 | FFmpeg configuration and path management |
| [Xabe.FFmpeg.Downloader](https://www.nuget.org/packages/Xabe.FFmpeg.Downloader) | 6.0.2 | Automatic FFmpeg binary downloading |
| [Spectre.Console](https://www.nuget.org/packages/Spectre.Console) | 0.54.0 | Rich terminal UI (tables, progress bars, selection prompts) |

## Legal Disclaimer

CSS decryption functionality is **opt-in** and requires the `--decrypt` flag plus `libdvdcss-2.dll`. The legality of circumventing CSS copy protection varies by jurisdiction. Users are responsible for ensuring their use complies with applicable laws. This software is intended for personal backup of legally owned DVDs.

## License

MIT
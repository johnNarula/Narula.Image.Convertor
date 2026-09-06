# nImgConvertor

Batch image format conversion from the command line. Point it at a folder, name a target
type, get a folder of converted images and a report.

```
nImgConvertor -s .\photos -r -d .\converted -t jpg
```

```
──────────────────────────────────────────
  204 files    00:00:09.1    22.4/sec

  Converted    162
  Copied        40   (already jpg)
  Failed         2

  154 MB → 42.8 MB   (72% smaller)

  Failures
    trip\truncated.png      not a recognisable image
    trip\from-iphone.heic   unsupported format (HEIC/AVIF)
──────────────────────────────────────────
```

## Options

| Flag | Meaning |
|---|---|
| `-s <path>` | Source (required) — a folder, a glob, or a single file. See below |
| `-r` | Recurse into subfolders; the destination mirrors the tree |
| `-d <path>` | Destination folder. Defaults to a `Converted to <type>` folder inside the source folder |
| `-t <type>` | Target: `jpg` `jpeg` `png` `webp` `bmp` `gif` `tiff` `tif` `tga` |
| `-q <1-100>` | Encoder quality, default 85 (JPEG and WebP only) |
| `-o <bool>` | Overwrite existing destination files, default `true` |
| `-trans <bool>` | Preserve transparency, default `true` |
| `-bg <#RRGGBB>` | Matte colour used when flattening, default `#FFFFFF` |
| `-m <bool>` | Preserve EXIF/ICC metadata, default `true` |
| `-p <n>` | Parallel workers, default = CPU count |
| `-e` | Stop on the first failure instead of carrying on |
| `-h` | Help |

Boolean flags take an explicit value: `-o false`, not a bare `-o`.

**Quote any path containing spaces**, or your shell splits it into separate arguments before
the tool ever sees it.

### The three forms of `-s`

| You type | You get |
|---|---|
| `-s "C:\photos"` | every image in that folder |
| `-s "C:\photos\*.jpg"` | only the names matching the pattern (`*` and `?`) |
| `-s "C:\photos\one.jpg"` | just that one file |

A glob combines with `-r` — `-s "C:\photos\*.jpg" -r` matches `*.jpg` throughout the tree.
An explicitly named single file is converted whatever its extension.

Exit codes: `0` all good, `1` one or more files failed, `2` bad arguments or missing
source folder.

## Examples

Convert a folder to JPEG, output beside the originals in `Converted to jpg`:

```bash
nImgConvertor -s "C:\photos" -t jpg
```

Convert a photo library to JPEG, keeping the folder structure:

```bash
nImgConvertor -s "C:\photos" -r -d "C:\converted" -t jpg
```

Only the PNGs, only this folder:

```bash
nImgConvertor -s "C:\photos\*.png" -t webp
```

One file:

```bash
nImgConvertor -s "C:\photos\front-elevation.webp" -t jpg
```

Shrink a website's images to WebP at quality 80:

```bash
nImgConvertor -s .\assets\img -r -d .\assets\webp -t webp -q 80
```

Flatten transparent icons onto black, stripping metadata:

```bash
nImgConvertor -s .\icons -d .\out -t jpg -trans false -bg #000000 -m false
```

Fill gaps without touching what is already there:

```bash
nImgConvertor -s .\photos -r -d .\converted -t jpg -o false
```

## What it does that you might not expect

- **Rotated photos come out upright.** EXIF orientation is applied to the pixels and the
  tag cleared, so the image is correct regardless of what opens it.
- **A file already in the target format is copied, not re-encoded** — so the destination
  is a complete mirror and nothing loses quality for no reason. Asking for `-trans false`,
  `-m false`, a different JPEG `-q`, or converting a photo that needs uprighting all force
  a genuine re-encode instead.
- **Transparent images going to JPEG or BMP are flattened onto `-bg`**, because those
  containers have no alpha channel. Set `-bg` or you get white.
- **Animated GIFs become a single still frame** unless the target is GIF, WebP, or TIFF.
- **Non-image files are ignored**, not reported as failures.
- **`.heic` / `.heif` / `.avif` are listed as failures** rather than skipped silently, so
  you know those files were in the folder (see limitations).
- **Writes are atomic** — each output is encoded to a temp file and renamed, so Ctrl+C
  never leaves a half-written image behind.
- **A destination inside the source folder is not re-scanned as input.**
- **A blocked destination is explained, not just reported.** See below.

## If it cannot create the destination folder

Windows Defender **Controlled Folder Access** protects Documents, Pictures, Desktop and
their OneDrive equivalents, and refuses writes from applications that are not on its allow
list. Windows reports that refusal as `Could not find file '<the folder>'`, which looks
like a bug in the tool but is not — the same executable writes happily to an unprotected
folder.

To allow it:

> Windows Security → Virus & threat protection → Ransomware protection →
> Manage ransomware protection → Allow an app through Controlled folder access →
> Add an allowed app → Browse all apps → pick `nImgConvertor.exe`

Add the exact executable you run. A rebuild to a new location needs adding again.

The tool prints these steps, and the path to add, whenever destination creation fails.

## Limitations

The decoder is [ImageSharp](https://github.com/SixLabors/ImageSharp), which reads JPEG,
PNG, GIF, BMP, TIFF, WebP, TGA, PBM, and QOI. It **cannot** read HEIC/HEIF (iPhone
photos), AVIF, camera RAW, or PSD. Those files are reported as
`unsupported format (HEIC/AVIF)` rather than converted.

Support can be added later without restructuring anything: `IImageConverter` is the seam,
and a Magick.NET-backed implementation would take over the extensions listed in
`ImageSharpConverter.RequiresFallbackDecoder`.

## Building

Requires the .NET 10 SDK.

```bash
dotnet test
```

```bash
dotnet publish src/Narula.Image.Convertor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish/win-x64
```

That produces a standalone `publish/win-x64/nImgConvertor.exe` (~23 MB) that runs on a
machine with no .NET installed. For a much smaller build on a machine that already has
the .NET 10 runtime, drop `--self-contained true -p:PublishTrimmed=true`.

## Licensing note

This project depends on ImageSharp **3.1.12** under the Six Labors Split License, which
grants Apache-2.0 terms for open-source use, non-profits, and for-profit use under 1M USD
annual gross revenue.

ImageSharp 4.x is pinned away from deliberately: it adds a build-time licence-key check
that **fails Release builds** without a key from Six Labors, even for users who qualify
for the free grant. If a key is obtained, upgrading is a package bump plus one call site
(`Color.TryParseHex` gained a `ColorHexFormat` argument in 4.x).

## Design

[docs/superpowers/specs/2026-09-05-nimgconvertor-design.md](docs/superpowers/specs/2026-09-05-nimgconvertor-design.md)

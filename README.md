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

## The window

Running `img2img` with no arguments opens `img2imgUI`, the desktop app. It is also fine to
run `img2imgUI` directly.

Drop a folder or an image onto it, choose a format, press Convert. That drop zone is the
point of the app: it is what removes the need to type quoted paths, and it is the one thing
a browser-based UI could not do — browsers withhold file paths by design.

The window and the command line are the same program. The window builds an argument list and
hands it to the same parser, so every default, rule and quirk applies identically to both,
and neither can grow behaviour the other lacks.

## Options

| Flag | Meaning |
|---|---|
| `-s <path>` | Source (required) — a folder, a glob, or a single file. See below |
| `-r` | Recurse into subfolders; the destination mirrors the tree |
| `-d <path>` | Destination folder. Defaults to a `Converted to <type>` folder inside the source folder |
| `-t <type>` | Target — `jpg` `png` `webp` `avif` `tiff` `bmp` `gif` `ico` `jxl` `pdf` and ~190 more (`-formats`) |
| `-q <1-100>` | Encoder quality, default 100 (formats that record one) |
| `-iconsize <n>` | Which size to take from a multi-size `.ico`, and which to write: 16, 32, 48, 64, 128, 256 |
| `-o <bool>` | Overwrite existing destination files, default `true` |
| `-trans <bool>` | Preserve transparency, default `true` |
| `-bg <colour>` | Matte used when flattening — `#RRGGBB` or a name like `white`, default `#FFFFFF` |
| `-m <bool>` | Preserve EXIF/ICC metadata, default `true` |
| `-p <n>` | Parallel workers, default = CPU count |
| `-e` | Stop on the first failure instead of carrying on |
| `-formats` | List every format that can be read and written |
| `-h` | Help |

Running with no options at all opens the window instead.

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
- **Transparency is flattened onto `-bg` when the target cannot carry alpha.** Which
  formats those are is determined at runtime by encoding a tiny transparent image and
  checking whether it survived, so it stays correct across all ~190 targets.
- **Animation is kept when the target can hold it** (GIF, WebP, TIFF, AVIF and others) and
  collapses to the first frame when it cannot. This is read from ImageMagick per format,
  not from a list baked into the tool.
- **Non-image files are ignored**, not reported as failures.
- **`.heic` / `.heif` / `.avif` are listed as failures** rather than skipped silently, so
  you know those files were in the folder (see limitations).
- **Writes are atomic** — each output is encoded to a temp file and renamed, so Ctrl+C
  never leaves a half-written image behind.
- **A destination inside the source folder is not re-scanned as input.**
- **Formats with a size cap get a scaled copy, not a failure.** ICO cannot hold anything
  larger than 256x256, so a photo is scaled to fit that box with its aspect ratio intact,
  and the report says how many were resized. Nothing else is ever resized.
- **A blocked destination is explained, not just reported.** See below.

## Changing the defaults

Every default lives in `ToolDefaults` and can be overridden without rebuilding, by putting a
`settings.json` beside the executable. Mention only what you want to change:

```json
{
  "quality": 90,
  "background": "black",
  "destinationFolderFormat": "{0} versions"
}
```

| Setting | Default | Meaning |
|---|---|---|
| `quality` | 100 | Encoder quality, 1-100 |
| `overwrite` | true | Replace existing destination files |
| `preserveTransparency` | true | Keep alpha where the target supports it |
| `preserveMetadata` | true | Keep EXIF/ICC/IPTC/XMP |
| `background` | `#FFFFFF` | Matte when flattening, and icon padding when not transparent |
| `parallelism` | 0 | Workers; 0 means one per processor |
| `destinationFolderFormat` | `Converted to {0}` | Folder made when `-d` is omitted; `{0}` is the target type |
| `iconSizes` | 16, 32, 48, 64, 128, 256 | Sizes written into an `.ico`, and what `-iconsize` accepts |

Command-line flags always win over the file. An unusable value is reported and ignored rather
than stopping the run, and `-h` reports whatever defaults are actually in force.

Comments and trailing commas are allowed, so the file can be hand-edited.

## Icons

An `.ico` holds the same picture at several sizes, so it needs saying which one is meant.

**Reading one**, the largest is used. Reading an icon as a plain image otherwise takes
whichever size the file lists first — usually 16x16, which is a surprising answer to
"convert this icon to a png". `-iconsize 32` picks a specific one, and asking for a size the
file does not contain fails with a list of what it does hold.

**Writing one**, every conventional size the source can supply is produced in a single
`.ico` — 16, 32, 48, 64, 128 and 256. Sizes larger than the source are skipped rather than
upscaled, and `-iconsize 48` narrows the output to that one size.

Entries are square, as icons should be. The picture is fitted inside the box at its own
aspect ratio and the remainder is padded transparent — nothing is cropped away. With
`-trans false` the padding takes the `-bg` colour instead. Icons are capped at 256 because
an ICO directory entry stores each dimension in a single byte.

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

### Running the window on Linux

The engine needs nothing beyond the binary, but `img2imgUI` needs the X11 client libraries.
A desktop distribution has them; a server or WSL install usually does not:

```bash
sudo apt install libice6 libsm6
```

Verified on WSL2 Ubuntu 24.04: the command line converts with no extra packages at all, and
the window needs exactly those two.

## Formats

The decoder is [Magick.NET](https://github.com/dlemstra/Magick.NET) (ImageMagick), which
reads **261 formats** and writes **197**. That includes HEIC/HEIF (iPhone photos), AVIF,
camera RAW (CR2, CR3, NEF, ARW, DNG and friends), PSD, SVG and JPEG XL.

```bash
nImgConvertor -formats
```

`-t` accepts anything that can be written, so the target list is not a fixed menu.
HEIC is a notable read-only case: it decodes, but nothing here can encode it.

A **folder scan** picks up a deliberate list of picture extensions rather than everything
ImageMagick can decode — it also reads `txt`, `html`, `json` and `pdf`, and those should
not be swept up by "convert this folder". Naming a file outright or globbing an extension
counts as explicit intent and bypasses that list:

```bash
nImgConvertor -s "C:\scans\*.pdf" -t png
```

### Limitations

Writing HEIC is not supported. Vector sources (SVG, AI, EPS) rasterise at their natural
size, and there is no general resize flag — the only scaling the tool does is fitting an
image into a target that has a hard dimension cap (ICO and CUR, at 256x256).

## Building

Requires the .NET 10 SDK.

```bash
dotnet test
```

Publishing needs no flags beyond the platform — framework-dependent and single-file are the
project defaults, so each publish drops exactly one executable:

```bash
dotnet publish src/Narula.Image.Convertor.Cli -c Release -r win-x64 -o publish/v2
```

```bash
dotnet publish src/Narula.Image.Convertor.UI -c Release -r win-x64 -o publish/v2
```

Into the same folder that gives two files — `img2img.exe` at 26 MB and `img2imgUI.exe` at
55 MB — and lets the command line find the window. Both need the .NET 10 runtime, which is
why they are this small; add `-p:SelfContained=true` for a machine without it, at roughly
four times the size.

Swap the platform for elsewhere: `-r linux-x64`, `-r linux-musl-x64` for Alpine, `-r osx-arm64`
or `-r osx-x64`. A binary built on Windows arrives without the execute bit, so `chmod +x` it.

The solution is three projects: `Narula.Image.Convertor` is the engine library,
`.Cli` produces `img2img.exe`, and `.UI` produces `img2imgUI.exe`. Both executables call the
same engine in-process, so there is one implementation of the conversion rules.

Both produce exactly one `nImgConvertor.exe`. Without `-p:PublishSingleFile=true` you get
around 190 loose files instead. `-p:DebugType=none` drops the `.pdb`.

Measured notes: trimming is unsafe with Magick.NET's native library, and ReadyToRun changed
startup by one millisecond while costing 12 MB, so both are off. The first run extracts the
native library to a temp folder and is slower than later ones.

The `bin/` and `obj/` folders hold hundreds of intermediate build files. They are gitignored
and nothing runs from them — delete them freely.

## Licensing note

v2 depends on **Magick.NET-Q8-AnyCPU** under the Apache-2.0 ImageMagick licence. No
licence key, no revenue threshold.

v1 used ImageSharp 3.1.12 under the Six Labors Split License and was pinned there because
ImageSharp 4.x adds a build-time licence-key check that fails Release builds without a key.
Moving to Magick.NET removed that constraint entirely.

## Versions

| What | Where | Notes |
|---|---|---|
| **v1.0.0 — ImageSharp** | tag `v1.0.0`, branch `v1.0-imagesharp` | 23 MB exe, 9 formats, no HEIC/AVIF/RAW |
| v2.1.0 — window + icons + settings | tag `v2.1.0`, `master` | `img2img.exe` 26 MB + `img2imgUI.exe` 55 MB |
| v2.0.0 — Magick.NET | tag `v2.0.0` | 261 read / 197 write, incl. HEIC/AVIF/RAW/PSD |

Measured on 204 mixed files (154 MB) converting to JPEG: v1 took 8.5 s, v2 took 13.2 s.
v2 is about 1.5x slower and 4x larger; it reads formats v1 cannot open at all.

### Going back to v1.0

Everything below leaves v1.0 exactly as it shipped; the tag is immutable and the branch is
never force-pushed.

Get the v1.0 source:

```bash
git checkout v1.0.0
```

Or work on it with somewhere to commit:

```bash
git checkout v1.0-imagesharp
```

Rebuild the v1.0 executable from either of those:

```bash
dotnet publish src/Narula.Image.Convertor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish/win-x64
```

Return to the latest work:

```bash
git checkout master
```

## Design

[docs/superpowers/specs/2026-09-05-nimgconvertor-design.md](docs/superpowers/specs/2026-09-05-nimgconvertor-design.md)

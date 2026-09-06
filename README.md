# img2img

Batch image format conversion, from a window or from the command line. Point it at a folder,
name a target type, get a folder of converted images and a report.

```
img2img -s .\photos -r -d .\converted -t jpg
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

## Installing

`publish/installer/img2img-setup-<version>.exe` installs all three programs, adds them to your
PATH, and puts "Convert images here..." on the folder right-click menu. It installs for you
alone by default, so it needs no administrator, and offers an all-users install to anyone who
wants one. It checks for the .NET 10 runtime and fetches it from Microsoft only if missing.

It is not signed, so SmartScreen will call the publisher unknown: *More info*, then
*Run anyway*. See `src/Narula.Image.Convertor.Setup/README.md` for how it is built and exactly
what it touches.

Copying the three executables into a folder yourself works just as well; nothing depends on
being installed.

## The window

Running `img2img` with no arguments opens `img2imgUI`, the desktop app. It is also fine to
run `img2imgUI` directly, and `img2img -ui` opens it deliberately.

`-ui` carries the other flags over, so `img2img -ui -s "C:\photos" -t webp -r` opens the
window with the source, target and recursion already set. Anything unusable is dropped
without complaint rather than refusing to start — the window is in front of you, and every
value is there to be corrected. The Explorer right-click entry is the same mechanism.

**About** in the top corner reports the version, the ImageMagick build underneath, and how
many formats this build can actually read and write, with a button to copy the lot.

Drop a folder or an image onto it, choose a format, press Convert. That drop zone is the
point of the app: it is what removes the need to type quoted paths, and it is the one thing
a browser-based UI could not do — browsers withhold file paths by design.

Both pickers filter as you type and keep a drop-down button, so they behave like the lists they
resemble. The button always shows the whole list, not what is left after filtering on whatever
is already in the box, which is what makes it a drop-down rather than a second search. While a
conversion runs, everything that sets it up is disabled and Cancel takes over; cancelling keeps
whatever had already been converted.

The destination shows its full path as soon as a source is chosen, before any button is pressed,
and says whether that folder already exists or will be created.

The format picker takes every one of the 197 writable formats and filters as you type, showing
ImageMagick's own description of whatever is selected. The source has an "only these files"
filter, defaulting to all supported image types, which narrows a folder scan to one extension.

When an icon is on either end of the conversion, a size control appears and says which
direction it means: *Size to take* and *Largest* when reading an icon, *Icon sizes* and
*All sizes* when writing one. Narrowing the filter to `.ico` counts as reading icons too.

The window and the command line are the same program. The window builds an argument list and
hands it to the same parser, so every default, rule and quirk applies identically to both,
and neither can grow behaviour the other lacks.

## For AI agents

`img2imgMcp.exe` is an MCP server, installed alongside the other two. It gives an agent the
same engine the window and the command line use:

| Tool | What it does |
|---|---|
| `convert_images` | Convert a folder, a file or a pattern to another format |
| `list_formats` | Every format this build can write, and optionally the read-only ones |
| `describe_format` | What one format supports: transparency, frames, size limits |
| `inspect_image` | The real format, size, transparency and frame count of a file |

Claude Code takes one line:

```bash
claude mcp add img2img -- "C:\Program Files\img2img\img2imgMcp.exe"
```

(that is the all-users path; a per-user install puts it in
`%LOCALAPPDATA%\Programs\img2img` instead)

Any other client wants the JSON, which the server will print for you with the correct path
already filled in:

```bash
img2imgMcp --print-config
```

Merge that into the client's MCP configuration; it is one entry under `mcpServers`. **About**
in the window shows the same command and copies the JSON to the clipboard, so none of this has
to be remembered.

Failures come back described rather than thrown: a bad target names the target, a missing
source names the path, and a run that partly failed lists each file with its reason. The
server only ever writes into the destination folder it reports.

Running `img2imgMcp` by hand does nothing visible — it talks over stdin and stdout, and is
meant to be launched by the agent. `img2imgMcp -h` prints the setup instructions instead.

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
| `-ui` | Open the window instead, filled in from the other flags given here |
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
img2img -s "C:\photos" -t jpg
```

Convert a photo library to JPEG, keeping the folder structure:

```bash
img2img -s "C:\photos" -r -d "C:\converted" -t jpg
```

Only the PNGs, only this folder:

```bash
img2img -s "C:\photos\*.png" -t webp
```

One file:

```bash
img2img -s "C:\photos\front-elevation.webp" -t jpg
```

Shrink a website's images to WebP at quality 80:

```bash
img2img -s .\assets\img -r -d .\assets\webp -t webp -q 80
```

Flatten transparent icons onto black, stripping metadata:

```bash
img2img -s .\icons -d .\out -t jpg -trans false -bg #000000 -m false
```

Fill gaps without touching what is already there:

```bash
img2img -s .\photos -r -d .\converted -t jpg -o false
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
  not from a list baked into the tool. An `.ico` source is the exception: its frames are
  alternate *sizes*, not animation, so one is always chosen — see below.
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
`settings.json` in either of two places. The first one found wins:

| Where | For |
|---|---|
| `%AppData%\9thAct\img2img\settings.json` | Your own preferences. Survives reinstalling, and works when the program lives somewhere you cannot write to |
| Beside the executable | A copied or portable tool carrying its settings with it. This is the one the installer lays down, and it never overwrites an existing file |

Mention only what you want to change:

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

**Reading one**, the largest is used, whatever the target. Reading an icon as a plain image
otherwise takes whichever size the file lists first — usually 16x16, which is a surprising
answer to "convert this icon to a png". `-iconsize 32` picks a specific one, and asking for a
size the file does not contain fails with a list of what it does hold.

That holds even for targets that can carry several frames. Until 1.0.26.0906 it did not:
converting a seven-size icon to TIFF or WebP wrote all seven as *pages*, and the file then
reported itself as 16x16 — the first page — so anything opening it saw a thumbnail. Real
animation is unaffected; an animated GIF still keeps every frame.

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
> Add an allowed app → Browse all apps → pick `img2img.exe`

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
img2img -formats
```

`-t` accepts anything that can be written, so the target list is not a fixed menu.
HEIC is a notable read-only case: it decodes, but nothing here can encode it.

A **folder scan** picks up a deliberate list of picture extensions rather than everything
ImageMagick can decode — it also reads `txt`, `html`, `json` and `pdf`, and those should
not be swept up by "convert this folder". Naming a file outright or globbing an extension
counts as explicit intent and bypasses that list:

```bash
img2img -s "C:\scans\*.pdf" -t png
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

The solution is five projects: `Narula.Image.Convertor` is the engine library, `.Cli`
produces `img2img.exe`, `.UI` produces `img2imgUI.exe`, `.Mcp` produces `img2imgMcp.exe`, and
`.Setup` builds the installer. All three executables call the same engine in-process, so there
is one implementation of the conversion rules and no front end can drift from another.

The installer is built on purpose and is not part of a normal build, so this repository still
builds and tests on a machine without Inno Setup:

```bash
dotnet build src/Narula.Image.Convertor.Setup -t:Installer
```

That needs Inno Setup 6.3+ (`winget install JRSoftware.InnoSetup`). It publishes both
executables into one staging folder and compiles `publish/installer/img2img-setup-<version>.exe`.

Both produce exactly one `img2img.exe`. Without `-p:PublishSingleFile=true` you get
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

Versions are `major.minor.YY.MMDD`, dated when the build was made, and shared by every project
from `Directory.Build.props`. `1.0.26.0906` is the first release of the installed product,
built on 6 September 2026. Reproduce an older number with
`dotnet build -p:BuildYear=26 -p:BuildDay=0906`.

The .NET assembly version cannot hold a leading zero, so file properties read `1.0.26.906`
while the About box and `-h` show the padded `1.0.26.0906`.

| What | Where | Notes |
|---|---|---|
| **v1.0.0 — ImageSharp** | tag `v1.0.0`, branch `v1.0-imagesharp` | 23 MB exe, 9 formats, no HEIC/AVIF/RAW |
| 1.0.YY.MMDD — installer, About, `-ui` | `master` | Version scheme restarted here; the old 2.x numbering is retired |
| v2.1.0 — window + icons + settings | tag `v2.1.0` | `img2img.exe` 26 MB + `img2imgUI.exe` 55 MB |
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

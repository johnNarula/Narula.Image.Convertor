# nImgConvertor — Design

- **Date:** 2026-09-05
- **Project:** Narula.Image.Convertor
- **Namespace:** Narula.Image.Convertor
- **Output:** `nImgConvertor.exe`
- **Target framework:** `net10.0` (C# 14)
- **Status:** v1 shipped (tag `v1.0.0`). This document describes v2 on branch `v2-magick`.
- **v2 change:** ImageSharp replaced wholesale by Magick.NET

## Purpose

A single-purpose CLI that batch-converts image files from one format to another. It
optimises for two things: being obvious to use, and being fast on folders containing
thousands of files.

Non-goals: resizing, cropping, watermarking, colour-space conversion, a GUI, a daemon
mode. If those are wanted later they are separate features, not hidden flags.

## CLI surface

```
nImgConvertor -s <source> -t <type> [options]

  -s <path>      Source (required): a folder, a glob, or a single file
  -r             Recurse into subfolders; destination mirrors the tree
  -d <path>      Destination folder; defaults to "Converted to <type>" inside the source folder
  -t <type>      Target type: jpg jpeg png webp bmp gif tiff tif tga
  -q <1-100>     Encoder quality (default 100; formats that record one)
  -o <bool>      Overwrite existing destination files (default true)
  -trans <bool>  Preserve transparency (default true)
  -bg <#RRGGBB>  Matte colour used when flattening (default #FFFFFF)
  -m <bool>      Preserve EXIF/ICC metadata (default true)
  -p <n>         Parallel workers (default = Environment.ProcessorCount)
  -e             Stop on first failure
  -h             Help
```

Boolean flags take an explicit `true`/`false` value (`-o false`). This is deliberate and
consistent across `-o`, `-trans`, and `-m`.

**The three forms of `-s`.** A path that names an existing folder means every image in it.
A path whose last segment contains `*` or `?` splits into a folder plus a glob, and only
matching names are taken; the glob combines with `-r` to match throughout the tree. A path
that names an existing file means that file alone, converted whatever its extension — the
user pointed at exactly one thing. Anything else is treated as a folder so the run can
report it as missing.

Globs are matched with `FileSystemName.MatchesSimpleExpression` rather than handed to
`Directory.EnumerateFiles`, because Win32 pattern semantics have surprises (`*.tif`
matching `.tiff`) that the documented API does not.

**`-d` is optional.** Omitted, output goes to a folder named `Converted to <type>` inside
the source folder — beside the originals, obviously named, and (being inside the source)
already covered by the rule that a run never reads its own output. Nothing is ever written
next to an original under its own name unless `-d` says so explicitly.

`-t jpg` and `-t jpeg` both select the JPEG encoder, as do `-t tiff` and `-t tif` for
TIFF; the output file receives exactly the extension the user typed. Running with no
arguments prints the help text and exits 0.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Every file converted, copied, or intentionally skipped |
| 1 | One or more files failed |
| 2 | Invalid arguments, or the source folder or file does not exist |

## Behavioural rules

**Source selection.** Only files whose extension is in the known image set enter the work
list: `.jpg .jpeg .jpe .jfif .png .webp .bmp .gif .tif .tiff .tga .pbm .qoi .heic .heif
.avif`. Everything else (`.txt`, `Thumbs.db`, `.db`) is invisible to the tool and is not
counted as a failure.

`.heic`, `.heif`, and `.avif` deliberately enter the work list even though the current
decoder cannot read them. They are rejected up front with the reason
`unsupported format (HEIC/AVIF)`, so the user learns the files were there rather than
silently losing them.

**Destination layout.** With `-r`, the destination mirrors the source directory
structure. A source file at `<src>\a\b\c.png` with `-t jpg` is written to
`<dst>\a\b\c.jpg`. Directories are created on demand. A destination folder that sits
inside the source tree is excluded from the scan, so a run never consumes its own output.

**Destination collisions.** Two sources can map onto one destination — `logo.png` and
`logo.jpg` both become `logo.jpg`. Letting two workers race for the same file handle
would produce a nondeterministic failure, so the scanner resolves this instead: the
source already in the target format wins (converting it is a no-op anyway), ties break
alphabetically, and every loser is reported as a failure reading
`destination collides with <path>`.

**Overwrite.** With `-o false`, an existing destination file is left untouched and the
result is recorded as `Skipped (exists)`. With the default `-o true` it is overwritten.

**Same-format pass-through.** When the source is already in the target format and the run
asks for nothing that would change the file, the original bytes are copied across and the
result is recorded as `Copied`. This keeps the destination a complete mirror. The
shortcut is disqualified by any of:

- `-trans false` or `-m false` — the user asked for a transformation, and copying would
  quietly ignore it
- an EXIF orientation tag above 1 — the photo needs uprighting
- JPEG only: a quantization-table quality estimate that differs from `-q`

Formats other than JPEG expose no readable quality, so `-q` cannot force a re-encode of
an already-correct PNG, WebP, BMP, or TIFF.

**Transparency.** With the default `-trans true`, alpha is preserved whenever the target
container supports it (PNG, WebP, GIF, TIFF, TGA). Targets without an alpha channel
(JPEG, BMP) are flattened onto `-bg` regardless of the flag — the flag cannot invent a
capability the container lacks. With `-trans false`, alpha is flattened onto `-bg` in
every case.

**Metadata.** Default `-m true` preserves EXIF, ICC, IPTC, and XMP profiles when both the
source and the target format support them. `-m false` strips them. EXIF orientation is
always applied to the pixel data and then normalised, so rotated photos do not come out
sideways.

**Multi-frame inputs.** Animated GIFs and multi-page TIFFs contribute their first frame
only unless the target is GIF, WebP, or TIFF. PNG is treated as single-frame on purpose:
converting an animated GIF to PNG should produce a still image, not an APNG.

**Protected folders.** Windows Defender Controlled Folder Access guards Documents,
Pictures, Desktop and their OneDrive equivalents, refusing writes from applications not on
its allow list. It reports the refusal as ERROR_FILE_NOT_FOUND, which .NET surfaces as a
`FileNotFoundException` naming the folder being created — so a policy decision reads like a
bug in the caller. Confirmed against Defender event ID 1123, which logs the blocked path.

There is nothing the tool can do about this beyond saying so clearly, which
`Application` does: the destination-creation failure prints the underlying error and then
names the exact Windows Security screen and the executable path to add. Directory creation
still retries a few times through `Directories.EnsureExists`, but only for genuine
short-lived races — a blocked write does not become unblocked by waiting.

A published, unsigned executable is subject to this; the same code run from
`bin\Debug` may not be, which makes the failure look like a build-configuration problem
rather than a security policy.

**Atomic writes.** Each conversion encodes to a sibling `.nimgtmp` file and renames it
into place, so an interrupted run never leaves a half-written image where a valid one is
expected. The temp file is removed on any failure.

**Failure handling.** By default a failed file is recorded and the run continues. With
`-e`, the first failure cancels the run: in-flight work is allowed to finish, the report
is printed for what completed, and the process exits 1. Ctrl+C behaves the same way.

Failure reasons are mapped to plain language: `unsupported format (HEIC/AVIF)`,
`not a recognisable image` (nothing in the bytes matches a known format),
`corrupt or unreadable image`, `access denied`, or the underlying I/O message.

## Architecture

One project, one NuGet dependency (`SixLabors.ImageSharp` 3.1.12).

| File | Responsibility |
|---|---|
| `Program.cs` | Wires up Ctrl+C and hands off to `Application` |
| `Application.cs` | The whole run, from arguments to exit code |
| `CliOptions.cs` | Options record, hand-rolled parser, help text |
| `WorkItem.cs` | `(SourcePath, DestinationPath, RelativePath)` |
| `FileScanner.cs` | Enumeration, extension filtering, mirrored paths, collision resolution |
| `IImageConverter.cs` | The seam: `ConvertAsync(WorkItem, CliOptions, CancellationToken)` |
| `ImageSharpConverter.cs` | The only implementation today |
| `ConversionResult.cs` | `Outcome` enum + reason + bytes in/out |
| `ConversionEngine.cs` | `Parallel.ForEachAsync`, cancellation, result collection |
| `Directories.cs` | Directory creation with a short retry for genuine races |
| `ProgressReporter.cs` | In-place counter with redirected-output fallback |
| `Report.cs` | Final summary rendering |

`Application` is separate from `Program` so exit-code behaviour can be tested without
launching a process.

A bare word where a flag was expected reports that the path may need quoting, since a
shell splitting an unquoted path with spaces is the overwhelmingly common cause.

Argument parsing is hand-rolled. Twelve flags do not justify a dependency, and
`System.CommandLine` would add startup cost to a tool whose whole point is speed.

### Why ImageSharp 3.1 and not 4.x

ImageSharp 4.x adds a build-time licence check: without a `sixlabors.lic` file or a
`SixLaborsLicenseKey` property it emits warnings in Debug and **fails the build outright
in Release**, even for users who qualify for the free Apache-2.0 grant. Obtaining the key
requires registering with Six Labors.

3.1.12 carries the same Six Labors Split License — Apache-2.0 for open-source use, for
non-profits, and for for-profit use under 1M USD annual gross revenue — with no key gate.
The API differences that mattered were trivial (`Color.TryParseHex` lost its
`ColorHexFormat` argument). If a licence key is ever obtained, moving to 4.x is a
one-line package bump plus that one call site.

### The decoder seam

`IImageConverter` exists so that a `MagickNetConverter` can be added later to handle the
formats ImageSharp rejects (HEIC, HEIF, AVIF, RAW, PSD), without restructuring the
scanner, the engine, or the reporting. The intended future shape is a composite that
tries ImageSharp first and falls back for the extensions listed in
`ImageSharpConverter.RequiresFallbackDecoder`. Nothing is built for that today beyond the
interface — the abstraction is the whole investment.

This choice was made knowingly: ImageSharp is pure managed, ships as a small
self-contained executable with no native payload, and is fast; the cost is that iPhone
photos (`.heic`) and AVIF cannot be read until the fallback is added.

### Data flow

Scanning runs to completion before any conversion starts. This is what makes the
`(x of y)` counter possible — `y` must be known up front. The work list is sorted by
relative path so runs are reproducible.

The list is then handed to `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism = -p`. Each worker is fully independent: it reads one file,
writes one file, and returns a `ConversionResult` into a `ConcurrentBag`. The only shared
mutable state is an `Interlocked` progress counter and the cancellation token.

### Progress output

A single console line is redrawn in place:

```
Processing IMG_4821.png (247 of 500)...
```

Redraws are throttled to roughly 20 per second — at several thousand files, unthrottled
console writes become the bottleneck rather than the encoder. When
`Console.IsOutputRedirected` is true, the reporter switches automatically to plain
one-line-per-file output so that piping and log capture still work.

Because workers complete out of order, the counter reflects completion count, not source
order. The filename shown is whichever file most recently finished.

### Final report

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

`Converted` is always shown; `Copied`, `Skipped`, and `Failed` appear only when non-zero.
The size line covers files that actually landed in the destination. Failure paths are
shown relative to the source root, capped at 10 entries with a "... N more" line so a
catastrophic run does not bury the summary. If the run was cut short by `-e` or Ctrl+C,
the header reads "247 of 512 files".

## Performance

- `Parallel.ForEachAsync` at `Environment.ProcessorCount`
- Stream-based decode and encode; no `File.ReadAllBytes`
- ImageSharp's default pooled memory allocator, left alone
- Progress redraws throttled
- Published ReadyToRun to remove JIT cost from a short-lived process

Measured: 204 mixed-format files totalling 154 MB (mostly 1280×800 BMP and PNG) converted
to JPEG in 9.1 s — 22.4 files/sec — on the development machine. The steady state is
CPU-bound inside the encoder, which is the correct place for the time to go.

## Testing

An xUnit project alongside the main one, 74 tests. Fixture images are generated
programmatically at test time — no binary assets in the repository.

Coverage:

- Argument parsing: full command line, defaults, aliases, and every rejection path
- The three `-s` forms, and the default `-d` for each of them
- Glob selection, glob with `-r`, and a glob matching nothing
- Directory creation: nested, idempotent, contended by 64 workers, blocked by a file,
  and recreated immediately after deletion
- The blocked-destination message, including the Controlled Folder Access guidance
- Extension filtering, recursion, mirrored paths, self-output exclusion, sort order
- Destination collision resolution
- Overwrite policy in both states
- Same-format copy-through, and each of the four disqualifiers
- Alpha flattening: forced by container, and forced by `-trans false`
- Alpha survival on a container that supports it
- Metadata stripping and EXIF uprighting
- Animated GIF reduced to one frame
- Corrupt files, unsupported formats, and no residue left behind
- Exit codes for clean, partial-failure, bad-argument, missing-source, and help runs
- Converting a folder in place over itself

The test project's namespace is `ImageConvertor.Tests` rather than
`Narula.Image.Convertor.Tests`, because the `Narula.Image` namespace shadows
`SixLabors.ImageSharp.Image` and makes `Image<Rgba32>` unresolvable. The production code
works around the same clash with a `using ISImage = SixLabors.ImageSharp.Image;` alias.

## Open items

None.

---

# v2 — Magick.NET

## Why replace rather than fall back

v1's design left `IImageConverter` as a seam so Magick.NET could be added *behind*
ImageSharp for formats it could not read. Measuring the actual cost changed the answer:
a published build carrying Magick.NET is around 100 MB whether or not ImageSharp is also
present, so the hybrid bought only speed on common formats, at the price of two decoders
whose metadata, quality and transparency semantics would have to be kept in agreement.
v1's pass-through rule leaned on ImageSharp's quantization-table quality estimate, which
Magick reports differently — exactly the sort of divergence that produces quiet bugs.

Replacing outright also retired the ImageSharp licence pin. Magick.NET is Apache-2.0 with
no key check, so there is no longer a version the project cannot upgrade to.

## What the numbers were

| | v1 (ImageSharp 3.1.12) | v2 (Magick.NET 14.17.1) |
|---|---|---|
| Published exe | 23 MB, trimmed | 100 MB, not trimmable |
| Startup (`-h`, mean of 5) | 58 ms | 83 ms |
| 204 files / 154 MB → JPEG | 8.5 s | 13.2 s |
| Output for that run | 42.8 MB | 36.6 MB |
| Formats read / written | 9 / 9 | 261 / 197 |

v2 is roughly 1.5x slower and 4x larger. It produced smaller JPEGs at the same nominal
`-q 85`, because the two encoders map that number onto different settings; the number is
not comparable across the versions.

ReadyToRun was measured at 84 ms startup against 83 ms without, for 12 MB — single-file
native extraction dominates startup, so R2R is off.

## Format rules are asked, not asserted

Opening `-t` to every writable format is only safe if the per-format rules cannot go
stale, so all three come from ImageMagick at runtime:

- **writable** — `MagickFormatInfo.SupportsWriting`, which is what validates `-t`
- **multi-frame** — `MagickFormatInfo.SupportsMultipleFrames`, which decides whether an
  animation is preserved or collapsed to its first frame
- **alpha** — not exposed by the format table, so `ImageFormats.SupportsAlpha` probes it:
  encode a 2×2 transparent image to the format, read it back, report whether the
  transparency survived. About 3 ms, cached per format. A format that refuses the probe is
  assumed to keep alpha, which is the safe direction to be wrong — the matte colour is
  applied anyway if the encoder drops it.

## Source selection stays curated

`-t` is open; the folder scan is not. ImageMagick reads `txt`, `html`, `json` and `pdf`,
and a user running "convert this folder to jpg" does not want their notes swept in. The
scan therefore keeps a deliberate list of picture extensions.

Explicit intent overrides it. Naming a file, or writing a glob with a concrete extension
such as `*.pdf`, sets `SourceExtensionIsExplicit` and bypasses the list entirely.

## Dimension caps and the one place scaling happens

Resizing was a stated non-goal, and remains one everywhere except formats that physically
cannot hold the image. ICO is the case that matters: converting a photo to an icon is a
request that only makes sense at icon size, so refusing it would be refusing the obvious
interpretation. Sources larger than the cap are scaled to fit with their aspect ratio
intact, after uprighting so a sideways photo is measured as it will be stored, and the
report shows a `Resized` count.

The cap is **256, not 512**. ImageMagick's ICO writer accepts up to 512 per dimension and
rejects 513 — measured by bisection. But an ICO directory entry stores each dimension in a
single byte, where 0 means 256, so an entry larger than 256 cannot describe itself: at 512
the directory claims 256x256 while the embedded PNG is really 512x320. Consumers choose an
entry by reading that directory, so the file is malformed however willing the encoder was
to write it. A test parses the directory of a generated ICO and asserts it matches the
payload, so this cannot regress quietly.

## Behaviour changes from v1

- Animation is preserved when the target supports multiple frames, instead of always
  collapsing to the first frame. GIF → WebP keeps all frames; GIF → JPEG keeps one.
- `-bg` accepts ImageMagick colour names (`white`, `chartreuse`) as well as hex.
- `-q` applies to any format that records a quality, not just JPEG and WebP.
- Images bound for a capped format (ICO, CUR) are scaled to fit rather than rejected
- Failure reasons are cleaned up before display: ImageMagick appends the offending path
  and the C source location that raised the error, both of which are noise in a report
  that already shows the path.

Everything else — the three forms of `-s`, the default destination, mirrored trees,
collision resolution, overwrite policy, pass-through, uprighting, atomic writes, exit
codes, the progress line and the report — is unchanged and still covered by its tests.

## Icons carry several sizes

An `.ico` is a container of the same picture at several resolutions, which makes both
directions ambiguous.

**Reading.** `new MagickImage(icon.ico)` returns whichever entry the file lists first, and
files are conventionally ordered smallest first — so converting a seven-size icon to PNG
silently produced a 16x16 image. The largest entry is now selected instead, and
`-iconsize` picks a named one. A requested size the file lacks fails with a list of the
sizes it does hold, rather than falling back to something the user did not ask for.

**Writing.** A single-entry icon is a poor icon. Converting to ICO now emits every
conventional size the source can supply (16, 32, 48, 64, 128, 256), skipping any larger
than the source rather than upscaling into blur. `-iconsize` narrows it to one.

Entries are square. The picture is fitted inside the box at its own aspect ratio and the
remainder padded transparent, so nothing is cropped away and no border colour is invented:
a 3024x4032 photo becomes a 192x256 picture centred in a 256x256 entry with transparent
columns either side. When `-trans false` asks for transparency to be flattened, the padding
takes the matte colour instead, since transparent padding would contradict the request.

`-iconsize` is deliberately restricted to the six conventional sizes. Arbitrary values
would make it a general resize flag, which remains a non-goal.

## Defaults live in one place

Changing a default used to mean finding it in the parser and rebuilding. They now sit in
`ToolDefaults`, and `Defaults.Load` will take them from a `settings.json` beside the
executable, so a preference change needs neither a code edit nor a build.

Precedence is file, then flags: the file moves the starting point, a flag always wins.
`-h` renders from the values actually in force rather than a fixed string, so help cannot
disagree with behaviour.

An unusable value is reported on stderr and that one field falls back to its built-in --
a typo in a preferences file is no reason to refuse to convert images. Same for a file that
will not parse at all. Comments and trailing commas are accepted because the file is meant
to be hand-edited.

### Quality 100 and AVIF

The default quality is 100, which AVIF cannot accept: at exactly 100 it asks AOM for
lossless, and this build refuses that unless chroma delta-q is also disabled, failing with
"Only --enable_chroma_deltaq=0 can be used with --lossless=1". 99 encodes fine and is
visually indistinguishable, so AVIF's quality is capped there rather than the conversion
being lost. Found by the test that converts into every common target, which started failing
the moment the default moved.

## The desktop window

`img2imgUI` is an Avalonia application over the same engine. Avalonia was chosen after
checking rather than by reputation: it builds on .NET 10, it is MIT licensed — this project
has already lost a version to a dependency's licence terms — and it is the only mainstream
option that reaches Linux, which .NET MAUI does not.

A browser UI was considered and rejected on one fact: a browser cannot hand you a file path.
Drag a folder onto a web page and you get file contents; `<input type=file>` reports
`C:akepath\`. The friction this tool exists to remove — typing quoted paths to folders —
is precisely what a browser cannot fix.

### How the two front ends stay identical

The window does not call the engine's internals directly. It fills in a `ConversionRequest`,
turns that into an argument list, and hands it to the same parser the command line uses. Every
default, validation rule and quirk therefore applies to both, and neither can drift.

Underneath, `ConversionRun.ExecuteAsync` holds the orchestration both share — parse, validate,
scan, convert — and returns a `RunOutcome` with nothing printed. The console front end renders
it as a report; the window renders it as a summary and a list of failures.

Progress reaches both through `IProgressSink`: the engine counts, a sink presents. The console
implementation redraws a throttled line; the window updates a progress bar.

### Linux prerequisites

The engine runs on a bare Linux install with nothing added — verified on WSL2 Ubuntu 24.04,
converting real HEIC photos. The window does not: Avalonia's X11 backend needs `libICE.so.6`
and `libSM.so.6`, which a desktop distribution ships but a server or WSL image does not
(`apt install libice6 libsm6`). Everything else Avalonia wants was already present.

This is a packaging fact rather than a defect, but it is the difference between "builds for
Linux" and "runs on Linux", and only launching it on a real one surfaces it.

### Structure

Three projects, because an executable cannot reference another executable:
`Narula.Image.Convertor` is the engine library, `.Cli` is `img2img.exe`, `.UI` is
`img2imgUI.exe`. Running `img2img` with no arguments launches the window found beside it, and
falls back to printing help when it is not there. That decision lives in the executable rather
than the library, so nothing can spawn a process merely by calling the engine.

`MainViewModel` holds the window's behaviour and references no Avalonia type, so it is tested
without a display — including one test that converts real files through the same constructor
the window uses.

## Packaging

| Layout | Files | Size | Needs |
|---|---|---|---|
| Framework-dependent, single file | 1 | 26 MB | .NET 10 runtime |
| Self-contained, single file | 1 | 97 MB | nothing |
| Self-contained, not single file | ~190 | 108 MB | nothing |

The framework-dependent single file is the documented default: the runtime is already
present wherever the tool is built, and 26 MB against 97 MB is worth more than portability
that is not being used. `-p:DebugType=none` removes the `.pdb`, leaving exactly one file.

Trimming is off because Magick.NET's native library is not trim-safe. ReadyToRun is off
because it was measured at 84 ms startup against 83 ms without, for 12 MB — single-file
native extraction dominates startup, so it bought nothing.

## The icon

`tools/GenerateIcon.cs` draws the icon from normalised coordinates with 8x8 supersampling
and assembles the `.ico` container by hand. It carries five designs; the shipped one is
`split` — a single photo divided by a diagonal seam, white on one side and amber on the
other, saying "the same picture, two formats" without needing an arrow. It was chosen
because the 16px rendering is the one that matters, and a split block stays sharp and
distinctive there while arrows and stacked cards dissolve.

Sizes below 32px drop the interior detail. Entries are DIBs except 256, which is PNG —
the convention, and a quarter the size of a raw DIB at that resolution. Each directory
entry is verified to describe its own payload.

The tool uses Magick.NET, the same library as the application, so the repository does not
carry a second imaging dependency just to draw an icon.

## Verification

92 tests, plus these end-to-end checks on real files:

- **Six real iPhone HEIC photos** converted to JPEG: v2 converted all six with correct
  dimensions, orientation and format; v1 failed all six with "unsupported format".
- AVIF, JPEG XL and PSD written from JPEG and then read back and converted onward, so both
  directions of each new format are exercised.
- The 204-file mixed fixture set produced identical outcome counts to v1 (162 converted,
  40 copied, 2 genuinely corrupt files failed).
- 27 real WebP property photos converted to every common target.

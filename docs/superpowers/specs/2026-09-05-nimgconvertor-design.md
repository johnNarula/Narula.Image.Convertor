# nImgConvertor — Design

- **Date:** 2026-09-05
- **Project:** Narula.Image.Convertor
- **Namespace:** Narula.Image.Convertor
- **Output:** `nImgConvertor.exe`
- **Target framework:** `net10.0` (C# 14)
- **Status:** Built and verified. This document describes what was implemented.

## Purpose

A single-purpose CLI that batch-converts image files from one format to another. It
optimises for two things: being obvious to use, and being fast on folders containing
thousands of files.

Non-goals: resizing, cropping, watermarking, colour-space conversion, a GUI, a daemon
mode. If those are wanted later they are separate features, not hidden flags.

## CLI surface

```
nImgConvertor -s <folder> -d <folder> -t <type> [options]

  -s <path>      Source folder (required)
  -r             Recurse into subfolders; destination mirrors the tree
  -d <path>      Destination folder (required, even when identical to source)
  -t <type>      Target type: jpg jpeg png webp bmp gif tiff tif tga
  -q <1-100>     Encoder quality (default 85; applies to JPEG and WebP only)
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

`-d` is always required, even when it points at the source folder. There is no implicit
"write next to the original" behaviour — destructive defaults are not acceptable here.

`-t jpg` and `-t jpeg` both select the JPEG encoder, as do `-t tiff` and `-t tif` for
TIFF; the output file receives exactly the extension the user typed. Running with no
arguments prints the help text and exits 0.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Every file converted, copied, or intentionally skipped |
| 1 | One or more files failed |
| 2 | Invalid arguments, or the source folder does not exist |

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
| `ProgressReporter.cs` | In-place counter with redirected-output fallback |
| `Report.cs` | Final summary rendering |

`Application` is separate from `Program` so exit-code behaviour can be tested without
launching a process.

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

An xUnit project alongside the main one, 55 tests. Fixture images are generated
programmatically at test time — no binary assets in the repository.

Coverage:

- Argument parsing: full command line, defaults, aliases, and every rejection path
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

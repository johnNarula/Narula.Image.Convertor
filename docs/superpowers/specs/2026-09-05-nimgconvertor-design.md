# nImgConvertor — Design

- **Date:** 2026-09-05
- **Project:** Narula.Image.Convertor
- **Namespace:** Narula.Image.Convertor
- **Output:** `nImgConvertor.exe`
- **Target framework:** `net10.0` (C# 14)
- **Status:** Approved

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
  -t <type>      Target type: jpg jpeg png webp bmp gif tiff tga
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

`-t jpg` and `-t jpeg` both select the JPEG encoder; the output file receives exactly the
extension the user typed.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Every file converted, copied, or intentionally skipped |
| 1 | One or more files failed |
| 2 | Invalid arguments, or the source folder does not exist |

## Behavioural rules

**Source selection.** Only files whose extension is in the known image set enter the work
list: `.jpg .jpeg .png .webp .bmp .gif .tif .tiff .tga .pbm .qoi .heic .heif .avif`.
Everything else (`.txt`, `Thumbs.db`, `.db`) is invisible to the tool and is not counted
as a failure.

`.heic`, `.heif`, and `.avif` deliberately enter the work list even though the current
decoder cannot read them. They land in the failure list as "unsupported format" so the
user learns the files were there rather than silently losing them.

**Destination layout.** With `-r`, the destination mirrors the source directory
structure. A source file at `<src>\a\b\c.png` with `-t jpg` is written to
`<dst>\a\b\c.jpg`. Directories are created on demand.

**Overwrite.** With `-o false`, an existing destination file is left untouched and the
result is recorded as `Skipped (exists)`. With the default `-o true` it is overwritten.

**Same-format short-circuit.** When the target extension matches the source extension,
the encode is skipped and the original file is copied to the destination instead,
recorded as `Copied`. This keeps the destination a complete mirror. The exception is
JPEG-to-JPEG, where ImageSharp's quantization-table quality estimate is compared against
`-q`: if they differ, a real re-encode happens. Formats that expose no readable quality
(PNG, WebP, BMP, TIFF) always take the copy path.

**Transparency.** With the default `-trans true`, alpha is preserved whenever the target
container supports it. Targets without an alpha channel (JPEG, BMP) are flattened onto
`-bg` regardless of the flag — the flag cannot invent a capability the container lacks.
With `-trans false`, alpha is flattened onto `-bg` in every case.

**Metadata.** Default `-m true` preserves EXIF and ICC profiles when both the source and
the target format support them. `-m false` strips them. EXIF orientation is always
applied to the pixel data and then normalised, so rotated photos do not come out sideways.

**Multi-frame inputs.** Animated GIFs and multi-page TIFFs contribute their first frame
only when the target is a single-frame format. No `_000`/`_001` expansion.

**Failure handling.** By default a failed file is recorded and the run continues. With
`-e`, the first failure cancels the run: in-flight work is allowed to finish, the report
is printed for what completed, and the process exits 1.

## Architecture

One project, one NuGet dependency (`SixLabors.ImageSharp`).

| File | Responsibility |
|---|---|
| `Program.cs` | Top-level statements: parse, validate, scan, run, report, return exit code |
| `CliOptions.cs` | Options record, hand-rolled parser, help text |
| `WorkItem.cs` | `(SourcePath, DestinationPath, RelativePath)` |
| `FileScanner.cs` | Enumeration, extension filtering, mirrored destination path computation |
| `IImageConverter.cs` | The seam: `Convert(WorkItem, CliOptions, CancellationToken)` returning a `ConversionResult` |
| `ImageSharpConverter.cs` | The only implementation today |
| `ConversionResult.cs` | `Outcome` enum + reason + bytes in/out + elapsed |
| `ConversionEngine.cs` | `Parallel.ForEachAsync`, cancellation, result collection |
| `ProgressReporter.cs` | In-place counter with redirected-output fallback |
| `Report.cs` | Final summary rendering |

Argument parsing is hand-rolled. Twelve flags do not justify a dependency, and
`System.CommandLine` would add startup cost to a tool whose whole point is speed.

### The decoder seam

`IImageConverter` exists so that a `MagickNetConverter` can be added later to handle the
formats ImageSharp rejects (HEIC, HEIF, AVIF, RAW, PSD), without restructuring the
application. The intended future shape is a composite that tries ImageSharp first and
falls back on `UnknownImageFormatException`. Nothing is built for that today beyond the
interface — the abstraction is the whole investment.

This choice was made knowingly: ImageSharp is pure managed, ships as a small
self-contained executable with no native payload, and is fast; the cost is that iPhone
photos (`.heic`) and AVIF cannot be read until the fallback is added.

### Data flow

Scanning runs to completion before any conversion starts. This is what makes the
`(x of y)` counter possible — `y` must be known up front.

The work list is then handed to `Parallel.ForEachAsync` with
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
  512 files    00:00:11.4    45/sec

  Converted    487
  Copied         9   (already jpg)
  Skipped        4   (exists, -o false)
  Failed        12

  1.42 GB → 384 MB   (73% smaller)

  Failures
    photos\raw\IMG_0031.heic   unsupported format
    photos\bad.png             corrupt PNG header
    ... 10 more
──────────────────────────────────────────
```

Failure paths are shown relative to the source root. The list is capped at 10 entries
with a "... N more" line; the cap exists so a catastrophic run does not bury the summary.

## Performance

- `Parallel.ForEachAsync` at `Environment.ProcessorCount`
- Stream-based decode and encode; no `File.ReadAllBytes`
- ImageSharp's default pooled memory allocator, left alone
- Progress redraws throttled
- Published ReadyToRun to remove JIT cost from a short-lived process

The expected steady state is CPU-bound inside the encoder, which is the correct place for
the time to go.

## Testing

An xUnit project alongside the main one. Fixture images are generated programmatically at
test time — no binary assets in the repository.

Coverage:

- Extension filtering: non-image files excluded, `.heic` included and failed
- Mirrored destination path computation, with and without `-r`
- Overwrite policy in both states
- Same-format copy path, and the JPEG quality-differs re-encode path
- Alpha flattening: forced by container, and forced by `-trans false`
- Quality value reaching the encoder
- Argument parsing: valid, missing required, malformed boolean, out-of-range quality
- Exit code selection for clean, partial-failure, and bad-argument runs

## Open items

None.

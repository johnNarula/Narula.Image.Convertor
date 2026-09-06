using System.ComponentModel;

using Narula.Image.Convertor;

namespace Narula.Image.Convertor.Mcp;

/// <summary>
/// What the tools hand back. These are separate from the engine's own types on purpose: they
/// are a protocol surface, so they say plain things in plain words and can stay stable while
/// the engine's internals move.
/// </summary>
public sealed record ConversionReport(
    [property: Description("True when the run finished. False means it never started; see message.")]
    bool Ran,
    [property: Description("What went wrong when Ran is false, or a note about the run.")]
    string? Message,
    [property: Description("Folder the output was written to.")]
    string? Destination,
    [property: Description("Files re-encoded into the target format.")]
    int Converted,
    [property: Description("Files already in the target format, copied across untouched.")]
    int Copied,
    [property: Description("Files left alone because they already existed and overwrite was false.")]
    int Skipped,
    [property: Description("Files that could not be converted. Each one is listed in failures.")]
    int Failed,
    [property: Description("Files scaled down to fit a format with a hard size limit, such as ICO at 256x256.")]
    int Resized,
    [property: Description("Total size of the inputs that produced output, in bytes.")]
    long BytesIn,
    [property: Description("Total size of the output, in bytes.")]
    long BytesOut,
    [property: Description("How long the run took, in seconds.")]
    double Seconds,
    [property: Description("Every failure, with the reason it failed.")]
    IReadOnlyList<ConversionFailure> Failures)
{
    /// <summary>At most this many failures are listed; the count is always exact.</summary>
    private const int MaxFailuresListed = 50;

    internal static ConversionReport From(RunOutcome outcome)
    {
        if (outcome.Status is not RunStatus.Completed and not RunStatus.NothingToDo)
        {
            return new ConversionReport(false, Explain(outcome), outcome.Options?.DestinationPath,
                0, 0, 0, 0, 0, 0, 0, outcome.Elapsed.TotalSeconds, []);
        }

        var failures = outcome.Results.Where(r => r.Outcome == Outcome.Failed).ToList();

        bool produced(ConversionResult r) => r.Outcome is Outcome.Converted or Outcome.Copied;

        return new ConversionReport(
            true,
            outcome.Status == RunStatus.NothingToDo ? "No images matched the source." : null,
            outcome.Options?.DestinationPath,
            outcome.Results.Count(r => r.Outcome == Outcome.Converted),
            outcome.Results.Count(r => r.Outcome == Outcome.Copied),
            outcome.Results.Count(r => r.Outcome == Outcome.Skipped),
            failures.Count,
            outcome.Results.Count(r => r.Outcome == Outcome.Converted && r.Reason is not null),
            outcome.Results.Where(produced).Sum(r => r.BytesIn),
            outcome.Results.Where(produced).Sum(r => r.BytesOut),
            outcome.Elapsed.TotalSeconds,
            [.. failures.Take(MaxFailuresListed).Select(f => new ConversionFailure(f.Item.RelativePath, f.Reason ?? "unknown"))]);
    }

    private static string Explain(RunOutcome outcome) => outcome.Status switch
    {
        RunStatus.InvalidArguments => outcome.Message ?? "the arguments did not make sense",
        RunStatus.SourceMissing => outcome.Message ?? "the source does not exist",
        RunStatus.SourceUnreadable => outcome.Message ?? "the source could not be read",
        RunStatus.DestinationUnavailable =>
            (outcome.Message ?? "the destination folder could not be created") +
            ". On Windows this is usually Controlled Folder Access blocking the write.",
        _ => outcome.Message ?? outcome.Status.ToString(),
    };
}

public sealed record ConversionFailure(
    [property: Description("Path relative to the source folder.")] string File,
    [property: Description("Why this file could not be converted.")] string Reason);

public sealed record FormatListing(
    [property: Description("How many formats are listed.")] int Count,
    IReadOnlyList<FormatSummary> Formats);

public sealed record FormatSummary(
    [property: Description("The name to pass as a convert_images target, such as webp.")] string Name,
    [property: Description("ImageMagick's own description of the format.")] string Description,
    [property: Description("True when this format can be written, and so used as a target.")] bool Writable);

public sealed record FormatDetail(
    string Name,
    [property: Description("True when this format can be used as a convert_images target.")] bool Writable,
    string? Description,
    [property: Description("Whether the format can hold transparency. Null when it cannot be written.")] bool? SupportsTransparency,
    [property: Description("Whether the format can hold several frames, as animation or pages.")] bool? SupportsMultipleFrames,
    [property: Description("Hard pixel limit per side, such as 256 for ICO. Null means no limit.")] int? MaxDimension,
    [property: Description("Present only when something needs saying, such as the format being read-only.")] string? Note);

public sealed record ImageFacts(
    string Path,
    [property: Description("The format actually found in the file, which need not match its extension.")] string? Format,
    int Width,
    int Height,
    [property: Description("True when the image carries an alpha channel.")] bool HasTransparency,
    [property: Description("Frames in the file: more than one means animation or multiple pages or icon sizes.")] int Frames,
    long Bytes,
    [property: Description("Why the file could not be read, when it could not be.")] string? Error);

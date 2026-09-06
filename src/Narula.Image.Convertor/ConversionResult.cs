namespace Narula.Image.Convertor;

internal enum Outcome
{
    /// <summary>The file was decoded and re-encoded into the target format.</summary>
    Converted,

    /// <summary>Already in the target format at the requested quality, so the original was copied across.</summary>
    Copied,

    /// <summary>Deliberately left alone — the destination already existed and <c>-o false</c> was set.</summary>
    Skipped,

    /// <summary>Something went wrong; <see cref="ConversionResult.Reason"/> says what.</summary>
    Failed,
}

internal readonly record struct ConversionResult(
    WorkItem Item,
    Outcome Outcome,
    string? Reason,
    long BytesIn,
    long BytesOut)
{
    public static ConversionResult Converted(WorkItem item, long bytesIn, long bytesOut) =>
        new(item, Outcome.Converted, null, bytesIn, bytesOut);

    public static ConversionResult Copied(WorkItem item, long bytes, string reason) =>
        new(item, Outcome.Copied, reason, bytes, bytes);

    public static ConversionResult Skipped(WorkItem item, string reason) =>
        new(item, Outcome.Skipped, reason, 0, 0);

    public static ConversionResult Failed(WorkItem item, string reason) =>
        new(item, Outcome.Failed, reason, 0, 0);
}

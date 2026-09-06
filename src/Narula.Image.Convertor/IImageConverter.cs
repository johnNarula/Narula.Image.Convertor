namespace Narula.Image.Convertor;

/// <summary>
/// Converts one file. This exists as an interface so a Magick.NET-backed implementation can be
/// added later for the formats ImageSharp cannot read (HEIC, HEIF, AVIF, RAW, PSD) without
/// disturbing the scanner, the engine, or the reporting.
/// </summary>
internal interface IImageConverter
{
    ValueTask<ConversionResult> ConvertAsync(WorkItem item, CliOptions options, CancellationToken cancellationToken);
}

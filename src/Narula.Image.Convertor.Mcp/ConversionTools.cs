using System.ComponentModel;

using ImageMagick;

using ModelContextProtocol.Server;

using Narula.Image.Convertor;

namespace Narula.Image.Convertor.Mcp;

/// <summary>
/// The tools an agent can call. Every one of them goes through the same engine the window and
/// the command line use, so an agent cannot get different behaviour from a person doing the
/// same thing by hand.
/// </summary>
[McpServerToolType]
public static class ConversionTools
{
    [McpServerTool(Name = "convert_images")]
    [Description(
        "Converts images to another format. The source may be a folder, a single image file, " +
        "or a pattern such as C:\\photos\\*.heic. Returns what happened to every file, " +
        "including the reason for each failure. Nothing outside the destination folder is " +
        "written, and existing files are only replaced when overwrite is true.")]
    public static async Task<ConversionReport> ConvertImages(
        [Description("Folder, single image file, or a pattern like C:\\photos\\*.heic.")]
        string source,
        [Description("Target format, such as jpg, png, webp, avif, tiff, ico or pdf. Call list_formats for all of them.")]
        string target,
        [Description("Where the output goes. Defaults to a \"Converted to <target>\" folder inside the source folder.")]
        string? destination = null,
        [Description("Include subfolders. The destination mirrors the source tree.")]
        bool recursive = false,
        [Description("Encoder quality from 1 to 100, for formats that record one. Defaults to the configured value.")]
        int? quality = null,
        [Description("Replace files already in the destination.")]
        bool overwrite = true,
        [Description("Keep transparency where the target format can hold it.")]
        bool preserveTransparency = true,
        [Description("Keep EXIF and colour profile metadata.")]
        bool preserveMetadata = true,
        [Description("Colour transparency is flattened onto when the target cannot hold it, such as #FFFFFF or white.")]
        string? background = null,
        [Description("Which size to take from a multi-size .ico: 16, 32, 48, 64, 128 or 256. Omit for the largest.")]
        int? iconSize = null,
        CancellationToken cancellationToken = default)
    {
        List<string> args = ["-s", source, "-t", target];

        if (!string.IsNullOrWhiteSpace(destination)) args.AddRange(["-d", destination]);
        if (recursive) args.Add("-r");
        if (quality is { } q) args.AddRange(["-q", q.ToString()]);
        if (!string.IsNullOrWhiteSpace(background)) args.AddRange(["-bg", background]);
        if (iconSize is { } size) args.AddRange(["-iconsize", size.ToString()]);

        args.AddRange(["-o", overwrite ? "true" : "false"]);
        args.AddRange(["-trans", preserveTransparency ? "true" : "false"]);
        args.AddRange(["-m", preserveMetadata ? "true" : "false"]);

        RunOutcome outcome = await ConversionRun.ExecuteAsync([.. args], new SilentProgress(), cancellationToken);

        return ConversionReport.From(outcome);
    }

    [McpServerTool(Name = "list_formats")]
    [Description(
        "Lists the image formats this build can handle, with ImageMagick's own description of " +
        "each. Writable formats are the ones valid as a convert_images target.")]
    public static FormatListing ListFormats(
        [Description("Only formats that can be written. False also lists read-only formats.")]
        bool writableOnly = true)
    {
        List<FormatSummary> formats =
        [
            .. ImageFormats.WritableFormatsCommonFirst().Select(f => new FormatSummary(f.Name, f.Description, true)),
        ];

        if (!writableOnly)
        {
            HashSet<string> writable = [.. formats.Select(f => f.Name)];

            formats.AddRange(MagickNET.SupportedFormats
                .Where(f => f.SupportsReading && !f.SupportsWriting)
                .Select(f => f.Format.ToString().ToLowerInvariant())
                .Where(name => !writable.Contains(name))
                .Distinct()
                .Order()
                .Select(name => new FormatSummary(name, ImageFormats.Describe(name) ?? string.Empty, false)));
        }

        return new FormatListing(formats.Count, formats);
    }

    [McpServerTool(Name = "describe_format")]
    [Description(
        "What one format can and cannot do: whether it can be written, whether it holds " +
        "transparency or several frames, and any hard size limit.")]
    public static FormatDetail DescribeFormat(
        [Description("A format name such as webp, ico or heic, with or without a leading dot.")]
        string format)
    {
        string name = format.TrimStart('.').ToLowerInvariant();

        if (ImageFormats.ResolveTarget(name) is not { } resolved)
        {
            return new FormatDetail(name, false, ImageFormats.Describe(name), null, null, null,
                "Nothing can write this format. It may still be readable as a source.");
        }

        return new FormatDetail(
            name,
            true,
            ImageFormats.Describe(name),
            ImageFormats.SupportsAlpha(resolved),
            ImageFormats.SupportsMultipleFrames(resolved),
            ImageFormats.MaxDimension(resolved),
            null);
    }

    [McpServerTool(Name = "inspect_image")]
    [Description("Reads one image and reports its real format, size, transparency and frame count.")]
    public static ImageFacts InspectImage(
        [Description("Path to an image file.")] string path)
    {
        if (!File.Exists(path))
        {
            return new ImageFacts(path, null, 0, 0, false, 0, 0, $"No file at {path}");
        }

        try
        {
            using MagickImageCollection frames = new(path);
            IMagickImage<byte> first = frames[0];

            return new ImageFacts(
                path,
                first.Format.ToString().ToLowerInvariant(),
                (int)first.Width,
                (int)first.Height,
                first.HasAlpha,
                frames.Count,
                new FileInfo(path).Length,
                null);
        }
        catch (MagickException exception)
        {
            return new ImageFacts(path, null, 0, 0, false, 0, new FileInfo(path).Length, exception.Message);
        }
    }

    /// <summary>
    /// The engine reports progress as it goes; over a protocol call there is nobody watching, so
    /// it is dropped rather than buffered into a result nobody asked for.
    /// </summary>
    private sealed class SilentProgress : IProgressSink
    {
        public void Report(int completed, int total, string fileName)
        {
        }

        public void Finish()
        {
        }
    }
}

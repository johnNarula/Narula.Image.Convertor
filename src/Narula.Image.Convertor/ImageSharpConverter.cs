using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

// 'Narula.Image' shadows SixLabors.ImageSharp.Image, so the type needs an unambiguous name here.
using ISImage = SixLabors.ImageSharp.Image;

namespace Narula.Image.Convertor;

internal sealed class ImageSharpConverter : IImageConverter
{
    private const string TempSuffix = ".nimgtmp";

    /// <summary>Targets whose container can carry an alpha channel.</summary>
    private static readonly HashSet<string> AlphaCapable =
        new(StringComparer.Ordinal) { "png", "webp", "gif", "tiff", "tga" };

    /// <summary>
    /// Targets we let keep more than one frame. PNG is deliberately absent: converting an
    /// animated GIF to PNG should produce a still image, not an APNG.
    /// </summary>
    private static readonly HashSet<string> MultiFrameCapable =
        new(StringComparer.Ordinal) { "webp", "gif", "tiff" };

    /// <summary>
    /// Formats this decoder genuinely cannot read. Rejected up front with a clear reason rather
    /// than left to surface as whatever exception the format sniffer happens to throw. When a
    /// Magick.NET fallback is added, this is the set it takes over.
    /// </summary>
    private static readonly HashSet<string> RequiresFallbackDecoder =
        new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif", ".avif" };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async ValueTask<ConversionResult> ConvertAsync(WorkItem item, CliOptions options, CancellationToken cancellationToken)
    {
        string temporaryPath = item.DestinationPath + TempSuffix;

        try
        {
            if (RequiresFallbackDecoder.Contains(Path.GetExtension(item.SourcePath)))
            {
                return ConversionResult.Failed(item, "unsupported format (HEIC/AVIF)");
            }

            if (!options.Overwrite && File.Exists(item.DestinationPath))
            {
                return ConversionResult.Skipped(item, "exists");
            }

            long bytesIn = new FileInfo(item.SourcePath).Length;

            string? directory = Path.GetDirectoryName(item.DestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directories.EnsureExists(directory);
            }

            if (await CanPassThroughAsync(item, options, cancellationToken).ConfigureAwait(false))
            {
                if (!SamePath(item.SourcePath, item.DestinationPath))
                {
                    File.Copy(item.SourcePath, item.DestinationPath, overwrite: true);
                }

                return ConversionResult.Copied(item, bytesIn, $"already {options.TargetType}");
            }

            using ISImage image = await ISImage.LoadAsync(item.SourcePath, cancellationToken).ConfigureAwait(false);

            Prepare(image, options);

            // Encode to a sibling temp file and rename, so an interrupted run never leaves a
            // half-written image sitting where a valid one is expected.
            await using (FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await image.SaveAsync(output, CreateEncoder(options), cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, item.DestinationPath, overwrite: true);

            long bytesOut = new FileInfo(item.DestinationPath).Length;
            return ConversionResult.Converted(item, bytesIn, bytesOut);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemp(temporaryPath);
            throw;
        }
        catch (UnknownImageFormatException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "not a recognisable image");
        }
        catch (Exception exception) when (exception is InvalidImageContentException or ArgumentException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "corrupt or unreadable image");
        }
        catch (ImageFormatException exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "access denied");
        }
        catch (Exception exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, exception.Message);
        }
    }

    /// <summary>
    /// True when the source is already in the target format and the run asks for nothing that
    /// would change the pixels or the metadata, so the original bytes can be copied across
    /// untouched. Anything that would alter the file — flattening, stripping, re-quantising, or
    /// baking in a rotation — disqualifies the shortcut.
    /// </summary>
    private static async ValueTask<bool> CanPassThroughAsync(WorkItem item, CliOptions options, CancellationToken cancellationToken)
    {
        string source = Normalize(Path.GetExtension(item.SourcePath));
        string target = Normalize("." + options.TargetType);

        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            return false;
        }

        // The user asked for a transformation; copying would quietly ignore it.
        if (!options.PreserveTransparency || !options.PreserveMetadata)
        {
            return false;
        }

        // Header-only read: cheap enough to do for every candidate.
        ImageInfo info = await ISImage.IdentifyAsync(item.SourcePath, cancellationToken).ConfigureAwait(false);

        if (NeedsReorienting(info.Metadata.ExifProfile))
        {
            return false;
        }

        // JPEG is the only format here that records the quality it was written at, so it is the
        // only one where "-q" can force a genuine re-encode of an already-correct format.
        return source is not "jpg" || info.Metadata.GetJpegMetadata().Quality == options.Quality;
    }

    private static bool NeedsReorienting(ExifProfile? exif) =>
        exif is not null &&
        exif.TryGetValue(ExifTag.Orientation, out IExifValue<ushort>? orientation) &&
        orientation.Value > 1;

    private static void Prepare(ISImage image, CliOptions options)
    {
        // Bake rotation into the pixels and clear the tag, so output is upright regardless of
        // whether the target format or the viewer understands EXIF orientation.
        image.Mutate(context => context.AutoOrient());

        if (!MultiFrameCapable.Contains(options.TargetType) && image.Frames.Count > 1)
        {
            while (image.Frames.Count > 1)
            {
                image.Frames.RemoveFrame(image.Frames.Count - 1);
            }
        }

        bool targetKeepsAlpha = AlphaCapable.Contains(options.TargetType);
        if (!options.PreserveTransparency || !targetKeepsAlpha)
        {
            image.Mutate(context => context.BackgroundColor(options.Background));
        }

        if (!options.PreserveMetadata)
        {
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
        }
    }

    private static IImageEncoder CreateEncoder(CliOptions options) => options.TargetType switch
    {
        "jpg" or "jpeg" => new JpegEncoder { Quality = options.Quality },
        "png" => new PngEncoder(),
        "webp" => new WebpEncoder { Quality = options.Quality },
        "bmp" => new BmpEncoder(),
        "gif" => new GifEncoder(),
        "tiff" or "tif" => new TiffEncoder(),
        "tga" => new TgaEncoder(),
        _ => throw new NotSupportedException($"no encoder for '{options.TargetType}'"),
    };

    /// <summary>Collapses format aliases so jpg/jpeg and tif/tiff compare equal.</summary>
    private static string Normalize(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpeg" or "jpe" or "jfif" => "jpg",
        "tif" => "tiff",
        var other => other,
    };

    private static bool SamePath(string a, string b) => string.Equals(a, b, PathComparison);

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing the run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

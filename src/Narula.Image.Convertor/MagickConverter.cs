using ImageMagick;

namespace Narula.Image.Convertor;

internal sealed class MagickConverter : IImageConverter
{
    private const string TempSuffix = ".nimgtmp";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly MagickFormat _target;
    private readonly bool _targetKeepsAlpha;
    private readonly bool _targetKeepsFrames;
    private readonly int? _targetMaxDimension;

    /// <summary>Format capabilities are settled once here rather than per file.</summary>
    public MagickConverter(CliOptions options)
    {
        _target = options.TargetFormat;
        _targetKeepsAlpha = ImageFormats.SupportsAlpha(_target);
        _targetKeepsFrames = ImageFormats.SupportsMultipleFrames(_target);
        _targetMaxDimension = ImageFormats.MaxDimension(_target);
    }

    public async ValueTask<ConversionResult> ConvertAsync(WorkItem item, CliOptions options, CancellationToken cancellationToken)
    {
        string temporaryPath = item.DestinationPath + TempSuffix;

        try
        {
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

            if (CanPassThrough(item, options))
            {
                if (!string.Equals(item.SourcePath, item.DestinationPath, PathComparison))
                {
                    File.Copy(item.SourcePath, item.DestinationPath, overwrite: true);
                }

                return ConversionResult.Copied(item, bytesIn, $"already {options.TargetType}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool resized = await Task.Run(() => Encode(item, options, temporaryPath), cancellationToken).ConfigureAwait(false);

            File.Move(temporaryPath, item.DestinationPath, overwrite: true);

            long bytesOut = new FileInfo(item.DestinationPath).Length;
            return ConversionResult.Converted(item, bytesIn, bytesOut, resized ? $"resized to fit {options.TargetType}" : null);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemp(temporaryPath);
            throw;
        }
        catch (MagickCorruptImageErrorException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "corrupt or unreadable image");
        }
        catch (MagickMissingDelegateErrorException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "unsupported format");
        }
        catch (MagickException exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, Clean(exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "access denied");
        }
        catch (Exception exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, Clean(exception.Message));
        }
    }

    /// <summary>
    /// Multi-frame sources go through a collection so animation survives into a format that can
    /// hold it; everything else reads a single image, which is the first frame.
    /// </summary>
    /// <returns>True when the image had to be scaled down to fit the target's dimension cap.</returns>
    private bool Encode(WorkItem item, CliOptions options, string temporaryPath)
    {
        bool resized = false;

        if (_targetKeepsFrames)
        {
            using MagickImageCollection frames = new(item.SourcePath);

            if (frames.Count > 1)
            {
                frames.Coalesce();

                foreach (IMagickImage<byte> frame in frames)
                {
                    resized |= Prepare(frame, options);
                }

                frames.Write(temporaryPath, _target);
                return resized;
            }
        }

        using MagickImage image = new(item.SourcePath);
        resized = Prepare(image, options);
        image.Write(temporaryPath, _target);
        return resized;
    }

    private bool Prepare(IMagickImage<byte> image, CliOptions options)
    {
        // Bake rotation into the pixels and clear the tag, so the result is upright regardless of
        // whether the target format or the viewer understands EXIF orientation.
        image.AutoOrient();
        image.Orientation = OrientationType.TopLeft;

        // Some containers cap their dimensions — ICO at 512. Scale to fit rather than refuse:
        // the request only makes sense at icon size. After AutoOrient, so a sideways photo is
        // measured the way it will actually be stored.
        bool resized = false;

        if (_targetMaxDimension is { } cap && (image.Width > cap || image.Height > cap))
        {
            image.Resize(new MagickGeometry((uint)cap, (uint)cap) { Greater = true });
            resized = true;
        }

        if (!options.PreserveTransparency || !_targetKeepsAlpha)
        {
            image.BackgroundColor = options.Background;
            image.Alpha(AlphaOption.Remove);
        }

        if (!options.PreserveMetadata)
        {
            image.Strip();
        }

        if (options.Quality is { } quality)
        {
            image.Quality = (uint)quality;
        }

        return resized;
    }

    /// <summary>
    /// True when the source is already in the target format and the run asks for nothing that
    /// would change the file, so the original bytes can be copied across untouched. Reads only
    /// the header — a full decode here would cost more than the copy it is trying to avoid.
    /// </summary>
    private bool CanPassThrough(WorkItem item, CliOptions options)
    {
        if (!options.PreserveTransparency || !options.PreserveMetadata)
        {
            return false;
        }

        MagickImageInfo info;

        try
        {
            info = new MagickImageInfo(item.SourcePath);
        }
        catch (MagickException)
        {
            return false;   // Unreadable header: let the real decode report why.
        }

        if (ImageFormats.Canonical(info.Format) != ImageFormats.Canonical(_target))
        {
            return false;
        }

        if (info.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft))
        {
            return false;
        }

        // Only formats that record the quality they were written at can tell us whether -q would
        // actually change anything; where it is unknown, re-encoding would be a guess.
        return options.Quality is not { } requested || info.Quality == 0 || info.Quality == (uint)requested;
    }

    /// <summary>
    /// ImageMagick messages carry the offending path and the C source location that raised them —
    /// "image type not supported `C:\photos\x.heic' @ error/heic.c/ReadHEICImage/1036". The report
    /// already shows the path, so trim back to the part that tells the user something.
    /// </summary>
    private static string Clean(string message)
    {
        string text = message.Trim();

        int location = text.IndexOf(" @ ", StringComparison.Ordinal);
        if (location > 0)
        {
            text = text[..location];
        }

        int quotedPath = text.IndexOf(" `", StringComparison.Ordinal);
        if (quotedPath > 0)
        {
            text = text[..quotedPath];
        }

        int newline = text.IndexOfAny(['\r', '\n']);
        return (newline < 0 ? text : text[..newline]).Trim();
    }

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

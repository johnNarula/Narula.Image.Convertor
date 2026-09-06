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

    /// <summary>Format capabilities are settled once here rather than per file.</summary>
    public MagickConverter(CliOptions options)
    {
        _target = options.TargetFormat;
        _targetKeepsAlpha = ImageFormats.SupportsAlpha(_target);
        _targetKeepsFrames = ImageFormats.SupportsMultipleFrames(_target);
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

            await Task.Run(() => Encode(item, options, temporaryPath), cancellationToken).ConfigureAwait(false);

            File.Move(temporaryPath, item.DestinationPath, overwrite: true);

            long bytesOut = new FileInfo(item.DestinationPath).Length;
            return ConversionResult.Converted(item, bytesIn, bytesOut);
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
            return ConversionResult.Failed(item, FirstLine(exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, "access denied");
        }
        catch (Exception exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, FirstLine(exception.Message));
        }
    }

    /// <summary>
    /// Multi-frame sources go through a collection so animation survives into a format that can
    /// hold it; everything else reads a single image, which is the first frame.
    /// </summary>
    private void Encode(WorkItem item, CliOptions options, string temporaryPath)
    {
        if (_targetKeepsFrames)
        {
            using MagickImageCollection frames = new(item.SourcePath);

            if (frames.Count > 1)
            {
                frames.Coalesce();

                foreach (IMagickImage<byte> frame in frames)
                {
                    Prepare(frame, options);
                }

                frames.Write(temporaryPath, _target);
                return;
            }
        }

        using MagickImage image = new(item.SourcePath);
        Prepare(image, options);
        image.Write(temporaryPath, _target);
    }

    private void Prepare(IMagickImage<byte> image, CliOptions options)
    {
        // Bake rotation into the pixels and clear the tag, so the result is upright regardless of
        // whether the target format or the viewer understands EXIF orientation.
        image.AutoOrient();
        image.Orientation = OrientationType.TopLeft;

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

    private static string FirstLine(string message)
    {
        string trimmed = message.Trim();
        int newline = trimmed.IndexOfAny(['\r', '\n']);
        return newline < 0 ? trimmed : trimmed[..newline];
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

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
    private readonly bool _targetIsIcon;

    /// <summary>Format capabilities are settled once here rather than per file.</summary>
    public MagickConverter(CliOptions options)
    {
        _target = options.TargetFormat;
        _targetKeepsAlpha = ImageFormats.SupportsAlpha(_target);
        _targetKeepsFrames = ImageFormats.SupportsMultipleFrames(_target);
        _targetMaxDimension = ImageFormats.MaxDimension(_target);
        _targetIsIcon = ImageFormats.Canonical(_target) is MagickFormat.Ico or MagickFormat.Cur;
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
        catch (IconSizeNotFoundException exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, exception.Message);
        }
        catch (Exception exception) when (IsPermissionRefusal(exception))
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, ConversionResult.PermissionDenied);
        }
        catch (MagickException exception)
        {
            TryDeleteTemp(temporaryPath);
            return ConversionResult.Failed(item, Clean(exception.Message));
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

        // An .ico holds the same picture at several sizes. Reading it as a single image takes
        // whichever the file happens to list first, which is normally the 16px one — a surprising
        // answer to "convert this icon to a png".
        if (_targetIsIcon)
        {
            return WriteIcon(item, options, temporaryPath);
        }

        if (IsMultiSizeIcon(item.SourcePath, options))
        {
            using MagickImageCollection sizes = new(item.SourcePath);
            using IMagickImage<byte> chosen = SelectIconFrame(sizes, options);

            resized = Prepare(chosen, options);
            chosen.Write(temporaryPath, _target);
            return resized;
        }

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

    /// <summary>
    /// AVIF treats quality 100 as a request for lossless, and this build's AOM encoder refuses
    /// that unless chroma delta-q is also disabled — so a plain 100 fails outright with
    /// "Only --enable_chroma_deltaq=0 can be used with --lossless=1". 99 encodes fine and is
    /// visually indistinguishable, so the ceiling is lowered rather than the file lost.
    /// </summary>
    private int ClampQuality(int quality) =>
        ImageFormats.Canonical(_target) is MagickFormat.Avif && quality >= 100 ? 99 : quality;

    /// <summary>
    /// Builds a proper icon: the same picture at every conventional size the source can supply,
    /// which is what an .ico is for. Sizes larger than the source are skipped rather than
    /// upscaled, and -iconsize narrows it to a single size.
    /// </summary>
    private bool WriteIcon(WorkItem item, CliOptions options, string temporaryPath)
    {
        using IMagickImage<byte> source = ReadForIcon(item, options);

        // Prepare without the dimension cap: each entry is sized individually below.
        Prepare(source, options, applyDimensionCap: false);

        uint longest = Math.Max(source.Width, source.Height);
        int[] wanted = options.IconSize is { } only
            ? [only]
            : [.. CliOptions.IconSizes.Where(size => size <= longest)];

        // A source smaller than the smallest conventional size still deserves an icon.
        if (wanted.Length == 0)
        {
            wanted = [CliOptions.IconSizes[0]];
        }

        // Icons are square. Fit the picture inside the box and pad the remainder rather than
        // cropping, so nothing is lost; the padding is transparent unless the run asked for
        // transparency to be flattened, in which case it takes the matte colour.
        IMagickColor<byte> padding = options.PreserveTransparency ? MagickColors.Transparent : options.Background;

        using MagickImageCollection entries = [];

        foreach (int size in wanted)
        {
            IMagickImage<byte> entry = source.Clone();

            entry.Resize(new MagickGeometry((uint)size, (uint)size) { Greater = true });

            if (options.PreserveTransparency)
            {
                entry.Alpha(AlphaOption.Set);
            }

            entry.BackgroundColor = padding;
            entry.Extent(new MagickGeometry((uint)size, (uint)size), Gravity.Center);

            entry.Format = _target;
            entries.Add(entry);
        }

        entries.Write(temporaryPath, _target);

        return longest > wanted[^1];
    }

    /// <summary>The image an icon should be built from, unwrapping a multi-size icon source.</summary>
    private IMagickImage<byte> ReadForIcon(WorkItem item, CliOptions options)
    {
        if (IsMultiSizeIcon(item.SourcePath, options))
        {
            using MagickImageCollection sizes = new(item.SourcePath);
            return SelectIconFrame(sizes, options);
        }

        return new MagickImage(item.SourcePath);
    }

    /// <summary>
    /// True when the source is an icon carrying more than one size and we are picking one out of
    /// it. A multi-size icon converted to another icon keeps all its sizes unless -iconsize asks
    /// for a specific one.
    /// </summary>
    private bool IsMultiSizeIcon(string path, CliOptions options)
    {
        if (_targetKeepsFrames && options.IconSize is null && !_targetIsIcon)
        {
            return false;
        }

        try
        {
            if (ImageFormats.Canonical(new MagickImageInfo(path).Format) is not (MagickFormat.Ico or MagickFormat.Cur))
            {
                return false;
            }
        }
        catch (MagickException)
        {
            return false;
        }

        using MagickImageCollection sizes = new(path);
        return sizes.Count > 1;
    }

    /// <summary>
    /// Picks the requested size, or the largest when none was asked for. A clone is returned so
    /// the collection can be disposed without taking the chosen image with it.
    /// </summary>
    private static IMagickImage<byte> SelectIconFrame(MagickImageCollection sizes, CliOptions options)
    {
        if (options.IconSize is not { } wanted)
        {
            return sizes.OrderByDescending(f => (long)f.Width * f.Height).First().Clone();
        }

        IMagickImage<byte>? match = sizes.FirstOrDefault(f => Math.Max(f.Width, f.Height) == wanted);

        if (match is null)
        {
            string available = string.Join(", ", sizes.Select(f => $"{f.Width}x{f.Height}").Distinct());
            throw new IconSizeNotFoundException($"no {wanted}px image inside; it holds {available}");
        }

        return match.Clone();
    }

    private bool Prepare(IMagickImage<byte> image, CliOptions options, bool applyDimensionCap = true)
    {
        // Bake rotation into the pixels and clear the tag, so the result is upright regardless of
        // whether the target format or the viewer understands EXIF orientation.
        image.AutoOrient();
        image.Orientation = OrientationType.TopLeft;

        // Some containers cap their dimensions — ICO at 512. Scale to fit rather than refuse:
        // the request only makes sense at icon size. After AutoOrient, so a sideways photo is
        // measured the way it will actually be stored.
        bool resized = false;

        if (applyDimensionCap && _targetMaxDimension is { } cap && (image.Width > cap || image.Height > cap))
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
            image.Quality = (uint)ClampQuality(quality);
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
    /// Whether the operating system refused the write rather than the image being at fault.
    /// ImageMagick wraps a refused open in its own exception type and leaves the underlying
    /// reason in the text, so the message is the only reliable signal.
    /// </summary>
    internal static bool IsPermissionRefusal(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception.Message.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("access is denied", StringComparison.OrdinalIgnoreCase);

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

/// <summary>Raised when -iconsize names a size the icon does not contain.</summary>
internal sealed class IconSizeNotFoundException(string message) : Exception(message);

using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// Everything the tool needs to know about a format, asked of ImageMagick at runtime rather than
/// hardcoded. With nearly two hundred writable targets, a maintained table would be wrong within
/// a release; this cannot drift because it is the library answering about itself.
/// </summary>
internal static class ImageFormats
{
    /// <summary>Alpha capability is not in ImageMagick's format table, so it is probed once per format.</summary>
    private static readonly Dictionary<MagickFormat, bool> AlphaCache = [];
    private static readonly Lock AlphaGate = new();

    /// <summary>Resolves what the user typed after -t. Null when nothing can write that format.</summary>
    public static MagickFormat? ResolveTarget(string type)
    {
        if (!Enum.TryParse(type, ignoreCase: true, out MagickFormat format))
        {
            return null;
        }

        return MagickFormatInfo.Create(format) is { SupportsWriting: true } ? format : null;
    }

    public static bool SupportsMultipleFrames(MagickFormat format) =>
        MagickFormatInfo.Create(format)?.SupportsMultipleFrames ?? false;

    /// <summary>
    /// Whether an alpha channel survives a round trip into this format. Determined by encoding a
    /// tiny transparent image and reading back whether the transparency is still there — about
    /// three milliseconds, once per format per run. Formats that refuse the probe are assumed to
    /// keep alpha, which is the safe way to be wrong: the matte colour still applies if the
    /// encoder drops it.
    /// </summary>
    public static bool SupportsAlpha(MagickFormat format)
    {
        lock (AlphaGate)
        {
            if (AlphaCache.TryGetValue(format, out bool cached))
            {
                return cached;
            }

            bool supported;

            try
            {
                using MagickImage probe = new(MagickColors.Transparent, 2, 2);
                probe.Format = format;

                using MagickImage roundTripped = new(probe.ToByteArray());
                supported = roundTripped.HasAlpha;
            }
            catch (MagickException)
            {
                supported = true;
            }

            AlphaCache[format] = supported;
            return supported;
        }
    }

    /// <summary>
    /// Collapses aliases so a jpg source and a jpeg target compare equal. ImageMagick keeps Jpg
    /// and Jpeg (and Tif and Tiff) as separate enum members that mean the same encoder.
    /// </summary>
    public static MagickFormat Canonical(MagickFormat format) => format switch
    {
        MagickFormat.Jpg or MagickFormat.Jpe => MagickFormat.Jpeg,
        MagickFormat.Tif => MagickFormat.Tiff,
        MagickFormat.Ptif => MagickFormat.Tiff,
        _ => format,
    };

    /// <summary>The -formats listing: what can be read, what can be written.</summary>
    public static void WriteListing(TextWriter writer)
    {
        List<IMagickFormatInfo> all = [.. MagickNET.SupportedFormats];
        List<string> writable = [.. all.Where(f => f.SupportsWriting).Select(f => f.Format.ToString().ToLowerInvariant()).Order()];
        List<string> readOnly = [.. all.Where(f => f is { SupportsReading: true, SupportsWriting: false }).Select(f => f.Format.ToString().ToLowerInvariant()).Order()];

        writer.WriteLine($"ImageMagick {MagickNET.Version}");
        writer.WriteLine();
        writer.WriteLine($"Targets for -t  ({writable.Count} formats can be written)");
        writer.WriteLine();
        WriteColumns(writer, writable);
        writer.WriteLine();
        writer.WriteLine($"Read-only  ({readOnly.Count} formats can be read but not written)");
        writer.WriteLine();
        WriteColumns(writer, readOnly);
    }

    private static void WriteColumns(TextWriter writer, List<string> names)
    {
        const int Columns = 8;
        int width = names.Count == 0 ? 1 : names.Max(n => n.Length) + 2;

        for (int i = 0; i < names.Count; i += Columns)
        {
            writer.WriteLine("  " + string.Concat(names.Skip(i).Take(Columns).Select(n => n.PadRight(width))).TrimEnd());
        }
    }
}

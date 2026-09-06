using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// Command-line values worth putting into the window before anyone touches it, read from the
/// same flags the command line uses.
///
/// This is deliberately not <see cref="CliOptions.Parse"/>. That parser is strict, because a
/// batch job that ran with a misunderstood flag is worse than one that refused to start. Here
/// the opposite holds: the window is about to be shown to a person who can see and correct
/// every value, so anything unusable is simply left out and the rest is still offered. Nothing
/// in here can fail.
/// </summary>
internal sealed record UiPrefill
{
    public string? Source { get; init; }
    public string? Destination { get; init; }
    public string? Target { get; init; }
    public bool? Recursive { get; init; }
    public int? Quality { get; init; }
    public bool? Overwrite { get; init; }
    public bool? PreserveTransparency { get; init; }
    public bool? PreserveMetadata { get; init; }
    public string? Background { get; init; }
    public int? IconSize { get; init; }

    /// <summary>True when at least one value survived, so the window has something to apply.</summary>
    public bool HasAnything =>
        Source is not null || Destination is not null || Target is not null || Recursive is not null ||
        Quality is not null || Overwrite is not null || PreserveTransparency is not null ||
        PreserveMetadata is not null || Background is not null || IconSize is not null;

    public static UiPrefill From(string[] args)
    {
        string? source = null, destination = null, target = null, background = null;
        bool? recursive = null, overwrite = null, transparency = null, metadata = null;
        int? quality = null, iconSize = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-r":
                    recursive = true;
                    break;

                case "-s" when Value(args, ref i) is { } s && LooksLikeUsableSource(s):
                    source = s.TrimEnd('\\', '/');
                    break;

                case "-d" when Value(args, ref i) is { } d && LooksLikePath(d):
                    destination = d;
                    break;

                case "-t" when Value(args, ref i) is { } t && ImageFormats.ResolveTarget(t.TrimStart('.')) is not null:
                    target = t.TrimStart('.').ToLowerInvariant();
                    break;

                case "-q" when Value(args, ref i) is { } q && int.TryParse(q, out int parsed) && parsed is >= 1 and <= 100:
                    quality = parsed;
                    break;

                case "-iconsize" when Value(args, ref i) is { } n
                    && int.TryParse(n, out int size) && CliOptions.IconSizes.Contains(size):
                    iconSize = size;
                    break;

                case "-bg" when Value(args, ref i) is { } bg && IsColour(bg):
                    background = bg;
                    break;

                case "-o" when Bool(args, ref i) is { } o:
                    overwrite = o;
                    break;

                case "-trans" when Bool(args, ref i) is { } t:
                    transparency = t;
                    break;

                case "-m" when Bool(args, ref i) is { } m:
                    metadata = m;
                    break;

                default:
                    // Unknown flags, unusable values and stray words are skipped rather than
                    // reported: there is nowhere to report them to, and the window opens anyway.
                    break;
            }
        }

        return new UiPrefill
        {
            Source = source,
            Destination = destination,
            Target = target,
            Recursive = recursive,
            Quality = quality,
            Overwrite = overwrite,
            PreserveTransparency = transparency,
            PreserveMetadata = metadata,
            Background = background,
            IconSize = iconSize,
        };
    }

    /// <summary>
    /// Takes the value after a flag. The index only moves when something is actually taken, so
    /// "-s -t png" loses the unusable -s and still keeps -t rather than swallowing it. A value
    /// that is consumed but then rejected is still stepped over, which is what stops a bad
    /// value from being read a second time as a flag.
    /// </summary>
    private static string? Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            return null;
        }

        string candidate = args[i + 1];

        if (candidate.StartsWith('-') && candidate.Length > 1 && !Path.IsPathRooted(candidate))
        {
            return null;
        }

        i++;
        return candidate;
    }

    private static bool? Bool(string[] args, ref int i) => Value(args, ref i)?.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>
    /// A source is worth offering only if it is really there: a folder, a file, or a pattern in
    /// a folder that exists. Anything else would put a dead path in front of the user.
    /// </summary>
    private static bool LooksLikeUsableSource(string source)
    {
        if (!LooksLikePath(source))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(source) || File.Exists(source))
            {
                return true;
            }

            string name = Path.GetFileName(source);

            if (!name.Contains('*') && !name.Contains('?'))
            {
                return false;
            }

            string parent = Path.GetDirectoryName(source) is { Length: > 0 } directory ? directory : ".";
            return Directory.Exists(parent);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    /// <summary>A destination need not exist yet, but it does have to be a path.</summary>
    private static bool LooksLikePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        try
        {
            _ = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsColour(string value)
    {
        try
        {
            _ = new MagickColor(value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or MagickException)
        {
            return false;
        }
    }
}

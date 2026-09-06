using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>Everything the run needs, already validated. Built only by <see cref="Parse"/>.</summary>
internal sealed record CliOptions
{
    /// <summary>Exactly what the user typed after -s, kept for error messages.</summary>
    public required string SourceInput { get; init; }

    /// <summary>The folder to enumerate. For a glob or a single file, the folder containing it.</summary>
    public required string SourceRoot { get; init; }

    /// <summary>A simple glob (<c>*</c> and <c>?</c>) matched against file names. <c>*</c> means everything.</summary>
    public required string SourcePattern { get; init; }

    /// <summary>True when -s named one existing file, which is then converted regardless of its extension.</summary>
    public bool SourceIsSingleFile { get; init; }

    /// <summary>
    /// True when the user asked for a specific extension, by naming a file or writing a glob such
    /// as *.pdf. Explicit intent overrides the scanner's default list of image extensions.
    /// </summary>
    public bool SourceExtensionIsExplicit { get; init; }

    public required string DestinationPath { get; init; }

    /// <summary>Lower-cased, no leading dot — exactly as it will appear on output files.</summary>
    public required string TargetType { get; init; }

    /// <summary>The encoder <see cref="TargetType"/> resolved to.</summary>
    public required MagickFormat TargetFormat { get; init; }

    public bool Recursive { get; init; }

    /// <summary>Null when -q was not given, so formats keep whatever quality they were written at.</summary>
    public int? Quality { get; init; } = 85;

    public bool Overwrite { get; init; } = true;
    public bool PreserveTransparency { get; init; } = true;
    public MagickColor Background { get; init; } = new(MagickColors.White);
    public bool PreserveMetadata { get; init; } = true;
    public int Parallelism { get; init; } = Environment.ProcessorCount;
    public bool StopOnError { get; init; }

    public static ParseOutcome Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return ParseOutcome.Help;
        }

        string? source = null, destination = null, target = null;
        bool recursive = false, stopOnError = false;
        int? quality = 85;
        int parallelism = Environment.ProcessorCount;
        bool overwrite = true, preserveTransparency = true, preserveMetadata = true;
        MagickColor background = new(MagickColors.White);
        MagickFormat targetFormat = MagickFormat.Unknown;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];

            switch (flag.ToLowerInvariant())
            {
                case "-h" or "--help" or "-?" or "/?":
                    return ParseOutcome.Help;

                case "-formats" or "--formats":
                    return ParseOutcome.Formats;

                case "-r":
                    recursive = true;
                    break;

                case "-e":
                    stopOnError = true;
                    break;

                case "-s":
                    if (!TryTakeValue(args, ref i, flag, out string s, out string? err)) return ParseOutcome.Invalid(err);
                    source = s;
                    break;

                case "-d":
                    if (!TryTakeValue(args, ref i, flag, out string d, out err)) return ParseOutcome.Invalid(err);
                    destination = d;
                    break;

                case "-t":
                    if (!TryTakeValue(args, ref i, flag, out string t, out err)) return ParseOutcome.Invalid(err);
                    target = t.TrimStart('.').ToLowerInvariant();

                    if (ImageFormats.ResolveTarget(target) is not { } resolved)
                    {
                        return ParseOutcome.Invalid(
                            $"nothing can write '{t}'. Common targets: jpg png webp avif tiff bmp gif ico jxl pdf. " +
                            "Run 'nImgConvertor -formats' for the full list");
                    }

                    targetFormat = resolved;
                    break;

                case "-q":
                    if (!TryTakeValue(args, ref i, flag, out string q, out err)) return ParseOutcome.Invalid(err);
                    if (!int.TryParse(q, out int parsedQuality) || parsedQuality is < 1 or > 100)
                    {
                        return ParseOutcome.Invalid($"-q must be a whole number from 1 to 100, got '{q}'");
                    }
                    quality = parsedQuality;
                    break;

                case "-p":
                    if (!TryTakeValue(args, ref i, flag, out string p, out err)) return ParseOutcome.Invalid(err);
                    if (!int.TryParse(p, out parallelism) || parallelism < 1)
                    {
                        return ParseOutcome.Invalid($"-p must be a whole number of 1 or more, got '{p}'");
                    }
                    break;

                case "-o":
                    if (!TryTakeBool(args, ref i, flag, out overwrite, out err)) return ParseOutcome.Invalid(err);
                    break;

                case "-trans":
                    if (!TryTakeBool(args, ref i, flag, out preserveTransparency, out err)) return ParseOutcome.Invalid(err);
                    break;

                case "-m":
                    if (!TryTakeBool(args, ref i, flag, out preserveMetadata, out err)) return ParseOutcome.Invalid(err);
                    break;

                case "-bg":
                    if (!TryTakeValue(args, ref i, flag, out string bg, out err)) return ParseOutcome.Invalid(err);

                    try
                    {
                        background = new MagickColor(bg);
                    }
                    catch (Exception exception) when (exception is ArgumentException or MagickException)
                    {
                        return ParseOutcome.Invalid($"-bg must be a colour such as #FFFFFF or white, got '{bg}'");
                    }

                    break;

                default:
                    // A bare word here almost always means a path with spaces reached us
                    // already split by the shell, which is worth saying out loud.
                    return ParseOutcome.Invalid(flag.StartsWith('-')
                        ? $"unknown option '{flag}'"
                        : $"unexpected argument '{flag}' - if this is part of a path containing spaces, wrap the whole path in double quotes");
            }
        }

        if (source is null) return ParseOutcome.Invalid("-s (source) is required");
        if (target is null) return ParseOutcome.Invalid("-t (target type) is required");

        (string root, string pattern, bool singleFile, bool explicitExtension) = ResolveSource(source);

        // Without -d, output lands in a clearly named folder beside the originals. That folder
        // sits inside the source root, and FileScanner already refuses to read its own output.
        string resolvedDestination = destination is null
            ? Path.Combine(root, $"Converted to {target}")
            : Path.GetFullPath(destination);

        return ParseOutcome.Parsed(new CliOptions
        {
            SourceInput = source,
            SourceRoot = root,
            SourcePattern = pattern,
            SourceIsSingleFile = singleFile,
            SourceExtensionIsExplicit = explicitExtension,
            DestinationPath = resolvedDestination,
            TargetType = target,
            TargetFormat = targetFormat,
            Recursive = recursive,
            Quality = quality,
            Overwrite = overwrite,
            PreserveTransparency = preserveTransparency,
            Background = background,
            PreserveMetadata = preserveMetadata,
            Parallelism = parallelism,
            StopOnError = stopOnError,
        });
    }

    /// <summary>
    /// Works out what -s meant. A folder means every image in it; a path ending in a glob means
    /// only the names that match; an existing file means that file alone. A path that is none of
    /// those is treated as a folder, so <see cref="Application"/> can report it as missing.
    /// </summary>
    private static (string Root, string Pattern, bool SingleFile, bool ExplicitExtension) ResolveSource(string source)
    {
        string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (trimmed.Length == 0)
        {
            trimmed = source;
        }

        string lastSegment = Path.GetFileName(trimmed);

        if (lastSegment.Contains('*') || lastSegment.Contains('?'))
        {
            string directory = Path.GetDirectoryName(trimmed) is { Length: > 0 } parent ? parent : ".";

            // "*.pdf" names an extension outright; "*" or "shot-*" does not.
            string extension = Path.GetExtension(lastSegment);
            bool explicitExtension = extension.Length > 1 && !extension.Contains('*') && !extension.Contains('?');

            return (Path.GetFullPath(directory), lastSegment, false, explicitExtension);
        }

        string full = Path.GetFullPath(trimmed);

        if (File.Exists(full) && !Directory.Exists(full))
        {
            return (Path.GetDirectoryName(full) ?? ".", Path.GetFileName(full), true, true);
        }

        return (full, "*", false, false);
    }

    private static bool TryTakeValue(string[] args, ref int i, string flag, out string value, out string? error)
    {
        if (i + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"{flag} needs a value";
            return false;
        }

        value = args[++i];
        error = null;
        return true;
    }

    private static bool TryTakeBool(string[] args, ref int i, string flag, out bool value, out string? error)
    {
        value = true;

        if (!TryTakeValue(args, ref i, flag, out string raw, out error))
        {
            return false;
        }

        switch (raw.ToLowerInvariant())
        {
            case "true": value = true; return true;
            case "false": value = false; return true;
            default:
                error = $"{flag} must be followed by true or false, got '{raw}'";
                return false;
        }
    }

    public const string HelpText = """
        nImgConvertor 2 — batch image format conversion

        Usage:
          nImgConvertor -s <source> -t <type> [options]

          -s <path>      Source (required). One of:
                           a folder      C:\photos           every image in it
                           a pattern     C:\photos\*.jpg     only matching names
                           a single file C:\photos\one.jpg   just that file
          -r             Recurse into subfolders; destination mirrors the tree
          -d <path>      Destination folder. Defaults to a "Converted to <type>"
                         folder inside the source folder
          -t <type>      Target type. Common ones:
                           jpg png webp avif tiff bmp gif ico jxl pdf
                         Nearly 200 are writable — run -formats for the full list
          -q <1-100>     Encoder quality (default 85; formats that record one)
          -o <bool>      Overwrite existing destination files (default true)
          -trans <bool>  Preserve transparency (default true)
          -bg <colour>   Matte used when flattening, #RRGGBB or a name (default #FFFFFF)
          -m <bool>      Preserve EXIF/ICC metadata (default true)
          -p <n>         Parallel workers (default = CPU count)
          -e             Stop on first failure
          -formats       List every format that can be read and written
          -h             Show this help

        Reads HEIC, AVIF, camera RAW, PSD, SVG and around 260 others.

        Examples:
          nImgConvertor -s .\photos -t jpg
          nImgConvertor -s .\photos -r -d .\out -t webp -q 80
          nImgConvertor -s "C:\iPhone\*.heic" -t jpg
          nImgConvertor -s .\icons -d .\out -t jpg -trans false -bg #000000

        Exit codes:
          0  everything converted, copied, or intentionally skipped
          1  one or more files failed
          2  bad arguments, or the source folder does not exist
        """;
}

/// <summary>How parsing ended: show help, list formats, complain, or hand back options.</summary>
internal sealed record ParseOutcome(CliOptions? Options, string? Error, bool HelpRequested, bool FormatsRequested = false)
{
    public static readonly ParseOutcome Help = new(null, null, true);

    public static readonly ParseOutcome Formats = new(null, null, false, true);

    public static ParseOutcome Invalid(string? error) => new(null, error ?? "invalid arguments", false);

    public static ParseOutcome Parsed(CliOptions options) => new(options, null, false);
}

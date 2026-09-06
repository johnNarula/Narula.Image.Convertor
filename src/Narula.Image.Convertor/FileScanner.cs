using System.IO.Enumeration;

namespace Narula.Image.Convertor;

/// <summary>The work list, plus any items rejected before conversion started.</summary>
internal sealed record ScanResult(IReadOnlyList<WorkItem> Items, IReadOnlyList<ConversionResult> Rejected)
{
    public int Total => Items.Count + Rejected.Count;
}

internal static class FileScanner
{
    /// <summary>
    /// Extensions a folder scan will pick up. ImageMagick reads far more than this, including
    /// text-ish and document formats (txt, html, json, pdf) that nobody wants swept up by
    /// "convert this folder", so the scan uses a deliberate list of picture extensions instead of
    /// everything the library can decode. Anything outside it is still reachable by naming the
    /// file or globbing its extension, which <see cref="CliOptions.SourceExtensionIsExplicit"/>
    /// treats as the user overriding this list on purpose.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Everyday raster
        ".jpg", ".jpeg", ".jpe", ".jfif", ".jif", ".png", ".apng", ".gif", ".bmp", ".dib",
        ".webp", ".tif", ".tiff", ".tga", ".targa", ".ico", ".cur", ".pcx", ".qoi",
        // Modern codecs
        ".heic", ".heif", ".hif", ".avif", ".jxl", ".jp2", ".j2k", ".jpf", ".jpx", ".jpm",
        // Layered and vector
        ".psd", ".psb", ".xcf", ".svg", ".svgz", ".ai", ".eps", ".epsf",
        // Camera raw
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf",
        ".raf", ".rw2", ".pef", ".srw", ".x3f", ".erf", ".kdc", ".dcr", ".mrw", ".3fr",
        // Netpbm and the long tail
        ".ppm", ".pgm", ".pbm", ".pnm", ".pam", ".pfm", ".xpm", ".xbm",
        ".dds", ".exr", ".hdr", ".pict", ".pct", ".sgi", ".rgb", ".ras", ".sun",
        ".wbmp", ".fits", ".fts", ".miff", ".mng", ".jng", ".jbig", ".jbg",
    };

    /// <summary>The extensions a folder scan accepts, for anything offering the user a filter.</summary>
    public static IReadOnlyList<string> KnownImageExtensions { get; } =
        [.. ImageExtensions.Order(StringComparer.Ordinal)];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static ScanResult Scan(CliOptions options)
    {
        // An explicitly named file is honoured as given, extension filter and all: the user
        // pointed at exactly one thing, so the only sensible answer is to try it.
        if (options.SourceIsSingleFile)
        {
            string name = Path.GetFileName(options.SourcePattern);
            string destination = Path.Combine(options.DestinationPath, Path.ChangeExtension(name, "." + options.TargetType));

            return new ScanResult([new WorkItem(Path.Combine(options.SourceRoot, name), destination, name)], []);
        }

        EnumerationOptions enumeration = new()
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        string sourceRoot = options.SourceRoot;
        string destinationPrefix = WithSeparator(options.DestinationPath);
        bool destinationInsideSource =
            !PathsEqual(options.SourceRoot, options.DestinationPath) &&
            destinationPrefix.StartsWith(WithSeparator(sourceRoot), PathComparison);

        List<WorkItem> items = [];

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", enumeration))
        {
            // A named extension is the user overriding the default list, so honour it.
            if (!options.SourceExtensionIsExplicit && !ImageExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            // Matched here rather than passed to EnumerateFiles: Win32 pattern semantics have
            // surprises (*.tif matching .tiff and friends) that this API does not.
            if (options.SourcePattern != "*" &&
                !FileSystemName.MatchesSimpleExpression(options.SourcePattern, Path.GetFileName(file), ignoreCase: true))
            {
                continue;
            }

            // Never treat our own output as input.
            if (destinationInsideSource && file.StartsWith(destinationPrefix, PathComparison))
            {
                continue;
            }

            string relative = Path.GetRelativePath(sourceRoot, file);
            string destination = Path.Combine(options.DestinationPath, Path.ChangeExtension(relative, "." + options.TargetType));

            items.Add(new WorkItem(file, destination, relative));
        }

        return ResolveCollisions(items, options);
    }

    /// <summary>
    /// Two sources can map onto one destination (logo.png and logo.jpg both becoming logo.jpg).
    /// Pick a winner deterministically rather than letting two workers race for the same handle.
    /// The file already in the target format wins, since converting it is a no-op anyway.
    /// </summary>
    private static ScanResult ResolveCollisions(List<WorkItem> items, CliOptions options)
    {
        var groups = items.GroupBy(i => i.DestinationPath, StringComparer.FromComparison(PathComparison));

        List<WorkItem> kept = [];
        List<ConversionResult> rejected = [];

        foreach (var group in groups)
        {
            if (group.Count() == 1)
            {
                kept.Add(group.First());
                continue;
            }

            var ordered = group
                .OrderByDescending(i => Path.GetExtension(i.SourcePath).TrimStart('.').Equals(options.TargetType, StringComparison.OrdinalIgnoreCase))
                .ThenBy(i => i.RelativePath, StringComparer.FromComparison(PathComparison))
                .ToList();

            kept.Add(ordered[0]);

            foreach (WorkItem loser in ordered.Skip(1))
            {
                rejected.Add(ConversionResult.Failed(loser, $"destination collides with {ordered[0].RelativePath}"));
            }
        }

        kept.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, PathComparison));
        return new ScanResult(kept, rejected);
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), PathComparison);
}

namespace Narula.Image.Convertor;

/// <summary>The work list, plus any items rejected before conversion started.</summary>
internal sealed record ScanResult(IReadOnlyList<WorkItem> Items, IReadOnlyList<ConversionResult> Rejected)
{
    public int Total => Items.Count + Rejected.Count;
}

internal static class FileScanner
{
    /// <summary>
    /// Extensions we are willing to pick up. HEIC/HEIF/AVIF are here on purpose even though the
    /// current decoder cannot read them — the user should see them fail rather than silently
    /// vanish from the destination.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png", ".webp", ".bmp", ".gif",
        ".tif", ".tiff", ".tga", ".pbm", ".qoi",
        ".heic", ".heif", ".avif",
    };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static ScanResult Scan(CliOptions options)
    {
        EnumerationOptions enumeration = new()
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        string sourceRoot = options.SourcePath;
        string destinationPrefix = WithSeparator(options.DestinationPath);
        bool destinationInsideSource =
            !PathsEqual(options.SourcePath, options.DestinationPath) &&
            destinationPrefix.StartsWith(WithSeparator(sourceRoot), PathComparison);

        List<WorkItem> items = [];

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", enumeration))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(file)))
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

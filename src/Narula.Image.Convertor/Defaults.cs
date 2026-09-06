using System.Text.Json;

using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// The values a run starts from before any flag is given. Held in one place so changing a
/// preference does not mean hunting through the parser, and overridable from a settings file so
/// it does not mean rebuilding either.
/// </summary>
internal sealed record ToolDefaults
{
    /// <summary>Encoder quality for formats that record one.</summary>
    public int Quality { get; init; } = 100;

    public bool Overwrite { get; init; } = true;
    public bool PreserveTransparency { get; init; } = true;
    public bool PreserveMetadata { get; init; } = true;

    /// <summary>Matte used when flattening, and padding for square icons when not transparent.</summary>
    public string Background { get; init; } = "#FFFFFF";

    /// <summary>Workers to run at once. Zero means one per processor.</summary>
    public int Parallelism { get; init; }

    /// <summary>Folder created beside the source when -d is omitted; {0} is the target type.</summary>
    public string DestinationFolderFormat { get; init; } = "Converted to {0}";

    /// <summary>Sizes written into an .ico, and the values -iconsize accepts.</summary>
    public int[] IconSizes { get; init; } = [16, 32, 48, 64, 128, 256];

    public static readonly ToolDefaults BuiltIn = new();

    public int ResolvedParallelism => Parallelism > 0 ? Parallelism : Environment.ProcessorCount;

    public MagickColor ResolvedBackground => new(Background);
}

internal static class Defaults
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ToolDefaults Current { get; private set; } = ToolDefaults.BuiltIn;

    /// <summary>
    /// Your own settings, which survive reinstalling and work when the program itself lives
    /// somewhere you cannot write to, such as Program Files.
    /// </summary>
    public static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "9thAct",
        "img2img",
        FileName);

    /// <summary>Beside the executable, so a copied tool carries its settings with it.</summary>
    public static string InstalledSettingsPath =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, FileName);

    /// <summary>
    /// Where settings are looked for, in order. Yours wins over the installed copy, so an
    /// installer can lay down a default without ever overwriting a preference you set.
    /// </summary>
    public static IEnumerable<string> SearchPaths
    {
        get
        {
            yield return UserSettingsPath;
            yield return InstalledSettingsPath;
        }
    }

    /// <summary>The file actually in use, or null when neither exists and the built-ins apply.</summary>
    public static string? SettingsPath => SearchPaths.FirstOrDefault(File.Exists);

    public static void Initialise(TextWriter warnings) =>
        Current = SettingsPath is { } path ? Load(path, warnings) : ToolDefaults.BuiltIn;

    /// <summary>
    /// Reads a settings file, falling back to the built-in value for anything missing or
    /// unusable. A bad settings file warns and is ignored rather than stopping the run: the tool
    /// is still perfectly able to convert images with its own defaults.
    /// </summary>
    public static ToolDefaults Load(string path, TextWriter warnings)
    {
        if (!File.Exists(path))
        {
            return ToolDefaults.BuiltIn;
        }

        SettingsFile? file;

        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.WriteLine($"img2img: ignoring {path}: {exception.Message}");
            return ToolDefaults.BuiltIn;
        }

        if (file is null)
        {
            return ToolDefaults.BuiltIn;
        }

        ToolDefaults defaults = ToolDefaults.BuiltIn;

        if (file.Quality is { } quality)
        {
            if (quality is >= 1 and <= 100)
            {
                defaults = defaults with { Quality = quality };
            }
            else
            {
                warnings.WriteLine($"img2img: ignoring quality {quality} in {FileName}: must be 1 to 100");
            }
        }

        if (file.Parallelism is { } parallelism)
        {
            if (parallelism >= 0)
            {
                defaults = defaults with { Parallelism = parallelism };
            }
            else
            {
                warnings.WriteLine($"img2img: ignoring parallelism {parallelism} in {FileName}: must be 0 or more");
            }
        }

        if (file.Background is { } background)
        {
            try
            {
                _ = new MagickColor(background);
                defaults = defaults with { Background = background };
            }
            catch (Exception exception) when (exception is ArgumentException or MagickException)
            {
                warnings.WriteLine($"img2img: ignoring background '{background}' in {FileName}: not a colour");
            }
        }

        if (file.DestinationFolderFormat is { Length: > 0 } folder)
        {
            if (folder.Contains("{0}", StringComparison.Ordinal))
            {
                defaults = defaults with { DestinationFolderFormat = folder };
            }
            else
            {
                warnings.WriteLine($"img2img: ignoring destinationFolderFormat in {FileName}: it must contain {{0}}");
            }
        }

        if (file.IconSizes is { Length: > 0 } sizes)
        {
            if (sizes.All(s => s is > 0 and <= 256))
            {
                defaults = defaults with { IconSizes = [.. sizes.Distinct().Order()] };
            }
            else
            {
                warnings.WriteLine($"img2img: ignoring iconSizes in {FileName}: every size must be 1 to 256");
            }
        }

        return defaults with
        {
            Overwrite = file.Overwrite ?? defaults.Overwrite,
            PreserveTransparency = file.PreserveTransparency ?? defaults.PreserveTransparency,
            PreserveMetadata = file.PreserveMetadata ?? defaults.PreserveMetadata,
        };
    }

    /// <summary>Every field optional, so a settings file need only mention what it changes.</summary>
    private sealed record SettingsFile
    {
        public int? Quality { get; init; }
        public bool? Overwrite { get; init; }
        public bool? PreserveTransparency { get; init; }
        public bool? PreserveMetadata { get; init; }
        public string? Background { get; init; }
        public int? Parallelism { get; init; }
        public string? DestinationFolderFormat { get; init; }
        public int[]? IconSizes { get; init; }
    }
}

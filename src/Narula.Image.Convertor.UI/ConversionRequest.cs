using Narula.Image.Convertor;

namespace Narula.Image.Convertor.UI;

/// <summary>What the window has been filled in with, in the terms the engine already understands.</summary>
internal sealed record ConversionRequest
{
    public string Source { get; init; } = string.Empty;
    public string? Destination { get; init; }
    public string Target { get; init; } = "jpg";
    public bool Recursive { get; init; }
    public int Quality { get; init; } = Defaults.Current.Quality;
    public bool Overwrite { get; init; } = Defaults.Current.Overwrite;
    public bool PreserveTransparency { get; init; } = Defaults.Current.PreserveTransparency;
    public bool PreserveMetadata { get; init; } = Defaults.Current.PreserveMetadata;
    public string Background { get; init; } = Defaults.Current.Background;
    public int? IconSize { get; init; }

    /// <summary>
    /// Builds the same argument list a person would type. Going through the real parser rather
    /// than constructing options directly means the window cannot drift from the command line:
    /// every validation rule, default and quirk applies identically to both.
    /// </summary>
    public string[] ToArguments()
    {
        List<string> args = ["-s", Source, "-t", Target];

        if (!string.IsNullOrWhiteSpace(Destination))
        {
            args.AddRange(["-d", Destination]);
        }

        if (Recursive)
        {
            args.Add("-r");
        }

        args.AddRange(["-q", Quality.ToString()]);
        args.AddRange(["-o", Overwrite ? "true" : "false"]);
        args.AddRange(["-trans", PreserveTransparency ? "true" : "false"]);
        args.AddRange(["-m", PreserveMetadata ? "true" : "false"]);
        args.AddRange(["-bg", Background]);

        if (IconSize is { } size)
        {
            args.AddRange(["-iconsize", size.ToString()]);
        }

        return [.. args];
    }
}

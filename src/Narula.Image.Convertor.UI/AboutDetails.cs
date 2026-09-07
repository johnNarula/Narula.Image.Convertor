using Narula.Image.Convertor;

using AvaloniaApplication = Avalonia.Application;

namespace Narula.Image.Convertor.UI;

/// <summary>One labelled fact in the About box. A type rather than a tuple so the window can bind to it.</summary>
internal sealed record AboutRow(string Label, string Value);

/// <summary>
/// What the About box says. Kept out of the window itself so the facts can be tested, and so the
/// same text can be copied to the clipboard when someone is describing a problem.
/// </summary>
internal static class AboutDetails
{
    public static string Product => AppInfo.Product;

    public static string Version => $"Version {AppInfo.Version}";

    public static string Copyright => AppInfo.Copyright;

    public static string Summary =>
        "Converts images between formats, one file or a whole tree at a time, from a window or " +
        "from the command line as img2img.";

    /// <summary>
    /// Facts worth having when something goes wrong: which build, which engine, and which of the
    /// two settings files is actually in force.
    /// </summary>
    public static IReadOnlyList<AboutRow> Rows()
    {
        (int readable, int writable) = AppInfo.FormatCounts;

        List<AboutRow> rows =
        [
            new("Formats", $"{readable} readable, {writable} writable"),
            new("Engine", AppInfo.Engine),
            new("ImageMagick", AppInfo.ImageMagick),
            new("Interface", $"Avalonia {AssemblyVersion(typeof(AvaloniaApplication))}"),
            new("Runtime", AppInfo.Runtime),
            new("System", AppInfo.Platform),
        ];

        if (!string.IsNullOrWhiteSpace(Program.SettingsWarnings))
        {
            rows.Add(new("Settings warnings", Program.SettingsWarnings));
        }

        return rows;
    }

    /// <summary>
    /// The agent section. It exists because nobody discovers an MCP server by accident: the
    /// window is where someone looks, so this is where it says what was installed and how to
    /// point an agent at it.
    /// </summary>
    public static bool McpAvailable => McpConfig.IsInstalled;

    public static string McpHeading => "FOR AI AGENTS";

    public static string McpSummary => McpAvailable
        ? "An MCP server is installed beside this program. Point an AI agent at it and it can " +
          "convert images, list formats and inspect files through this same engine."
        : "The MCP server for AI agents was not installed beside this program. Reinstall to add it.";

    public static string McpClaudeCode => McpConfig.ClaudeCodeCommand();

    public static string McpOtherClients =>
        "Any other client: Copy config below, then merge it into that client's MCP settings file.";

    public static string McpTools =>
        string.Join("   ", McpConfig.Tools.Select(t => t.Tool));

    public static string McpSnippet() => McpConfig.Snippet();

    /// <summary>Where the programs and their licence files actually are.</summary>
    internal static string InstallFolder =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Licensing, from the window, because the executables are single-file: the notices for
    /// everything inside them are not visible unless something says where they went.
    /// </summary>
    public static string Licence =>
        "MIT licensed, with additional terms covering your files and indemnification. " +
        "Imaging by Magick.NET and ImageMagick (Apache-2.0). Window by Avalonia (MIT). " +
        $"Full text in LICENSE.txt and THIRD-PARTY-NOTICES.md, beside the program in {InstallFolder}.";

    public static string Author => "John Narula";

    public static string AuthorLink => "https://www.linkedin.com/in/johnNarula";

    /// <summary>The whole box as plain text, for pasting into a message.</summary>
    public static string AsText() => string.Join(
        Environment.NewLine,
        [
            Product,
            Version,
            Copyright,
            string.Empty,
            .. Rows().Select(r => $"{r.Label}: {r.Value}"),
            $"Author: {Author} ({AuthorLink})",
            string.Empty,
            Licence,
        ]);

    private static string AssemblyVersion(Type type)
    {
        Version? version = type.Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}

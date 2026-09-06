using System.Text.Json;

namespace Narula.Image.Convertor;

/// <summary>
/// What an AI agent needs in order to use this tool, and where to put it.
///
/// It lives in the engine rather than in the MCP server because two programs need it: the
/// server prints it with --print-config, and the window shows it in About so it can be found
/// without knowing the server exists. Both then quote the same path and the same wording.
/// </summary>
internal static class McpConfig
{
    /// <summary>The name agents will see the server by, and the name to register it under.</summary>
    public const string ServerName = "img2img";

    private const string ExecutableName = "img2imgMcp";

    /// <summary>
    /// The server executable. This may be the running process, when the server prints its own
    /// configuration, or the one sitting beside the window. When it has not been installed the
    /// expected path is still returned, so the instructions read sensibly either way.
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            string? directory = Path.GetDirectoryName(Environment.ProcessPath);

            if (string.IsNullOrEmpty(directory))
            {
                directory = AppContext.BaseDirectory;
            }

            string[] candidates = OperatingSystem.IsWindows()
                ? [ExecutableName + ".exe"]
                : [ExecutableName, ExecutableName + ".exe"];

            return candidates
                .Select(name => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists)
                ?? Path.Combine(directory, candidates[0]);
        }
    }

    /// <summary>False when the server was not shipped alongside, so About can say so.</summary>
    public static bool IsInstalled => File.Exists(ExecutablePath);

    /// <summary>
    /// The form Claude Desktop, Claude Code and most other clients take: one entry under
    /// mcpServers naming a command. It is meant to be merged into an existing file, not to
    /// replace one.
    /// </summary>
    public static string Snippet()
    {
        var document = new
        {
            mcpServers = new Dictionary<string, object>
            {
                [ServerName] = new { command = ExecutablePath, args = Array.Empty<string>() },
            },
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The one-line way in, for anyone using Claude Code.</summary>
    public static string ClaudeCodeCommand() => $"claude mcp add {ServerName} -- \"{ExecutablePath}\"";

    /// <summary>What the tools are, in the fewest words that still say something.</summary>
    public static IReadOnlyList<(string Tool, string Does)> Tools { get; } =
    [
        ("convert_images", "convert a folder, a file or a pattern to another format"),
        ("list_formats", "every format this build can write, and optionally read"),
        ("describe_format", "what one format supports: transparency, frames, size limits"),
        ("inspect_image", "the real format, size, transparency and frame count of a file"),
    ];

    /// <summary>The whole thing as plain text, for a terminal or the clipboard.</summary>
    public static string Instructions() => $"""
        img2img MCP server

        Lets an AI agent convert images for you, through exactly the same engine as the window
        and the command line.

        Claude Code, in one command:

          {ClaudeCodeCommand()}

        Any other client: add this to its MCP configuration file, merging it with whatever
        servers are already listed there.

        {Snippet()}

        The tools the agent gains:

        {string.Join(Environment.NewLine, Tools.Select(t => $"  {t.Tool,-16}{t.Does}"))}

        The server speaks over stdin and stdout, so running it by hand does nothing visible.
        It is meant to be launched by the agent.
        """;
}

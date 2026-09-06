using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Narula.Image.Convertor;
using Narula.Image.Convertor.Mcp;

// Run by hand, this is not much use, so it says so and prints what an agent needs instead of
// sitting silently waiting on a pipe nobody is writing to.
if (args.Any(a => a is "-h" or "--help" or "-?" or "/?"))
{
    Console.WriteLine(McpConfig.Instructions());
    return 0;
}

if (args.Any(a => a is "--print-config" or "-config"))
{
    Console.WriteLine(McpConfig.Snippet());
    return 0;
}

// The same settings.json as the other two front ends. Warnings go to standard error because
// standard output is the protocol channel and anything else on it corrupts the conversation.
Defaults.Initialise(Console.Error);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Logging follows for the same reason: the default console logger would write protocol-breaking
// text to standard output.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options =>
    {
        // Named for the command people already know, not for the binary. The instructions are
        // what an agent reads before deciding whether this server is worth calling.
        options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = McpConfig.ServerName,
            Version = AppInfo.Version,
        };

        options.ServerInstructions = $"""
            Converts images between formats using ImageMagick: {AppInfo.FormatCounts.Readable}
            formats can be read and {AppInfo.FormatCounts.Writable} written, including HEIC,
            AVIF, camera RAW, PSD and SVG.

            convert_images takes a folder, a single file or a pattern, and writes into a
            destination folder, defaulting to one beside the source. It never writes anywhere
            else. Ask describe_format before choosing an unusual target: it says whether that
            format keeps transparency, holds several frames, or caps its own size.
            """;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

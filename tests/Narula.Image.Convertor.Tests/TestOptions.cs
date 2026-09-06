using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

internal static class TestOptions
{
    /// <summary>Builds options the same way the CLI does, so tests exercise the real parser.</summary>
    public static CliOptions For(TestWorkspace workspace, string target, params string[] extra) =>
        CliOptions.Parse(["-s", workspace.Source, "-d", workspace.Destination, "-t", target, .. extra]).Options
        ?? throw new InvalidOperationException("test options failed to parse");
}

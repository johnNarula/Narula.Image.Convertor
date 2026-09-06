using System.Text.Json;

using Narula.Image.Convertor;
using Narula.Image.Convertor.Mcp;

namespace ImageConvertor.Tests;

/// <summary>
/// The MCP server's own surface. The protocol conversation itself is exercised separately by
/// driving the executable over stdio; these cover what that conversation carries.
/// </summary>
public class McpTests
{
    [Fact]
    public void The_configuration_snippet_is_valid_json_naming_the_server_and_its_path()
    {
        using JsonDocument document = JsonDocument.Parse(McpConfig.Snippet());

        JsonElement server = document.RootElement.GetProperty("mcpServers").GetProperty(McpConfig.ServerName);

        Assert.Equal(McpConfig.ExecutablePath, server.GetProperty("command").GetString());
        Assert.Empty(server.GetProperty("args").EnumerateArray());
    }

    [Fact]
    public void The_claude_code_command_quotes_the_path()
    {
        // Every real install path here has spaces in it, so an unquoted command would silently
        // register a truncated one.
        string command = McpConfig.ClaudeCodeCommand();

        Assert.Contains($"\"{McpConfig.ExecutablePath}\"", command);
        Assert.Contains(McpConfig.ServerName, command);
    }

    [Fact]
    public void The_instructions_name_every_tool_the_server_actually_exposes()
    {
        string instructions = McpConfig.Instructions();

        Assert.All(McpConfig.Tools, t => Assert.Contains(t.Tool, instructions));
        Assert.Contains("convert_images", instructions);
    }

    [Fact]
    public async Task A_real_conversion_comes_back_as_counts_and_a_destination()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("one.png");
        workspace.WritePng("two.png");

        ConversionReport report = await ConversionTools.ConvertImages(workspace.Source, "jpg");

        Assert.True(report.Ran);
        Assert.Equal(2, report.Converted);
        Assert.Equal(0, report.Failed);
        Assert.Empty(report.Failures);
        Assert.NotNull(report.Destination);
        Assert.True(Directory.Exists(report.Destination));
        Assert.True(report.BytesOut > 0);
    }

    [Fact]
    public async Task A_failure_arrives_named_and_explained_rather_than_as_a_thrown_error()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("fine.png");
        workspace.WriteCorrupt("broken.png");

        ConversionReport report = await ConversionTools.ConvertImages(workspace.Source, "jpg");

        Assert.True(report.Ran);
        Assert.Equal(1, report.Converted);
        Assert.Equal(1, report.Failed);
        Assert.Contains(report.Failures, f => f.File.Contains("broken") && !string.IsNullOrWhiteSpace(f.Reason));
    }

    [Fact]
    public async Task An_impossible_request_says_why_instead_of_reporting_a_run()
    {
        using TestWorkspace workspace = new();

        ConversionReport target = await ConversionTools.ConvertImages(workspace.Source, "not-a-format");
        Assert.False(target.Ran);
        Assert.Contains("not-a-format", target.Message);

        ConversionReport source = await ConversionTools.ConvertImages(
            Path.Combine(workspace.Root, "no-such-folder"), "png");
        Assert.False(source.Ran);
        Assert.NotNull(source.Message);
    }

    [Fact]
    public void Formats_are_listed_with_the_ones_people_ask_for_first()
    {
        FormatListing listing = ConversionTools.ListFormats(writableOnly: true);

        Assert.Equal("jpg", listing.Formats[0].Name);
        Assert.Equal(listing.Count, listing.Formats.Count);
        Assert.All(listing.Formats, f => Assert.True(f.Writable));

        // The order changes what is offered first; it must not change what is offered.
        Assert.Equal(
            ImageFormats.WritableFormats().Select(f => f.Name).Order(),
            listing.Formats.Select(f => f.Name).Order());
    }

    [Fact]
    public void A_format_reports_its_own_limits()
    {
        FormatDetail ico = ConversionTools.DescribeFormat("ico");

        Assert.True(ico.Writable);
        Assert.Equal(256, ico.MaxDimension);
        Assert.True(ico.SupportsTransparency);
        Assert.True(ico.SupportsMultipleFrames);

        FormatDetail jpeg = ConversionTools.DescribeFormat(".jpg");
        Assert.True(jpeg.Writable);
        Assert.False(jpeg.SupportsTransparency);
        Assert.Null(jpeg.MaxDimension);

        FormatDetail nonsense = ConversionTools.DescribeFormat("not-a-format");
        Assert.False(nonsense.Writable);
        Assert.NotNull(nonsense.Note);
    }

    [Fact]
    public void An_image_reports_what_it_really_is()
    {
        using TestWorkspace workspace = new();
        string transparent = workspace.WriteTransparentPng("clear.png");

        ImageFacts facts = ConversionTools.InspectImage(transparent);

        Assert.Equal("png", facts.Format);
        Assert.Equal(TestWorkspace.Width, facts.Width);
        Assert.Equal(TestWorkspace.Height, facts.Height);
        Assert.True(facts.HasTransparency);
        Assert.Equal(1, facts.Frames);
        Assert.Null(facts.Error);
    }

    [Fact]
    public void A_multi_frame_file_is_reported_as_one()
    {
        using TestWorkspace workspace = new();
        string animated = workspace.WriteAnimatedGif("moving.gif", frames: 3);

        Assert.Equal(3, ConversionTools.InspectImage(animated).Frames);
    }

    [Fact]
    public void A_file_that_is_not_there_is_explained_rather_than_thrown()
    {
        ImageFacts missing = ConversionTools.InspectImage(@"Z:\nope\absent.png");

        Assert.NotNull(missing.Error);
        Assert.Null(missing.Format);
    }
}

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parses_a_complete_command_line()
    {
        ParseOutcome outcome = CliOptions.Parse([
            "-s", @"C:\in", "-r", "-d", @"C:\out", "-t", "WebP",
            "-q", "72", "-o", "false", "-trans", "false", "-bg", "#102030",
            "-m", "false", "-p", "3", "-e",
        ]);

        CliOptions options = Assert.IsType<CliOptions>(outcome.Options);

        Assert.True(options.Recursive);
        Assert.Equal("webp", options.TargetType);
        Assert.Equal(72, options.Quality);
        Assert.False(options.Overwrite);
        Assert.False(options.PreserveTransparency);
        Assert.False(options.PreserveMetadata);
        Assert.Equal(3, options.Parallelism);
        Assert.True(options.StopOnError);
        Assert.Equal(new Rgba32(0x10, 0x20, 0x30), options.Background.ToPixel<Rgba32>());
    }

    [Fact]
    public void Applies_documented_defaults()
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", "jpg");

        Assert.False(options.Recursive);
        Assert.Equal(85, options.Quality);
        Assert.True(options.Overwrite);
        Assert.True(options.PreserveTransparency);
        Assert.True(options.PreserveMetadata);
        Assert.False(options.StopOnError);
        Assert.Equal(Environment.ProcessorCount, options.Parallelism);
        Assert.Equal(Color.White, options.Background);
    }

    [Fact]
    public void Resolves_paths_to_absolute()
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", "png");

        Assert.True(Path.IsPathFullyQualified(options.SourcePath));
        Assert.True(Path.IsPathFullyQualified(options.DestinationPath));
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData(".PNG")]
    public void Accepts_target_types_case_and_dot_insensitively(string target)
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", target);

        Assert.Equal(target.TrimStart('.').ToLowerInvariant(), options.TargetType);
    }

    [Fact]
    public void No_arguments_shows_help()
    {
        Assert.True(CliOptions.Parse([]).HelpRequested);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_flags_short_circuit_everything(string flag)
    {
        Assert.True(CliOptions.Parse(["-s", "in", flag]).HelpRequested);
    }

    [Theory]
    [InlineData(new[] { "-d", "out", "-t", "jpg" }, "-s")]
    [InlineData(new[] { "-s", "in", "-t", "jpg" }, "-d")]
    [InlineData(new[] { "-s", "in", "-d", "out" }, "-t")]
    public void Rejects_missing_required_options(string[] args, string expectedFlag)
    {
        ParseOutcome outcome = CliOptions.Parse(args);

        Assert.Null(outcome.Options);
        Assert.Contains(expectedFlag, outcome.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("high")]
    public void Rejects_out_of_range_quality(string quality)
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-q", quality]);

        Assert.Null(outcome.Options);
        Assert.Contains("-q", outcome.Error);
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("-trans")]
    [InlineData("-m")]
    public void Rejects_malformed_booleans(string flag)
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", flag, "yes"]);

        Assert.Null(outcome.Options);
        Assert.Contains("true or false", outcome.Error);
    }

    [Fact]
    public void Rejects_unsupported_target_type()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "heic"]);

        Assert.Null(outcome.Options);
        Assert.Contains("unsupported target type", outcome.Error);
    }

    [Fact]
    public void Rejects_unknown_option()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-z"]);

        Assert.Null(outcome.Options);
        Assert.Contains("unknown option", outcome.Error);
    }

    [Fact]
    public void Rejects_a_flag_with_no_value()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t"]);

        Assert.Null(outcome.Options);
        Assert.Contains("needs a value", outcome.Error);
    }

    [Fact]
    public void Rejects_a_malformed_background_colour()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-bg", "chartreuse"]);

        Assert.Null(outcome.Options);
        Assert.Contains("-bg", outcome.Error);
    }

    [Fact]
    public void Rejects_zero_parallelism()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-p", "0"]);

        Assert.Null(outcome.Options);
        Assert.Contains("-p", outcome.Error);
    }

    private static CliOptions Parse(params string[] args) =>
        CliOptions.Parse(args).Options ?? throw new InvalidOperationException("expected the arguments to parse");
}

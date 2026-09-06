using ImageMagick;

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
        Assert.Equal(new MagickColor("#102030"), options.Background);
    }

    [Fact]
    public void Applies_documented_defaults()
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", "jpg");

        Assert.False(options.Recursive);
        Assert.Equal(100, options.Quality);
        Assert.True(options.Overwrite);
        Assert.True(options.PreserveTransparency);
        Assert.True(options.PreserveMetadata);
        Assert.False(options.StopOnError);
        Assert.Equal(Environment.ProcessorCount, options.Parallelism);
        Assert.Equal(new MagickColor(MagickColors.White), options.Background);
    }

    [Fact]
    public void Resolves_paths_to_absolute()
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", "png");

        Assert.True(Path.IsPathFullyQualified(options.SourceRoot));
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
    public void A_folder_source_means_every_image_in_it()
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", "jpg");

        Assert.Equal("*", options.SourcePattern);
        Assert.False(options.SourceIsSingleFile);
        Assert.Equal(Path.GetFullPath("in"), options.SourceRoot);
    }

    [Fact]
    public void A_glob_source_splits_into_a_folder_and_a_pattern()
    {
        CliOptions options = Parse("-s", Path.Combine("in", "*.jpg"), "-d", "out", "-t", "png");

        Assert.Equal(Path.GetFullPath("in"), options.SourceRoot);
        Assert.Equal("*.jpg", options.SourcePattern);
        Assert.False(options.SourceIsSingleFile);
    }

    [Fact]
    public void A_bare_glob_resolves_against_the_current_folder()
    {
        CliOptions options = Parse("-s", "*.png", "-d", "out", "-t", "jpg");

        Assert.Equal(Path.GetFullPath("."), options.SourceRoot);
        Assert.Equal("*.png", options.SourcePattern);
    }

    [Fact]
    public void An_existing_file_source_is_recognised_as_a_single_file()
    {
        using TestWorkspace workspace = new();
        string file = workspace.WritePng("only-me.png");

        CliOptions options = Parse("-s", file, "-d", "out", "-t", "jpg");

        Assert.True(options.SourceIsSingleFile);
        Assert.Equal(workspace.Source, options.SourceRoot);
        Assert.Equal("only-me.png", options.SourcePattern);
    }

    [Fact]
    public void Without_d_the_destination_is_a_named_folder_inside_the_source()
    {
        CliOptions options = Parse("-s", "in", "-t", "webp");

        Assert.Equal(Path.Combine(Path.GetFullPath("in"), "Converted to webp"), options.DestinationPath);
    }

    [Fact]
    public void Without_d_a_single_file_source_writes_beside_the_file()
    {
        using TestWorkspace workspace = new();
        string file = workspace.WritePng("only-me.png");

        CliOptions options = Parse("-s", file, "-t", "jpg");

        Assert.Equal(Path.Combine(workspace.Source, "Converted to jpg"), options.DestinationPath);
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
    public void Rejects_a_format_that_can_be_read_but_not_written()
    {
        // HEIC decodes fine but nothing here can encode it, so it is a valid source, not a target.
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "heic"]);

        Assert.Null(outcome.Options);
        Assert.Contains("nothing can write", outcome.Error);
    }

    [Fact]
    public void Rejects_a_format_nothing_has_heard_of()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "squeeb"]);

        Assert.Null(outcome.Options);
        Assert.Contains("nothing can write", outcome.Error);
    }

    [Theory]
    [InlineData("avif")]
    [InlineData("jxl")]
    [InlineData("ico")]
    [InlineData("pdf")]
    [InlineData("psd")]
    public void Accepts_the_wider_target_range(string target)
    {
        CliOptions options = Parse("-s", "in", "-d", "out", "-t", target);

        Assert.Equal(target, options.TargetType);
    }

    [Fact]
    public void Formats_listing_is_its_own_outcome()
    {
        Assert.True(CliOptions.Parse(["-formats"]).FormatsRequested);
        Assert.True(CliOptions.Parse(["-s", "in", "-formats"]).FormatsRequested);
    }

    [Fact]
    public void A_named_extension_counts_as_explicit_intent()
    {
        Assert.True(Parse("-s", Path.Combine("in", "*.pdf"), "-d", "out", "-t", "jpg").SourceExtensionIsExplicit);
        Assert.False(Parse("-s", Path.Combine("in", "shot-*"), "-d", "out", "-t", "jpg").SourceExtensionIsExplicit);
        Assert.False(Parse("-s", "in", "-d", "out", "-t", "jpg").SourceExtensionIsExplicit);
    }

    [Fact]
    public void Rejects_unknown_option()
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-z"]);

        Assert.Null(outcome.Options);
        Assert.Contains("unknown option", outcome.Error);
    }

    [Fact]
    public void A_path_split_by_the_shell_says_so()
    {
        // What "-s C:\Property\11631 Suburban Rd" looks like by the time it reaches us.
        ParseOutcome outcome = CliOptions.Parse(["-s", @"C:\Property\11631", "Suburban", @"Rd\Pictures", "-d", "out", "-t", "jpg"]);

        Assert.Null(outcome.Options);
        Assert.Contains("double quotes", outcome.Error);
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
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-bg", "not-a-colour"]);

        Assert.Null(outcome.Options);
        Assert.Contains("-bg", outcome.Error);
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("chartreuse")]
    [InlineData("rgb(255,0,0)")]
    public void Accepts_hex_and_named_colours(string colour)
    {
        // ImageMagick understands colour names, so -bg is no longer hex-only.
        Assert.NotNull(CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg", "-bg", colour]).Options);
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

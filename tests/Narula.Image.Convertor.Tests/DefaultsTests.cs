using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class DefaultsTests
{
    [Fact]
    public void A_missing_settings_file_leaves_the_built_ins_alone()
    {
        using TestWorkspace workspace = new();
        StringWriter warnings = new();

        ToolDefaults defaults = Defaults.Load(Path.Combine(workspace.Root, "absent.json"), warnings);

        Assert.Equal(100, defaults.Quality);
        Assert.True(defaults.Overwrite);
        Assert.Equal("", warnings.ToString());
    }

    [Fact]
    public void A_settings_file_need_only_mention_what_it_changes()
    {
        ToolDefaults defaults = Load("""{ "quality": 72 }""", out string warnings);

        Assert.Equal(72, defaults.Quality);

        // Everything unmentioned keeps its built-in value.
        Assert.True(defaults.PreserveTransparency);
        Assert.Equal("#FFFFFF", defaults.Background);
        Assert.Equal("", warnings);
    }

    [Fact]
    public void Every_setting_can_be_overridden()
    {
        ToolDefaults defaults = Load("""
            {
              "quality": 60,
              "overwrite": false,
              "preserveTransparency": false,
              "preserveMetadata": false,
              "background": "black",
              "parallelism": 3,
              "destinationFolderFormat": "converted-{0}",
              "iconSizes": [16, 32]
            }
            """, out string warnings);

        Assert.Equal(60, defaults.Quality);
        Assert.False(defaults.Overwrite);
        Assert.False(defaults.PreserveTransparency);
        Assert.False(defaults.PreserveMetadata);
        Assert.Equal("black", defaults.Background);
        Assert.Equal(3, defaults.ResolvedParallelism);
        Assert.Equal("converted-{0}", defaults.DestinationFolderFormat);
        Assert.Equal(new List<int> { 16, 32 }, defaults.IconSizes.ToList());
        Assert.Equal("", warnings);
    }

    [Fact]
    public void Zero_parallelism_means_one_worker_per_processor()
    {
        ToolDefaults defaults = Load("""{ "parallelism": 0 }""", out _);

        Assert.Equal(Environment.ProcessorCount, defaults.ResolvedParallelism);
    }

    [Theory]
    [InlineData("""{ "quality": 0 }""", "quality")]
    [InlineData("""{ "quality": 101 }""", "quality")]
    [InlineData("""{ "parallelism": -2 }""", "parallelism")]
    [InlineData("""{ "background": "not-a-colour" }""", "background")]
    [InlineData("""{ "destinationFolderFormat": "no placeholder" }""", "destinationFolderFormat")]
    [InlineData("""{ "iconSizes": [16, 900] }""", "iconSizes")]
    public void An_unusable_value_warns_and_falls_back(string json, string expected)
    {
        ToolDefaults defaults = Load(json, out string warnings);

        Assert.Contains(expected, warnings);

        // The rest of the run is unaffected.
        Assert.Equal(ToolDefaults.BuiltIn.Quality, defaults.Quality);
        Assert.Equal(ToolDefaults.BuiltIn.Background, defaults.Background);
    }

    [Fact]
    public void A_broken_settings_file_warns_rather_than_stopping_the_run()
    {
        ToolDefaults defaults = Load("{ this is not json", out string warnings);

        Assert.Contains("ignoring", warnings);
        Assert.Equal(ToolDefaults.BuiltIn, defaults);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        ToolDefaults defaults = Load("""
            {
              // hand-edited files pick these up
              "quality": 90,
            }
            """, out string warnings);

        Assert.Equal(90, defaults.Quality);
        Assert.Equal("", warnings);
    }

    private static ToolDefaults Load(string json, out string warnings)
    {
        using TestWorkspace workspace = new();
        string path = Path.Combine(workspace.Root, "settings.json");
        Directory.CreateDirectory(workspace.Root);
        File.WriteAllText(path, json);

        StringWriter captured = new();
        ToolDefaults defaults = Defaults.Load(path, captured);
        warnings = captured.ToString();
        return defaults;
    }
}

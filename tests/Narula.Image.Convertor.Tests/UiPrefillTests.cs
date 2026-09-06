using Narula.Image.Convertor;
using Narula.Image.Convertor.UI;

namespace ImageConvertor.Tests;

/// <summary>
/// The lenient parser behind "img2img -ui ..." and the Explorer right-click entry. Its whole
/// job is to never refuse: whatever survives is offered, whatever does not is dropped silently,
/// because the window is about to be shown to someone who can see and fix every value.
/// </summary>
public class UiPrefillTests
{
    [Fact]
    public void Takes_the_values_it_understands()
    {
        using TestWorkspace workspace = new();

        UiPrefill prefill = UiPrefill.From(
            ["-s", workspace.Source, "-t", "png", "-r", "-q", "77", "-trans", "false", "-m", "false", "-o", "false"]);

        Assert.Equal(workspace.Source, prefill.Source);
        Assert.Equal("png", prefill.Target);
        Assert.True(prefill.Recursive);
        Assert.Equal(77, prefill.Quality);
        Assert.False(prefill.PreserveTransparency);
        Assert.False(prefill.PreserveMetadata);
        Assert.False(prefill.Overwrite);
    }

    [Fact]
    public void Drops_every_kind_of_rubbish_without_complaining()
    {
        UiPrefill prefill = UiPrefill.From(
        [
            "-t", "not-a-format",
            "-q", "900",
            "-iconsize", "999",
            "-bg", "not-a-colour",
            "-o", "perhaps",
            "-s", @"Z:\no\such\place",
            "--wat", "stray-word",
        ]);

        Assert.Null(prefill.Target);
        Assert.Null(prefill.Quality);
        Assert.Null(prefill.IconSize);
        Assert.Null(prefill.Background);
        Assert.Null(prefill.Overwrite);
        Assert.Null(prefill.Source);
        Assert.False(prefill.HasAnything);
    }

    [Fact]
    public void A_bad_value_never_swallows_the_flag_after_it()
    {
        // -s has nothing usable after it, so -t must survive rather than being eaten as its value.
        UiPrefill prefill = UiPrefill.From(["-s", "-t", "png"]);

        Assert.Null(prefill.Source);
        Assert.Equal("png", prefill.Target);
    }

    [Fact]
    public void A_rejected_value_is_not_then_read_as_a_flag()
    {
        UiPrefill prefill = UiPrefill.From(["-q", "-iconsize", "32"]);

        Assert.Null(prefill.Quality);
        Assert.Equal(32, prefill.IconSize);
    }

    [Fact]
    public void A_single_file_and_a_pattern_are_both_real_sources()
    {
        using TestWorkspace workspace = new();
        string file = workspace.WritePng("one.png");

        Assert.Equal(file, UiPrefill.From(["-s", file]).Source);
        Assert.Equal(
            Path.Combine(workspace.Source, "*.png"),
            UiPrefill.From(["-s", Path.Combine(workspace.Source, "*.png")]).Source);
    }

    [Fact]
    public void A_destination_does_not_have_to_exist_yet()
    {
        using TestWorkspace workspace = new();
        string wanted = Path.Combine(workspace.Root, "not", "created", "yet");

        Assert.Equal(wanted, UiPrefill.From(["-d", wanted]).Destination);
    }

    [Fact]
    public void Nothing_at_all_is_a_perfectly_good_answer()
    {
        UiPrefill prefill = UiPrefill.From([]);

        Assert.False(prefill.HasAnything);
    }

    [Fact]
    public void The_window_starts_from_what_survived()
    {
        using TestWorkspace workspace = new();

        MainViewModel model = new((_, _, _) => Task.FromResult(
            RunOutcome.Stopped(RunStatus.NothingToDo)));

        model.Apply(UiPrefill.From(["-s", workspace.Source, "-t", "webp", "-r", "-q", "60"]));

        Assert.Equal(workspace.Source, model.Source);
        Assert.Equal("webp", model.Target);
        Assert.True(model.Recursive);
        Assert.Equal(60, model.Quality);
        Assert.True(model.CanConvert);
    }

    [Fact]
    public void A_pattern_becomes_a_folder_and_a_filter_the_window_can_show()
    {
        using TestWorkspace workspace = new();

        MainViewModel model = new((_, _, _) => Task.FromResult(
            RunOutcome.Stopped(RunStatus.NothingToDo)));

        model.Apply(UiPrefill.From(["-s", Path.Combine(workspace.Source, "*.png")]));

        Assert.Equal(workspace.Source, model.Source);
        Assert.Equal(".png", model.SourceFilter);

        // And it goes back to the pattern the engine understands.
        Assert.Equal(Path.Combine(workspace.Source, "*.png"), model.EffectiveSource);
    }

    [Fact]
    public void Values_that_were_not_passed_keep_the_defaults()
    {
        using TestWorkspace workspace = new();

        MainViewModel model = new((_, _, _) => Task.FromResult(
            RunOutcome.Stopped(RunStatus.NothingToDo)));

        string target = model.Target;
        int quality = model.Quality;

        model.Apply(UiPrefill.From(["-s", workspace.Source]));

        Assert.Equal(target, model.Target);
        Assert.Equal(quality, model.Quality);
    }

    [Fact]
    public void The_ui_flag_is_recognised_and_removed()
    {
        Assert.True(UiLauncher.Requested(["-ui", "-s", "x"], out string[] rest));
        Assert.Equal(["-s", "x"], rest);

        Assert.False(UiLauncher.Requested(["-s", "x"], out string[] untouched));
        Assert.Equal(["-s", "x"], untouched);
    }
}

using Narula.Image.Convertor;
using Narula.Image.Convertor.UI;

namespace ImageConvertor.Tests;

public class MainViewModelTests
{
    [Fact]
    public void The_window_cannot_convert_until_a_source_is_chosen()
    {
        MainViewModel model = Model();

        Assert.False(model.CanConvert);

        model.Source = @"C:\photos";
        Assert.True(model.CanConvert);
    }

    [Fact]
    public void Choosing_a_source_shows_the_default_destination_immediately()
    {
        using TestWorkspace workspace = new();
        MainViewModel model = Model();

        // No button pressed: selecting a source is enough.
        model.Source = workspace.Source;

        Assert.Equal(Path.Combine(workspace.Source, "Converted to jpg"), model.DestinationHint);
        Assert.Contains("Default, beside the source.", model.DestinationNote);

        // And it follows the source when that changes.
        string other = Path.Combine(workspace.Root, "other");
        Directory.CreateDirectory(other);
        model.Source = other;

        Assert.Equal(Path.Combine(other, "Converted to jpg"), model.DestinationHint);
    }

    [Fact]
    public void The_destination_hint_shows_where_output_will_land()
    {
        MainViewModel model = Model();
        model.Source = Path.GetTempPath();
        model.Target = "webp";

        // Nobody should have to guess what the default does.
        Assert.EndsWith("Converted to webp", model.DestinationHint);

        model.Destination = @"C:\elsewhere";
        Assert.Equal(@"C:\elsewhere", model.DestinationHint);
    }

    [Theory]
    [InlineData("jpg", @"C:\photos", false)]
    [InlineData("ico", @"C:\photos", true)]
    [InlineData("png", @"C:\photos\app.ico", true)]
    public void Icon_sizes_only_appear_when_an_icon_is_involved(string target, string source, bool expected)
    {
        MainViewModel model = Model();
        model.Source = source;
        model.Target = target;

        Assert.Equal(expected, model.IconSizeApplies);
    }

    [Fact]
    public void The_window_speaks_to_the_engine_in_its_own_language()
    {
        MainViewModel model = Model();
        model.Source = @"C:\photos";
        model.Target = "webp";
        model.Recursive = true;
        model.Quality = 80;
        model.PreserveTransparency = false;
        model.Background = "#101010";

        string[] args = model.BuildRequest().ToArguments();

        Assert.Equal(@"C:\photos", Value(args, "-s"));
        Assert.Equal("webp", Value(args, "-t"));
        Assert.Equal("80", Value(args, "-q"));
        Assert.Equal("false", Value(args, "-trans"));
        Assert.Equal("#101010", Value(args, "-bg"));
        Assert.Contains("-r", args);

        // No -d means the engine applies its own default, rather than the window inventing one.
        Assert.DoesNotContain("-d", args);

        // Those arguments must survive the real parser.
        Assert.NotNull(CliOptions.Parse(args).Options);
    }

    [Fact]
    public void An_icon_size_is_only_sent_when_it_applies()
    {
        MainViewModel model = Model();
        model.Source = @"C:\photos";
        model.IconSize = 64;

        model.Target = "jpg";
        Assert.DoesNotContain("-iconsize", model.BuildRequest().ToArguments());

        model.Target = "ico";
        Assert.Equal("64", Value(model.BuildRequest().ToArguments(), "-iconsize"));
    }

    [Fact]
    public async Task A_finished_run_is_summarised_and_failures_listed()
    {
        WorkItem good = new(@"C:\a\one.png", @"C:\out\one.jpg", "one.png");
        WorkItem bad = new(@"C:\a\two.heic", @"C:\out\two.jpg", "two.heic");

        MainViewModel model = Model(new RunOutcome(
            RunStatus.Completed,
            null,
            CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg"]).Options,
            [ConversionResult.Converted(good, 2048, 1024), ConversionResult.Failed(bad, "unsupported format")],
            2,
            TimeSpan.FromSeconds(1.5)));

        model.Source = @"C:\photos";
        await model.ConvertAsync();

        Assert.Contains("1 converted", model.Summary);
        Assert.Contains("1 failed", model.Summary);
        Assert.Equal("Finished with failures.", model.Status);
        Assert.Contains("two.heic — unsupported format", model.Failures);
        Assert.False(model.Running);
    }

    [Fact]
    public async Task A_blocked_destination_explains_controlled_folder_access()
    {
        MainViewModel model = Model(RunOutcome.Stopped(
            RunStatus.DestinationUnavailable,
            "Could not find file C:/out",
            CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg"]).Options));

        model.Source = @"C:\photos";
        await model.ConvertAsync();

        Assert.Contains("Controlled Folder Access", model.Summary);
    }

    [Fact]
    public async Task Progress_is_reported_while_the_run_proceeds()
    {
        double seen = -1;

        MainViewModel model = new((args, progress, token) =>
        {
            progress.Report(3, 4, "three.png");
            return Task.FromResult(RunOutcome.Stopped(RunStatus.NothingToDo, "done"));
        });

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Progress)) seen = model.Progress;
        };

        model.Source = @"C:\photos";
        await model.ConvertAsync();

        Assert.Equal(75, seen);
    }

    [Fact]
    public void Every_writable_format_is_offered_with_the_common_ones_first()
    {
        MainViewModel model = Model();

        // Nearly two hundred, not a hand-picked ten.
        Assert.True(model.Targets.Count > 150, $"only {model.Targets.Count} targets offered");
        Assert.Equal("jpg", model.Targets[0]);
        Assert.Contains("avif", model.Targets);
        Assert.Contains("jxl", model.Targets);

        // Nothing offered that cannot actually be written.
        Assert.DoesNotContain("heic", model.Targets);
    }

    [Fact]
    public void The_chosen_format_is_described_in_the_library_own_words()
    {
        MainViewModel model = Model();

        model.Target = "jpg";
        Assert.Contains("Joint Photographic", model.TargetDescription);

        model.Target = "avif";
        Assert.Contains("AV1", model.TargetDescription);
    }

    [Fact]
    public void A_half_typed_format_cannot_be_converted()
    {
        MainViewModel model = Model();
        model.Source = @"C:\photos";

        model.Target = "jp";
        Assert.False(model.TargetIsWritable);
        Assert.False(model.CanConvert);
        Assert.Equal(string.Empty, model.TargetDescription);

        model.Target = "jpg";
        Assert.True(model.CanConvert);
    }

    [Fact]
    public void The_source_filter_defaults_to_everything_and_narrows_to_a_glob()
    {
        using TestWorkspace workspace = new();
        MainViewModel model = Model();
        model.Source = workspace.Source;

        Assert.Equal(MainViewModel.AllTypes, model.SourceFilter);
        Assert.Equal(workspace.Source, model.EffectiveSource);
        Assert.Contains(".heic", model.SourceFilters);
        Assert.Contains(".jpg", model.SourceFilters);

        model.SourceFilter = ".png";
        Assert.Equal(Path.Combine(workspace.Source, "*.png"), model.EffectiveSource);
    }

    [Fact]
    public async Task The_source_filter_really_narrows_what_gets_converted()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("one.png");
        workspace.WriteJpeg("two.jpg", 90);
        workspace.WriteJpeg("three.jpg", 90);

        MainViewModel model = new()
        {
            Source = workspace.Source,
            Destination = workspace.Destination,
            Target = "webp",
            SourceFilter = ".jpg",
        };

        await model.ConvertAsync();

        Assert.Contains("2 converted", model.Summary);
        Assert.False(File.Exists(workspace.InDestination("one.webp")));
    }

    [Fact]
    public async Task The_window_converts_real_files_through_the_real_engine()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("one.png");
        workspace.WritePng("two.png");

        // The parameterless constructor is what the window uses: no stub anywhere in this path.
        MainViewModel model = new()
        {
            Source = workspace.Source,
            Destination = workspace.Destination,
            Target = "jpg",
        };

        await model.ConvertAsync();

        Assert.Equal("Finished.", model.Status);
        Assert.Contains("2 converted", model.Summary);
        Assert.Empty(model.Failures);
        Assert.Equal(100, model.Progress);
        Assert.True(File.Exists(workspace.InDestination("one.jpg")));
        Assert.True(File.Exists(workspace.InDestination("two.jpg")));
    }

    [Fact]
    public void Use_default_shows_the_full_path_it_will_create()
    {
        using TestWorkspace workspace = new();
        MainViewModel model = Model();
        model.Source = workspace.Source;

        // Pick an explicit destination, then go back to the default.
        model.Destination = @"C:\elsewhere";
        Assert.Equal(@"C:\elsewhere", model.DestinationHint);

        model.Destination = string.Empty;

        string expected = Path.Combine(workspace.Source, "Converted to jpg");
        Assert.Equal(expected, model.DestinationHint);
        Assert.False(Directory.Exists(expected), "the point is that it shows a path that does not exist yet");

        // And says which destination is in force, plus that it need not exist yet.
        Assert.Contains("Default, beside the source.", model.DestinationNote);
        Assert.Contains("Created when you convert.", model.DestinationNote);

        Directory.CreateDirectory(expected);
        model.Destination = expected;
        Assert.Contains("Chosen folder.", model.DestinationNote);
        Assert.Contains("It already exists.", model.DestinationNote);
    }

    [Fact]
    public void The_destination_card_says_something_before_a_source_is_chosen()
    {
        MainViewModel model = Model();

        Assert.Contains("Choose a source", model.DestinationHint);
        Assert.Equal(string.Empty, model.DestinationNote);
    }

    [Fact]
    public void Cancel_is_only_available_while_a_run_is_in_flight()
    {
        TaskCompletionSource gate = new();

        MainViewModel model = new(async (args, progress, token) =>
        {
            await gate.Task;
            return RunOutcome.Stopped(RunStatus.NothingToDo, "done");
        });

        model.Source = @"C:\photos";

        Assert.False(model.CanCancel);
        Assert.True(model.IsIdle);

        Task running = model.ConvertAsync();

        Assert.True(model.CanCancel);
        Assert.False(model.IsIdle);
        Assert.False(model.CanConvert);

        gate.SetResult();
        running.Wait(TimeSpan.FromSeconds(5));

        Assert.False(model.CanCancel);
        Assert.True(model.IsIdle);
        Assert.True(model.CanConvert);
    }

    [Fact]
    public async Task Cancelling_stops_the_run_and_keeps_what_was_done()
    {
        MainViewModel model = new(async (args, progress, token) =>
        {
            // Stand in for a long conversion that watches the token.
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return RunOutcome.Stopped(RunStatus.Completed);
        });

        model.Source = @"C:\photos";
        Task running = model.ConvertAsync();

        while (!model.Running)
        {
            await Task.Delay(10);
        }

        model.Cancel();
        await running;

        Assert.Equal("Cancelled.", model.Status);
        Assert.False(model.Running);
        Assert.True(model.CanConvert);
    }

    [Fact]
    public async Task A_cancelled_run_still_reports_what_it_managed()
    {
        WorkItem done = new(@"C:\one.png", @"C:\out\one.jpg", "one.png");

        MainViewModel model = new(async (args, progress, token) =>
        {
            // Mirrors the engine: cancellation stops it handing out work, and it returns what
            // finished rather than throwing. The window has to notice the cancellation itself.
            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return new RunOutcome(
                RunStatus.Completed,
                null,
                CliOptions.Parse(["-s", "in", "-d", "out", "-t", "jpg"]).Options,
                [ConversionResult.Converted(done, 100, 50)],
                5,
                TimeSpan.FromSeconds(1));
        });

        model.Source = @"C:\photos";
        Task running = model.ConvertAsync();

        while (!model.Running)
        {
            await Task.Delay(10);
        }

        model.Cancel();
        await running;

        Assert.Contains("Cancelled", model.Status);
        Assert.Contains("1 converted", model.Summary);
    }

    private static MainViewModel Model(RunOutcome? outcome = null) =>
        new((args, progress, token) => Task.FromResult(outcome ?? RunOutcome.Stopped(RunStatus.NothingToDo, "nothing")));

    private static string? Value(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

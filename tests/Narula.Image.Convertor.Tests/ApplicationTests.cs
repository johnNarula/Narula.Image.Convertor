using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class ApplicationTests
{
    [Fact]
    public async Task A_clean_run_exits_zero_and_writes_every_file()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("one.png");
        workspace.WritePng("two.png");
        workspace.WritePng(Path.Combine("nested", "three.png"));

        int exitCode = await RunAsync(workspace, "-r", "-t", "jpg");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(workspace.InDestination("one.jpg")));
        Assert.True(File.Exists(workspace.InDestination("two.jpg")));
        Assert.True(File.Exists(workspace.InDestination("nested", "three.jpg")));
    }

    [Fact]
    public async Task A_failed_file_exits_one_but_the_rest_still_convert()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("good.png");
        workspace.WriteCorrupt("bad.png");

        int exitCode = await RunAsync(workspace, "-t", "jpg");

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(workspace.InDestination("good.jpg")));
        Assert.False(File.Exists(workspace.InDestination("bad.jpg")));
    }

    [Fact]
    public async Task Stopping_on_first_failure_still_exits_one()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCorrupt("bad.png");
        for (int i = 0; i < 20; i++)
        {
            workspace.WritePng($"good-{i:00}.png");
        }

        int exitCode = await RunAsync(workspace, "-t", "jpg", "-e", "-p", "1");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Bad_arguments_exit_two()
    {
        Assert.Equal(2, await Application.RunAsync(["-s", "somewhere", "-d", "elsewhere", "-t", "xcf"]));
    }

    [Fact]
    public async Task A_missing_source_folder_exits_two()
    {
        string missing = Path.Combine(Path.GetTempPath(), "nimg-tests", Guid.NewGuid().ToString("n"));

        Assert.Equal(2, await Application.RunAsync(["-s", missing, "-d", missing + "-out", "-t", "jpg"]));
    }

    [Fact]
    public async Task Help_exits_zero()
    {
        Assert.Equal(0, await Application.RunAsync(["-h"]));
        Assert.Equal(0, await Application.RunAsync([]));
    }

    [Fact]
    public async Task An_empty_source_folder_exits_zero_without_creating_noise()
    {
        using TestWorkspace workspace = new();
        workspace.WriteNonImage("readme.txt");

        int exitCode = await RunAsync(workspace, "-t", "jpg");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Collisions_are_reported_as_failures_rather_than_raced()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("logo.png");
        workspace.WriteJpeg("logo.jpg", 85);

        int exitCode = await RunAsync(workspace, "-t", "jpg", "-q", "85");

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(workspace.InDestination("logo.jpg")));
    }

    [Fact]
    public async Task Converting_in_place_over_the_source_folder_is_safe()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");
        string original = workspace.WriteJpeg("holiday.jpg", 85);
        byte[] before = File.ReadAllBytes(original);

        int exitCode = await Application.RunAsync(["-s", workspace.Source, "-d", workspace.Source, "-t", "jpg", "-q", "85"]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(workspace.Source, "photo.jpg")));
        Assert.Equal(before, File.ReadAllBytes(original));
    }

    [Fact]
    public async Task A_glob_source_converts_only_the_matching_files()
    {
        using TestWorkspace workspace = new();
        workspace.WriteJpeg("keep-one.jpg", 85);
        workspace.WriteJpeg("keep-two.jpg", 85);
        workspace.WritePng("leave-me.png");
        workspace.WriteAnimatedGif("leave-me.gif");

        int exitCode = await Application.RunAsync([
            "-s", Path.Combine(workspace.Source, "*.jpg"), "-d", workspace.Destination, "-t", "png"]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(workspace.InDestination("keep-one.png")));
        Assert.True(File.Exists(workspace.InDestination("keep-two.png")));
        Assert.Equal(2, Directory.GetFiles(workspace.Destination).Length);
    }

    [Fact]
    public async Task A_glob_with_r_matches_in_subfolders_too()
    {
        using TestWorkspace workspace = new();
        workspace.WriteJpeg("top.jpg", 85);
        workspace.WriteJpeg(Path.Combine("nested", "deep.jpg"), 85);
        workspace.WritePng(Path.Combine("nested", "ignored.png"));

        int exitCode = await Application.RunAsync([
            "-s", Path.Combine(workspace.Source, "*.jpg"), "-r", "-d", workspace.Destination, "-t", "png"]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(workspace.InDestination("top.png")));
        Assert.True(File.Exists(workspace.InDestination("nested", "deep.png")));
        Assert.False(File.Exists(workspace.InDestination("nested", "ignored.png")));
    }

    [Fact]
    public async Task A_single_file_source_converts_only_that_file()
    {
        using TestWorkspace workspace = new();
        string only = workspace.WritePng("only-me.png");
        workspace.WritePng("not-me.png");
        workspace.WritePng("nor-me.png");

        int exitCode = await Application.RunAsync(["-s", only, "-d", workspace.Destination, "-t", "jpg"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(["only-me.jpg"], Directory.GetFiles(workspace.Destination).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Without_d_output_lands_in_a_named_folder_inside_the_source()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        int exitCode = await Application.RunAsync(["-s", workspace.Source, "-t", "jpg"]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(workspace.Source, "Converted to jpg", "photo.jpg")));
    }

    [Fact]
    public async Task The_default_destination_is_not_picked_up_by_a_second_run()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        await Application.RunAsync(["-s", workspace.Source, "-r", "-t", "jpg"]);
        int exitCode = await Application.RunAsync(["-s", workspace.Source, "-r", "-t", "jpg"]);

        Assert.Equal(0, exitCode);

        // One source image in, one converted image out — not a second generation of output.
        string converted = Path.Combine(workspace.Source, "Converted to jpg");
        Assert.Single(Directory.GetFiles(converted, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_missing_single_file_exits_two()
    {
        using TestWorkspace workspace = new();

        int exitCode = await Application.RunAsync([
            "-s", Path.Combine(workspace.Source, "ghost.png"), "-d", workspace.Destination, "-t", "jpg"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task A_glob_matching_nothing_exits_zero()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        int exitCode = await Application.RunAsync([
            "-s", Path.Combine(workspace.Source, "*.tiff"), "-d", workspace.Destination, "-t", "jpg"]);

        Assert.Equal(0, exitCode);
    }

    private static Task<int> RunAsync(TestWorkspace workspace, params string[] extra) =>
        Application.RunAsync(["-s", workspace.Source, "-d", workspace.Destination, .. extra]);
}

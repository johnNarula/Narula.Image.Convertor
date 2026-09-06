using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class FileScannerTests
{
    [Fact]
    public void Picks_up_image_files_and_ignores_everything_else()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");
        workspace.WriteJpeg("holiday.jpg", 85);
        workspace.WriteNonImage("notes.txt");
        workspace.WriteNonImage("Thumbs.db");

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg"));

        Assert.Equal(["holiday.jpg", "photo.png"], scan.Items.Select(i => i.RelativePath).Order());
    }

    [Fact]
    public void Includes_formats_the_current_decoder_cannot_read()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCorrupt("from-phone.heic");

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg"));

        Assert.Single(scan.Items);
        Assert.Equal("from-phone.heic", scan.Items[0].RelativePath);
    }

    [Fact]
    public void Without_r_it_stays_in_the_top_folder()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("top.png");
        workspace.WritePng(Path.Combine("nested", "deep.png"));

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg"));

        Assert.Single(scan.Items);
        Assert.Equal("top.png", scan.Items[0].RelativePath);
    }

    [Fact]
    public void With_r_the_destination_mirrors_the_source_tree()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng(Path.Combine("a", "b", "deep.png"));

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg", "-r"));

        WorkItem item = Assert.Single(scan.Items);
        Assert.Equal(workspace.InDestination("a", "b", "deep.jpg"), item.DestinationPath);
    }

    [Fact]
    public void A_destination_inside_the_source_is_never_scanned_as_input()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("original.png");

        // Simulate a previous run having written output into a subfolder of the source.
        string nestedDestination = Path.Combine(workspace.Source, "converted");
        Directory.CreateDirectory(nestedDestination);
        File.Copy(Path.Combine(workspace.Source, "original.png"), Path.Combine(nestedDestination, "original.png"));

        CliOptions options = CliOptions.Parse(["-s", workspace.Source, "-r", "-d", nestedDestination, "-t", "jpg"]).Options!;
        ScanResult scan = FileScanner.Scan(options);

        Assert.Single(scan.Items);
        Assert.Equal("original.png", scan.Items[0].RelativePath);
    }

    [Fact]
    public void Two_sources_mapping_to_one_destination_are_resolved_deterministically()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("logo.png");
        workspace.WriteJpeg("logo.jpg", 85);

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg"));

        // The file already in the target format wins; the other is reported rather than raced.
        WorkItem kept = Assert.Single(scan.Items);
        Assert.Equal("logo.jpg", kept.RelativePath);

        ConversionResult rejected = Assert.Single(scan.Rejected);
        Assert.Equal("logo.png", rejected.Item.RelativePath);
        Assert.Equal(Outcome.Failed, rejected.Outcome);
        Assert.Contains("collides", rejected.Reason);
    }

    [Fact]
    public void Work_list_is_sorted_so_runs_are_reproducible()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("c.png");
        workspace.WritePng("a.png");
        workspace.WritePng("b.png");

        ScanResult scan = FileScanner.Scan(TestOptions.For(workspace, "jpg"));

        Assert.Equal(["a.png", "b.png", "c.png"], scan.Items.Select(i => i.RelativePath));
    }
}

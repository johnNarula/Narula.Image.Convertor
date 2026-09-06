using Narula.Image.Convertor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageConvertor.Tests;

public class ConversionTests
{
    [Fact]
    public async Task Converts_png_to_a_real_jpeg()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        ConversionResult result = await ConvertAsync(workspace, "photo.png", "jpg");

        Assert.Equal(Outcome.Converted, result.Outcome);

        string output = workspace.InDestination("photo.jpg");
        Assert.True(File.Exists(output));
        Assert.Equal("JPEG", Image.DetectFormat(output).Name);

        using Image<Rgba32> image = Image.Load<Rgba32>(output);
        Assert.Equal(64, image.Width);
        Assert.Equal(48, image.Height);
    }

    [Fact]
    public async Task Quality_reaches_the_encoder()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        ConversionResult low = await ConvertAsync(workspace, "photo.png", "jpg", "-q", "5");
        long lowBytes = new FileInfo(workspace.InDestination("photo.jpg")).Length;

        ConversionResult high = await ConvertAsync(workspace, "photo.png", "jpg", "-q", "97");
        long highBytes = new FileInfo(workspace.InDestination("photo.jpg")).Length;

        Assert.Equal(Outcome.Converted, low.Outcome);
        Assert.Equal(Outcome.Converted, high.Outcome);
        Assert.True(lowBytes < highBytes, $"expected q5 ({lowBytes}) to be smaller than q97 ({highBytes})");
    }

    [Fact]
    public async Task Transparency_is_flattened_when_the_target_cannot_carry_alpha()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        await ConvertAsync(workspace, "icon.png", "jpg", "-bg", "#FF0000");

        using Image<Rgba32> image = Image.Load<Rgba32>(workspace.InDestination("icon.jpg"));
        Rgba32 wasTransparent = image[60, 24];

        Assert.Equal(255, wasTransparent.A);
        Assert.True(wasTransparent.R > 200 && wasTransparent.G < 60 && wasTransparent.B < 60,
            $"expected the matte colour to show through, got {wasTransparent}");
    }

    [Fact]
    public async Task Transparency_survives_when_the_target_supports_it()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        await ConvertAsync(workspace, "icon.png", "webp");

        using Image<Rgba32> image = Image.Load<Rgba32>(workspace.InDestination("icon.webp"));
        Assert.Equal(0, image[60, 24].A);
    }

    [Fact]
    public async Task Trans_false_forces_a_re_encode_even_for_the_same_format()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        ConversionResult result = await ConvertAsync(workspace, "icon.png", "png", "-trans", "false", "-bg", "#00FF00");

        // Copying the original across would have silently ignored -trans.
        Assert.Equal(Outcome.Converted, result.Outcome);

        using Image<Rgba32> image = Image.Load<Rgba32>(workspace.InDestination("icon.png"));
        Rgba32 wasTransparent = image[60, 24];

        Assert.Equal(255, wasTransparent.A);
        Assert.True(wasTransparent.G > 200 && wasTransparent.R < 60, $"expected green matte, got {wasTransparent}");
    }

    [Fact]
    public async Task Same_format_at_the_same_quality_is_copied_through()
    {
        using TestWorkspace workspace = new();
        string source = workspace.WriteJpeg("holiday.jpg", 85);

        ConversionResult result = await ConvertAsync(workspace, "holiday.jpg", "jpg", "-q", "85");

        Assert.Equal(Outcome.Copied, result.Outcome);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(workspace.InDestination("holiday.jpg")));
    }

    [Fact]
    public async Task Same_format_at_a_different_quality_is_re_encoded()
    {
        using TestWorkspace workspace = new();
        string source = workspace.WriteJpeg("holiday.jpg", 92);

        ConversionResult result = await ConvertAsync(workspace, "holiday.jpg", "jpg", "-q", "40");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.True(result.BytesOut < new FileInfo(source).Length);
    }

    [Fact]
    public async Task Png_to_png_is_copied_through_by_default()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        ConversionResult result = await ConvertAsync(workspace, "photo.png", "png");

        Assert.Equal(Outcome.Copied, result.Outcome);
    }

    [Fact]
    public async Task Stripping_metadata_forces_a_re_encode_and_removes_the_profile()
    {
        using TestWorkspace workspace = new();
        workspace.WriteRotatedJpeg("tagged.jpg", orientation: 1, quality: 85);

        ConversionResult result = await ConvertAsync(workspace, "tagged.jpg", "jpg", "-q", "85", "-m", "false");

        Assert.Equal(Outcome.Converted, result.Outcome);

        ImageInfo info = Image.Identify(workspace.InDestination("tagged.jpg"));
        Assert.Null(info.Metadata.ExifProfile);
    }

    [Fact]
    public async Task A_rotated_photo_is_uprighted_rather_than_copied()
    {
        using TestWorkspace workspace = new();
        workspace.WriteRotatedJpeg("sideways.jpg", orientation: 6, quality: 85);

        ConversionResult result = await ConvertAsync(workspace, "sideways.jpg", "jpg", "-q", "85");

        // A straight copy would leave the photo dependent on the viewer honouring the EXIF tag.
        Assert.Equal(Outcome.Converted, result.Outcome);

        using Image<Rgba32> image = Image.Load<Rgba32>(workspace.InDestination("sideways.jpg"));
        Assert.Equal(48, image.Width);
        Assert.Equal(64, image.Height);

        ushort? orientation = image.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out IExifValue<ushort>? value) == true
            ? value!.Value
            : null;

        Assert.True(orientation is null or 1, $"expected the orientation tag to be cleared, got {orientation}");
    }

    [Fact]
    public async Task An_animated_gif_contributes_only_its_first_frame()
    {
        using TestWorkspace workspace = new();
        workspace.WriteAnimatedGif("spinner.gif", frames: 4);

        ConversionResult result = await ConvertAsync(workspace, "spinner.gif", "png");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using Image<Rgba32> image = Image.Load<Rgba32>(workspace.InDestination("spinner.png"));
        Assert.Equal(1, image.Frames.Count);
    }

    [Fact]
    public async Task Existing_destination_is_left_alone_when_overwrite_is_off()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        Directory.CreateDirectory(workspace.Destination);
        File.WriteAllText(workspace.InDestination("photo.jpg"), "previous run");

        ConversionResult result = await ConvertAsync(workspace, "photo.png", "jpg", "-o", "false");

        Assert.Equal(Outcome.Skipped, result.Outcome);
        Assert.Equal("previous run", File.ReadAllText(workspace.InDestination("photo.jpg")));
    }

    [Fact]
    public async Task Existing_destination_is_replaced_by_default()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        Directory.CreateDirectory(workspace.Destination);
        File.WriteAllText(workspace.InDestination("photo.jpg"), "previous run");

        ConversionResult result = await ConvertAsync(workspace, "photo.png", "jpg");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal("JPEG", Image.DetectFormat(workspace.InDestination("photo.jpg")).Name);
    }

    [Fact]
    public async Task A_corrupt_file_fails_without_leaving_anything_behind()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCorrupt("broken.png");

        ConversionResult result = await ConvertAsync(workspace, "broken.png", "jpg");

        Assert.Equal(Outcome.Failed, result.Outcome);
        Assert.False(File.Exists(workspace.InDestination("broken.jpg")));
        Assert.False(File.Exists(workspace.InDestination("broken.jpg.nimgtmp")));
    }

    [Fact]
    public async Task A_heic_file_is_reported_as_unsupported()
    {
        using TestWorkspace workspace = new();
        workspace.WriteCorrupt("from-phone.heic");

        ConversionResult result = await ConvertAsync(workspace, "from-phone.heic", "jpg");

        Assert.Equal(Outcome.Failed, result.Outcome);
        Assert.Equal("unsupported format (HEIC/AVIF)", result.Reason);
    }

    [Fact]
    public async Task Nested_destination_folders_are_created_on_demand()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng(Path.Combine("a", "b", "deep.png"));

        ConversionResult result = await ConvertAsync(workspace, Path.Combine("a", "b", "deep.png"), "jpg", "-r");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.True(File.Exists(workspace.InDestination("a", "b", "deep.jpg")));
    }

    private static async Task<ConversionResult> ConvertAsync(
        TestWorkspace workspace, string relativePath, string target, params string[] extra)
    {
        CliOptions options = TestOptions.For(workspace, target, extra);
        ScanResult scan = FileScanner.Scan(options);

        WorkItem item = scan.Items.Single(i =>
            string.Equals(i.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        return await new ImageSharpConverter().ConvertAsync(item, options, CancellationToken.None);
    }
}

using ImageMagick;

using Narula.Image.Convertor;

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
        Assert.Equal(MagickFormat.Jpeg, TestWorkspace.FormatOf(output));

        MagickImageInfo info = new(output);
        Assert.Equal((uint)TestWorkspace.Width, info.Width);
        Assert.Equal((uint)TestWorkspace.Height, info.Height);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("avif")]
    [InlineData("tiff")]
    [InlineData("bmp")]
    [InlineData("ico")]
    public async Task Converts_into_every_common_target(string target)
    {
        using TestWorkspace workspace = new();
        workspace.WriteJpeg("photo.jpg", 90);

        ConversionResult result = await ConvertAsync(workspace, "photo.jpg", target);

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.True(File.Exists(workspace.InDestination($"photo.{target}")));
    }

    [Fact]
    public async Task An_image_too_large_for_the_target_is_scaled_to_fit()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("huge.jpg", 1280, 800);

        ConversionResult result = await ConvertAsync(workspace, "huge.jpg", "ico");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal("resized to fit ico", result.Reason);

        // The largest entry is a square 256 box; the 8:5 picture sits inside it.
        using MagickImageCollection entries = new(workspace.InDestination("huge.ico"));
        IMagickImage<byte> largest = entries.OrderByDescending(e => e.Width).First();

        Assert.Equal(256u, largest.Width);
        Assert.Equal(256u, largest.Height);
    }

    [Fact]
    public async Task A_tall_image_is_scaled_by_its_longest_side()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("tall.jpg", 600, 3000);

        ConversionResult result = await ConvertAsync(workspace, "tall.jpg", "ico");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImageCollection entries = new(workspace.InDestination("tall.ico"));
        IMagickImage<byte> largest = entries.OrderByDescending(e => e.Height).First();

        Assert.Equal(256u, largest.Height);
        Assert.Equal(256u, largest.Width);
    }

    [Fact]
    public async Task An_image_already_within_the_cap_is_not_reported_as_resized()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("small-enough.jpg", 200, 256);

        ConversionResult result = await ConvertAsync(workspace, "small-enough.jpg", "ico");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Null(result.Reason);

        using MagickImageCollection entries = new(workspace.InDestination("small-enough.ico"));
        IMagickImage<byte> largest = entries.OrderByDescending(e => e.Height).First();

        // Squared up to 256, but the picture inside was never scaled down.
        Assert.Equal(256u, largest.Width);
        Assert.Equal(256u, largest.Height);
    }

    [Fact]
    public async Task Quality_reaches_the_encoder()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        await ConvertAsync(workspace, "photo.png", "jpg", "-q", "5");
        long lowBytes = new FileInfo(workspace.InDestination("photo.jpg")).Length;

        await ConvertAsync(workspace, "photo.png", "jpg", "-q", "97");
        long highBytes = new FileInfo(workspace.InDestination("photo.jpg")).Length;

        Assert.True(lowBytes < highBytes, $"expected q5 ({lowBytes}) to be smaller than q97 ({highBytes})");
    }

    [Fact]
    public async Task Transparency_is_flattened_when_the_target_cannot_carry_alpha()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        await ConvertAsync(workspace, "icon.png", "jpg", "-bg", "#FF0000");

        IMagickColor<byte> wasTransparent = TestWorkspace.PixelAt(workspace.InDestination("icon.jpg"), 60, 24);

        Assert.True(wasTransparent.R > 200 && wasTransparent.G < 60 && wasTransparent.B < 60,
            $"expected the matte colour to show through, got {wasTransparent.ToHexString()}");
    }

    [Fact]
    public async Task Transparency_survives_when_the_target_supports_it()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        await ConvertAsync(workspace, "icon.png", "webp");

        Assert.Equal(0, TestWorkspace.PixelAt(workspace.InDestination("icon.webp"), 60, 24).A);
    }

    [Fact]
    public async Task Trans_false_forces_a_re_encode_even_for_the_same_format()
    {
        using TestWorkspace workspace = new();
        workspace.WriteTransparentPng("icon.png");

        ConversionResult result = await ConvertAsync(workspace, "icon.png", "png", "-trans", "false", "-bg", "#00FF00");

        // Copying the original across would have silently ignored -trans.
        Assert.Equal(Outcome.Converted, result.Outcome);

        IMagickColor<byte> wasTransparent = TestWorkspace.PixelAt(workspace.InDestination("icon.png"), 60, 24);
        Assert.True(wasTransparent.G > 200 && wasTransparent.R < 60,
            $"expected green matte, got {wasTransparent.ToHexString()}");
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
    public async Task Jpg_and_jpeg_are_treated_as_the_same_format()
    {
        using TestWorkspace workspace = new();
        workspace.WriteJpeg("holiday.jpg", 85);

        ConversionResult result = await ConvertAsync(workspace, "holiday.jpg", "jpeg", "-q", "85");

        Assert.Equal(Outcome.Copied, result.Outcome);
        Assert.True(File.Exists(workspace.InDestination("holiday.jpeg")));
    }

    [Fact]
    public async Task Stripping_metadata_forces_a_re_encode_and_removes_the_profile()
    {
        using TestWorkspace workspace = new();
        workspace.WriteRotatedJpeg("tagged.jpg", OrientationType.TopLeft);

        ConversionResult result = await ConvertAsync(workspace, "tagged.jpg", "jpg", "-q", "85", "-m", "false");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImage image = new(workspace.InDestination("tagged.jpg"));
        Assert.Null(image.GetExifProfile());
    }

    [Fact]
    public async Task A_rotated_photo_is_uprighted_rather_than_copied()
    {
        using TestWorkspace workspace = new();
        workspace.WriteRotatedJpeg("sideways.jpg", OrientationType.RightTop);

        ConversionResult result = await ConvertAsync(workspace, "sideways.jpg", "jpg", "-q", "85");

        // A straight copy would leave the photo dependent on the viewer honouring the EXIF tag.
        Assert.Equal(Outcome.Converted, result.Outcome);

        MagickImageInfo info = new(workspace.InDestination("sideways.jpg"));
        Assert.Equal((uint)TestWorkspace.Height, info.Width);
        Assert.Equal((uint)TestWorkspace.Width, info.Height);
        Assert.True(info.Orientation is OrientationType.Undefined or OrientationType.TopLeft,
            $"expected the orientation tag to be cleared, got {info.Orientation}");
    }

    [Fact]
    public async Task An_animated_gif_contributes_only_its_first_frame_to_a_still_format()
    {
        using TestWorkspace workspace = new();
        workspace.WriteAnimatedGif("spinner.gif", frames: 4);

        ConversionResult result = await ConvertAsync(workspace, "spinner.gif", "jpg");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal(1, TestWorkspace.FrameCount(workspace.InDestination("spinner.jpg")));
    }

    [Fact]
    public async Task An_animated_gif_keeps_its_frames_in_a_format_that_can_hold_them()
    {
        using TestWorkspace workspace = new();
        workspace.WriteAnimatedGif("spinner.gif", frames: 4);

        ConversionResult result = await ConvertAsync(workspace, "spinner.gif", "webp");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal(4, TestWorkspace.FrameCount(workspace.InDestination("spinner.webp")));
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
        Assert.Equal(MagickFormat.Jpeg, TestWorkspace.FormatOf(workspace.InDestination("photo.jpg")));
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

        return await new MagickConverter(options).ConvertAsync(item, options, CancellationToken.None);
    }
}

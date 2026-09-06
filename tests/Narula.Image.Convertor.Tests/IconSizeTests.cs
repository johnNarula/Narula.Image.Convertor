using ImageMagick;

using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class IconSizeTests
{
    [Fact]
    public async Task A_multi_size_icon_converts_at_its_largest_size_by_default()
    {
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("app.ico", 16, 32, 64, 256);

        ConversionResult result = await ConvertAsync(workspace, "app.ico", "png");

        // Reading an icon as a single image takes whichever size is listed first — 16 here.
        Assert.Equal(Outcome.Converted, result.Outcome);

        MagickImageInfo info = new(workspace.InDestination("app.png"));
        Assert.Equal(256u, info.Width);
        Assert.Equal(256u, info.Height);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public async Task Iconsize_picks_the_requested_size(int size)
    {
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("app.ico", 16, 32, 64, 256);

        ConversionResult result = await ConvertAsync(workspace, "app.ico", "png", "-iconsize", size.ToString());

        Assert.Equal(Outcome.Converted, result.Outcome);

        MagickImageInfo info = new(workspace.InDestination("app.png"));
        Assert.Equal((uint)size, info.Width);
    }

    [Fact]
    public async Task Asking_for_a_size_the_icon_lacks_says_what_it_holds()
    {
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("app.ico", 16, 32);

        ConversionResult result = await ConvertAsync(workspace, "app.ico", "png", "-iconsize", "128");

        Assert.Equal(Outcome.Failed, result.Outcome);
        Assert.Contains("no 128px image inside", result.Reason);
        Assert.Contains("16x16", result.Reason);
        Assert.Contains("32x32", result.Reason);
    }

    [Fact]
    public async Task A_single_size_icon_is_unaffected()
    {
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("one.ico", 48);

        ConversionResult result = await ConvertAsync(workspace, "one.ico", "png");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal(48u, new MagickImageInfo(workspace.InDestination("one.png")).Width);
    }

    [Fact]
    public async Task An_animated_source_still_contributes_its_first_frame()
    {
        using TestWorkspace workspace = new();
        workspace.WriteAnimatedGif("spinner.gif", frames: 4);

        // Frame selection is about icons; an animation still collapses to its first frame.
        ConversionResult result = await ConvertAsync(workspace, "spinner.gif", "jpg");

        Assert.Equal(Outcome.Converted, result.Outcome);
        Assert.Equal(1, TestWorkspace.FrameCount(workspace.InDestination("spinner.jpg")));
    }

    [Theory]
    [InlineData("24")]
    [InlineData("512")]
    [InlineData("big")]
    public void Rejects_sizes_outside_the_conventional_set(string size)
    {
        ParseOutcome outcome = CliOptions.Parse(["-s", "in", "-d", "out", "-t", "png", "-iconsize", size]);

        Assert.Null(outcome.Options);
        Assert.Contains("-iconsize must be one of", outcome.Error);
    }

    [Fact]
    public void Omitting_iconsize_leaves_it_unset()
    {
        Assert.Null(CliOptions.Parse(["-s", "in", "-d", "out", "-t", "png"]).Options!.IconSize);
        Assert.Equal(64, CliOptions.Parse(["-s", "in", "-d", "out", "-t", "png", "-iconsize", "64"]).Options!.IconSize);
    }

    [Fact]
    public async Task Converting_to_ico_produces_every_conventional_size()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("photo.jpg", 1024, 1024);

        ConversionResult result = await ConvertAsync(workspace, "photo.jpg", "ico");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImageCollection entries = new(workspace.InDestination("photo.ico"));
        Assert.Equal(CliOptions.IconSizes.ToList(), entries.Select(e => (int)Math.Max(e.Width, e.Height)).ToList());
    }

    [Fact]
    public async Task An_icon_is_never_upscaled_past_its_source()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("small.jpg", 64, 64);

        await ConvertAsync(workspace, "small.jpg", "ico");

        using MagickImageCollection entries = new(workspace.InDestination("small.ico"));
        Assert.Equal(new List<int> { 16, 32, 48, 64 }, entries.Select(e => (int)Math.Max(e.Width, e.Height)).ToList());
    }

    [Fact]
    public async Task Iconsize_narrows_the_output_to_one_size()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("photo.jpg", 1024, 1024);

        await ConvertAsync(workspace, "photo.jpg", "ico", "-iconsize", "48");

        using MagickImageCollection entries = new(workspace.InDestination("photo.ico"));
        IMagickImage<byte> only = Assert.Single(entries);
        Assert.Equal(48u, only.Width);
    }

    [Fact]
    public async Task A_non_square_source_is_padded_to_square()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("wide.jpg", 1000, 500);

        await ConvertAsync(workspace, "wide.jpg", "ico");

        using MagickImageCollection entries = new(workspace.InDestination("wide.ico"));

        foreach (IMagickImage<byte> entry in entries)
        {
            Assert.Equal(entry.Width, entry.Height);
            Assert.True(entry.Width <= 256);
        }
    }

    [Fact]
    public async Task The_padding_on_a_squared_icon_is_transparent()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("wide.jpg", 1000, 500);

        await ConvertAsync(workspace, "wide.jpg", "ico");

        using MagickImageCollection entries = new(workspace.InDestination("wide.ico"));
        IMagickImage<byte> largest = entries.OrderByDescending(e => e.Width).First();

        // A 2:1 source in a 256 box leaves 64 rows of padding top and bottom.
        Assert.Equal(0, largest.GetPixels().GetPixel(128, 4).ToColor()!.A);
        Assert.Equal(0, largest.GetPixels().GetPixel(128, 252).ToColor()!.A);

        // The picture itself is untouched in the middle.
        Assert.Equal(255, largest.GetPixels().GetPixel(128, 128).ToColor()!.A);
    }

    [Fact]
    public async Task Trans_false_pads_with_the_matte_colour_instead()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("wide.jpg", 1000, 500);

        await ConvertAsync(workspace, "wide.jpg", "ico", "-trans", "false", "-bg", "#FF0000");

        using MagickImageCollection entries = new(workspace.InDestination("wide.ico"));
        IMagickImage<byte> largest = entries.OrderByDescending(e => e.Width).First();

        IMagickColor<byte> corner = largest.GetPixels().GetPixel(128, 4).ToColor()!;
        Assert.Equal(255, corner.A);
        Assert.True(corner.R > 200 && corner.G < 60, $"expected the matte colour, got {corner.ToHexString()}");
    }

    [Fact]
    public async Task Every_entry_of_a_written_icon_describes_itself()
    {
        using TestWorkspace workspace = new();
        workspace.WriteLargeJpeg("photo.jpg", 1024, 1024);

        await ConvertAsync(workspace, "photo.jpg", "ico");

        byte[] ico = File.ReadAllBytes(workspace.InDestination("photo.ico"));
        int count = BitConverter.ToUInt16(ico, 4);

        using MagickImageCollection entries = new(workspace.InDestination("photo.ico"));
        Assert.Equal(entries.Count, count);

        for (int i = 0; i < count; i++)
        {
            uint declared = ico[6 + (16 * i)] == 0 ? 256u : ico[6 + (16 * i)];
            Assert.Equal(entries[i].Width, declared);
        }
    }

    [Theory]
    [InlineData("webp")]
    [InlineData("tiff")]
    [InlineData("gif")]
    public async Task A_multi_frame_target_still_gets_one_size_from_an_icon(string target)
    {
        // An icon's frames are alternate sizes, not animation. Carrying them all across produced
        // a multi-page file whose first page — and so whose apparent size — was the 16px one.
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("app.ico", 16, 32, 64, 256);

        ConversionResult result = await ConvertAsync(workspace, "app.ico", target);

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImageCollection written = new(workspace.InDestination("app." + target));
        Assert.Single(written);
        Assert.Equal(256u, written[0].Width);
        Assert.Equal(256u, written[0].Height);
    }

    [Fact]
    public async Task Iconsize_still_chooses_for_a_multi_frame_target()
    {
        using TestWorkspace workspace = new();
        workspace.WriteMultiSizeIcon("app.ico", 16, 32, 64, 256);

        ConversionResult result = await ConvertAsync(workspace, "app.ico", "webp", "-iconsize", "32");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImageCollection written = new(workspace.InDestination("app.webp"));
        Assert.Single(written);
        Assert.Equal(32u, written[0].Width);
    }

    [Fact]
    public async Task A_genuinely_animated_source_keeps_all_of_its_frames()
    {
        // The icon rule must not cost real animation its frames.
        using TestWorkspace workspace = new();
        workspace.WriteAnimatedGif("moving.gif", frames: 3);

        ConversionResult result = await ConvertAsync(workspace, "moving.gif", "webp");

        Assert.Equal(Outcome.Converted, result.Outcome);

        using MagickImageCollection written = new(workspace.InDestination("moving.webp"));
        Assert.Equal(3, written.Count);
    }

    private static async Task<ConversionResult> ConvertAsync(
        TestWorkspace workspace, string relativePath, string target, params string[] extra)
    {
        CliOptions options = TestOptions.For(workspace, target, extra);
        ScanResult scan = FileScanner.Scan(options);
        WorkItem item = scan.Items.Single(i => string.Equals(i.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        return await new MagickConverter(options).ConvertAsync(item, options, CancellationToken.None);
    }
}

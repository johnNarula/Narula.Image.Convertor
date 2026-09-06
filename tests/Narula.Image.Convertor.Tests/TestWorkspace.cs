using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

using ISImage = SixLabors.ImageSharp.Image;

using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

/// <summary>
/// A throwaway folder plus helpers that write real image files into it. Fixtures are generated
/// rather than committed, so the repository stays free of binary test assets.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; }
    public string Source => Path.Combine(Root, "source");
    public string Destination => Path.Combine(Root, "destination");

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "nimg-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Source);
    }

    public string InSource(params string[] parts)
    {
        string full = Path.Combine([Source, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    public string InDestination(params string[] parts) => Path.Combine([Destination, .. parts]);

    public string WritePng(string relativePath, int width = 64, int height = 48)
    {
        string path = InSource(relativePath);
        using Image<Rgba32> image = Checkerboard(width, height, transparentRightHalf: false);
        image.Save(path, new PngEncoder());
        return path;
    }

    /// <summary>Left half opaque, right half fully transparent — enough to tell whether flattening happened.</summary>
    public string WriteTransparentPng(string relativePath, int width = 64, int height = 48)
    {
        string path = InSource(relativePath);
        using Image<Rgba32> image = Checkerboard(width, height, transparentRightHalf: true);
        image.Save(path, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
        return path;
    }

    public string WriteJpeg(string relativePath, int quality, int width = 64, int height = 48)
    {
        string path = InSource(relativePath);
        using Image<Rgba32> image = Checkerboard(width, height, transparentRightHalf: false);
        image.Save(path, new JpegEncoder { Quality = quality });
        return path;
    }

    public string WriteRotatedJpeg(string relativePath, ushort orientation, int quality = 85)
    {
        string path = InSource(relativePath);

        using Image<Rgba32> image = Checkerboard(64, 48, transparentRightHalf: false);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);

        image.Save(path, new JpegEncoder { Quality = quality });
        return path;
    }

    public string WriteAnimatedGif(string relativePath, int frames = 3)
    {
        string path = InSource(relativePath);

        using Image<Rgba32> image = new(32, 32, new Rgba32(220, 40, 40));

        for (int i = 1; i < frames; i++)
        {
            using Image<Rgba32> extra = new(32, 32, new Rgba32(40, 40, 220));
            image.Frames.AddFrame(extra.Frames.RootFrame);
        }

        image.Save(path, new GifEncoder());
        return path;
    }

    /// <summary>A file with an image extension and nothing but noise inside.</summary>
    public string WriteCorrupt(string relativePath)
    {
        string path = InSource(relativePath);
        File.WriteAllBytes(path, [.. Enumerable.Range(0, 512).Select(i => (byte)(i * 7 % 251))]);
        return path;
    }

    public string WriteNonImage(string relativePath, string content = "not an image")
    {
        string path = InSource(relativePath);
        File.WriteAllText(path, content);
        return path;
    }

    private static Image<Rgba32> Checkerboard(int width, int height, bool transparentRightHalf)
    {
        Image<Rgba32> image = new(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    bool light = ((x / 8) + (y / 8)) % 2 == 0;
                    byte alpha = transparentRightHalf && x >= width / 2 ? (byte)0 : (byte)255;

                    row[x] = light
                        ? new Rgba32(220, 180, 40, alpha)
                        : new Rgba32(60, 90, 200, alpha);
                }
            }
        });

        return image;
    }

    public static ISImage Load(string path) => ISImage.Load(path);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp folders are not worth failing a test run over.
        }
    }
}

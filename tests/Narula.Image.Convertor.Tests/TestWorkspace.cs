using ImageMagick;

using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

/// <summary>
/// A throwaway folder plus helpers that write real image files into it. Fixtures are generated
/// rather than committed, so the repository stays free of binary test assets.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public const int Width = 64;
    public const int Height = 48;

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

    public string WritePng(string relativePath)
    {
        string path = InSource(relativePath);
        using MagickImage image = Checkerboard(transparentRightHalf: false);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    /// <summary>Left half opaque, right half fully transparent — enough to tell whether flattening happened.</summary>
    public string WriteTransparentPng(string relativePath)
    {
        string path = InSource(relativePath);
        using MagickImage image = Checkerboard(transparentRightHalf: true);
        image.Write(path, MagickFormat.Png32);
        return path;
    }

    public string WriteJpeg(string relativePath, int quality)
    {
        string path = InSource(relativePath);
        using MagickImage image = Checkerboard(transparentRightHalf: false);
        image.Quality = (uint)quality;
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    /// <summary>A JPEG carrying an EXIF orientation tag, so uprighting has something to correct.</summary>
    public string WriteRotatedJpeg(string relativePath, OrientationType orientation, int quality = 85)
    {
        string path = InSource(relativePath);
        using MagickImage image = Checkerboard(transparentRightHalf: false);
        image.Quality = (uint)quality;

        // Setting Orientation alone does not create a profile to store it in, and a JPEG with no
        // EXIF block has no tag for the converter to find.
        ExifProfile exif = new();
        exif.SetValue(ExifTag.Orientation, (ushort)orientation);
        image.SetProfile(exif);
        image.Orientation = orientation;

        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    public string WriteAnimatedGif(string relativePath, int frames = 3)
    {
        string path = InSource(relativePath);
        using MagickImageCollection collection = [];

        for (int i = 0; i < frames; i++)
        {
            MagickImage frame = new(i % 2 == 0 ? MagickColors.Red : MagickColors.Blue, 32, 32);
            frame.AnimationDelay = 10;
            collection.Add(frame);
        }

        collection.Write(path, MagickFormat.Gif);
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

    private static MagickImage Checkerboard(bool transparentRightHalf)
    {
        byte[] pixels = new byte[Width * Height * 4];
        int i = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool light = ((x / 8) + (y / 8)) % 2 == 0;
                pixels[i++] = light ? (byte)220 : (byte)60;
                pixels[i++] = light ? (byte)180 : (byte)90;
                pixels[i++] = light ? (byte)40 : (byte)200;
                pixels[i++] = transparentRightHalf && x >= Width / 2 ? (byte)0 : (byte)255;
            }
        }

        MagickReadSettings settings = new()
        {
            Format = MagickFormat.Rgba,
            Width = Width,
            Height = Height,
            Depth = 8,
        };

        return new MagickImage(pixels, settings);
    }

    /// <summary>Reads one pixel's colour, for asserting on flattening and matte colours.</summary>
    public static IMagickColor<byte> PixelAt(string path, int x, int y)
    {
        using MagickImage image = new(path);
        return image.GetPixels().GetPixel(x, y).ToColor()!;
    }

    public static int FrameCount(string path)
    {
        using MagickImageCollection collection = new(path);
        return collection.Count;
    }

    public static MagickFormat FormatOf(string path) => new MagickImageInfo(path).Format;

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

#:package SixLabors.ImageSharp@3.1.12

// Draws the nImgConvertor application icon and assembles it into a multi-resolution .ico.
//
// Everything is rasterised by hand from normalised coordinates with 8x8 supersampling, so the
// only dependency is ImageSharp's PNG encoder. Sizes below 32px drop the detailed artwork for a
// bold arrow, because a photo frame with mountains turns to mush at 16 pixels.
//
//   dotnet run tools/GenerateIcon.cs -- src/Narula.Image.Convertor/icon.ico

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

string output = args.Length > 0 ? args[0] : "icon.ico";
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

Rgba32 TileTop = new(0x4F, 0x46, 0xE5);      // indigo
Rgba32 TileBottom = new(0x93, 0x33, 0xEA);   // violet
Rgba32 Paper = new(0xFF, 0xFF, 0xFF);
Rgba32 Accent = new(0xF5, 0x9E, 0x0B);       // amber

static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

Rgba32 Mix(Rgba32 a, Rgba32 b, float t)
{
    t = Clamp01(t);
    return new Rgba32(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        255);
}

// Signed distance to a rounded rectangle: negative inside.
static float RoundedRect(float px, float py, float x0, float y0, float x1, float y1, float radius)
{
    float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
    float hx = (x1 - x0) / 2 - radius, hy = (y1 - y0) / 2 - radius;
    float dx = MathF.Abs(px - cx) - hx, dy = MathF.Abs(py - cy) - hy;
    float outside = MathF.Sqrt(MathF.Max(dx, 0) * MathF.Max(dx, 0) + MathF.Max(dy, 0) * MathF.Max(dy, 0));
    return outside + MathF.Min(MathF.Max(dx, dy), 0) - radius;
}

static bool InCircle(float px, float py, float cx, float cy, float r) =>
    (px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r;

static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
{
    float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
    float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
    float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
    bool negative = d1 < 0 || d2 < 0 || d3 < 0;
    bool positive = d1 > 0 || d2 > 0 || d3 > 0;
    return !(negative && positive);
}

// Returns the colour at a normalised point, or null where the icon is transparent.
Rgba32? Sample(float x, float y, bool detailed)
{
    if (RoundedRect(x, y, 0.015f, 0.015f, 0.985f, 0.985f, 0.22f) > 0)
    {
        return null;
    }

    Rgba32 tile = Mix(TileTop, TileBottom, (x * 0.35f) + (y * 0.65f));

    if (!detailed)
    {
        // Small sizes: just the tile and a bold arrow.
        bool stem = x is >= 0.20f and <= 0.58f && y is >= 0.435f and <= 0.565f;
        bool head = InTriangle(x, y, 0.53f, 0.28f, 0.53f, 0.72f, 0.82f, 0.50f);
        return stem || head ? Paper : tile;
    }

    // Badge sits over the photo's corner; a ring of tile colour keeps the two apart.
    bool badgeRing = InCircle(x, y, 0.705f, 0.715f, 0.255f);
    bool badge = InCircle(x, y, 0.705f, 0.715f, 0.205f);

    if (badge)
    {
        bool stem = x is >= 0.60f and <= 0.745f && y is >= 0.687f and <= 0.743f;
        bool head = InTriangle(x, y, 0.715f, 0.640f, 0.715f, 0.790f, 0.825f, 0.715f);
        return stem || head ? Paper : Accent;
    }

    if (!badgeRing && RoundedRect(x, y, 0.145f, 0.185f, 0.715f, 0.640f, 0.065f) <= 0)
    {
        // Inside the photo: two ridges and a sun.
        if (InTriangle(x, y, 0.185f, 0.640f, 0.360f, 0.375f, 0.535f, 0.640f) ||
            InTriangle(x, y, 0.420f, 0.640f, 0.545f, 0.455f, 0.670f, 0.640f))
        {
            return TileTop;
        }

        return InCircle(x, y, 0.290f, 0.300f, 0.055f) ? Accent : Paper;
    }

    return tile;
}

List<byte[]> pngs = [];
List<Image<Rgba32>> images = [];

foreach (int size in sizes)
{
    bool detailed = size >= 32;
    const int Samples = 8;

    Image<Rgba32> image = new(size, size);

    image.ProcessPixelRows(accessor =>
    {
        for (int py = 0; py < size; py++)
        {
            Span<Rgba32> row = accessor.GetRowSpan(py);

            for (int px = 0; px < size; px++)
            {
                float r = 0, g = 0, b = 0, a = 0;

                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float nx = (px + (sx + 0.5f) / Samples) / size;
                        float ny = (py + (sy + 0.5f) / Samples) / size;

                        if (Sample(nx, ny, detailed) is { } colour)
                        {
                            r += colour.R; g += colour.G; b += colour.B; a += 255;
                        }
                    }
                }

                int total = Samples * Samples;
                float coverage = a / (255f * total);

                row[px] = coverage <= 0
                    ? new Rgba32(0, 0, 0, 0)
                    : new Rgba32(
                        (byte)(r / (total * coverage)),
                        (byte)(g / (total * coverage)),
                        (byte)(b / (total * coverage)),
                        (byte)MathF.Round(coverage * 255));
            }
        }
    });

    images.Add(image);

    if (Environment.GetEnvironmentVariable("ICON_DUMP") is { Length: > 0 } dump)
    {
        image.Save(Path.Combine(dump, $"size-{size}.png"), new PngEncoder());
    }

    using MemoryStream buffer = new();
    image.Save(buffer, new PngEncoder());
    pngs.Add(buffer.ToArray());
}

// A 32-bit bottom-up DIB plus an empty AND mask, which is what .ico entries hold.
static byte[] Dib(Image<Rgba32> image)
{
    int w = image.Width, h = image.Height;
    int maskStride = ((w + 31) / 32) * 4;

    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);

    writer.Write(40);            // biSize
    writer.Write(w);             // biWidth
    writer.Write(h * 2);         // biHeight: XOR bitmap plus AND mask
    writer.Write((short)1);      // biPlanes
    writer.Write((short)32);     // biBitCount
    writer.Write(0);             // biCompression = BI_RGB
    writer.Write(w * h * 4 + maskStride * h);
    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);

    image.ProcessPixelRows(accessor =>
    {
        for (int y = h - 1; y >= 0; y--)
        {
            Span<Rgba32> row = accessor.GetRowSpan(y);

            foreach (Rgba32 pixel in row)
            {
                writer.Write(pixel.B); writer.Write(pixel.G); writer.Write(pixel.R); writer.Write(pixel.A);
            }
        }
    });

    writer.Write(new byte[maskStride * h]);
    return stream.ToArray();
}

List<byte[]> payloads = [];

for (int i = 0; i < sizes.Length; i++)
{
    // PNG payloads are the norm at 256; everything smaller stays a DIB for maximum compatibility.
    payloads.Add(sizes[i] >= 256 ? pngs[i] : Dib(images[i]));
}

using (FileStream file = File.Create(output))
using (BinaryWriter writer = new(file))
{
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)sizes.Length);

    int offset = 6 + (16 * sizes.Length);

    for (int i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(payloads[i].Length);
        writer.Write(offset);
        offset += payloads[i].Length;
    }

    foreach (byte[] payload in payloads)
    {
        writer.Write(payload);
    }
}

foreach (Image<Rgba32> image in images)
{
    image.Dispose();
}

Console.WriteLine($"wrote {output} ({new FileInfo(output).Length:N0} bytes, {sizes.Length} sizes)");

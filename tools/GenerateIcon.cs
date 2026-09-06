#:package Magick.NET-Q8-AnyCPU@14.17.1

// Draws the nImgConvertor application icon and assembles it into a multi-resolution .ico.
//
// Artwork is rasterised by hand from normalised coordinates with 8x8 supersampling, so the only
// dependency is an encoder. Sizes below 32px switch to a simplified mark, because detail that
// reads at 256 turns to mush at 16.
//
//   dotnet run tools/GenerateIcon.cs -- <out.ico> [design]
//   dotnet run tools/GenerateIcon.cs -- --sheet <dir>      renders every design for comparison

using ImageMagick;

string[] designs = ["split", "swap", "stack", "cycle", "photo"];

if (args.Length >= 2 && args[0] == "--sheet")
{
    string dir = args[1];
    Directory.CreateDirectory(dir);

    foreach (string design in designs)
    {
        foreach (int size in new[] { 256, 48, 32, 16 })
        {
            Render(design, size).Write(Path.Combine(dir, $"{design}-{size}.png"), MagickFormat.Png);
        }
    }

    Console.WriteLine($"wrote {designs.Length * 4} previews to {dir}");
    return 0;
}

string output = args.Length > 0 ? args[0] : "icon.ico";
string chosen = args.Length > 1 ? args[1].ToLowerInvariant() : "split";

if (!designs.Contains(chosen))
{
    Console.Error.WriteLine($"unknown design '{chosen}'. Choose one of: {string.Join(", ", designs)}");
    return 2;
}

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
List<MagickImage> images = [.. sizes.Select(s => Render(chosen, s))];

WriteIco(output, sizes, images);

foreach (MagickImage image in images)
{
    image.Dispose();
}

Console.WriteLine($"wrote {output} ({new FileInfo(output).Length:N0} bytes, {sizes.Length} sizes, design '{chosen}')");
return 0;

// ---------------------------------------------------------------- palette

static (byte R, byte G, byte B) TileTop() => (0x4F, 0x46, 0xE5);     // indigo
static (byte R, byte G, byte B) TileBottom() => (0x93, 0x33, 0xEA);  // violet
static (byte R, byte G, byte B) Paper() => (0xFF, 0xFF, 0xFF);
static (byte R, byte G, byte B) Accent() => (0xF5, 0x9E, 0x0B);      // amber
static (byte R, byte G, byte B) Ink() => (0x31, 0x2E, 0x81);         // deep indigo

static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, float t)
{
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    return ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

// ---------------------------------------------------------------- geometry

static float RoundedRect(float px, float py, float x0, float y0, float x1, float y1, float radius)
{
    float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
    float hx = (x1 - x0) / 2 - radius, hy = (y1 - y0) / 2 - radius;
    float dx = MathF.Abs(px - cx) - hx, dy = MathF.Abs(py - cy) - hy;
    float outside = MathF.Sqrt(MathF.Max(dx, 0) * MathF.Max(dx, 0) + MathF.Max(dy, 0) * MathF.Max(dy, 0));
    return outside + MathF.Min(MathF.Max(dx, dy), 0) - radius;
}

static bool InRect(float px, float py, float x0, float y0, float x1, float y1) =>
    px >= x0 && px <= x1 && py >= y0 && py <= y1;

static bool InCircle(float px, float py, float cx, float cy, float r) =>
    (px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r;

static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
{
    float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
    float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
    float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
    return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
}

/// <summary>A slice of an annulus, for the curved arrows in the cycle design.</summary>
static bool InRing(float px, float py, float cx, float cy, float inner, float outer, float fromDeg, float toDeg)
{
    float dx = px - cx, dy = py - cy;
    float r = MathF.Sqrt(dx * dx + dy * dy);

    if (r < inner || r > outer)
    {
        return false;
    }

    float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
    if (angle < 0) angle += 360;
    if (fromDeg < 0) fromDeg += 360;
    if (toDeg < 0) toDeg += 360;

    return fromDeg <= toDeg ? angle >= fromDeg && angle <= toDeg : angle >= fromDeg || angle <= toDeg;
}

/// <summary>A right-pointing arrow: stem plus head, sized to a box.</summary>
static bool InArrow(float px, float py, float x0, float x1, float cy, float thickness, float headHeight)
{
    float headStart = x1 - (headHeight * 0.9f);
    bool stem = InRect(px, py, x0, cy - thickness / 2, headStart + 0.01f, cy + thickness / 2);
    bool head = InTriangle(px, py, headStart, cy - headHeight / 2, headStart, cy + headHeight / 2, x1, cy);
    return stem || head;
}

/// <summary>A photo: rounded card with a horizon of two ridges and a sun.</summary>
static (byte R, byte G, byte B)? Photo(
    float px, float py, float x0, float y0, float x1, float y1,
    (byte R, byte G, byte B) card, (byte R, byte G, byte B) ridge, (byte R, byte G, byte B) sun, bool detail)
{
    float w = x1 - x0, h = y1 - y0;

    if (RoundedRect(px, py, x0, y0, x1, y1, MathF.Min(w, h) * 0.16f) > 0)
    {
        return null;
    }

    if (!detail)
    {
        return card;
    }

    float baseY = y0 + h * 0.80f;

    if (InTriangle(px, py, x0 + w * 0.10f, baseY, x0 + w * 0.42f, y0 + h * 0.34f, x0 + w * 0.74f, baseY) ||
        InTriangle(px, py, x0 + w * 0.48f, baseY, x0 + w * 0.70f, y0 + h * 0.50f, x0 + w * 0.92f, baseY))
    {
        return ridge;
    }

    return InCircle(px, py, x0 + w * 0.28f, y0 + h * 0.26f, MathF.Min(w, h) * 0.11f) ? sun : card;
}

// ---------------------------------------------------------------- designs

static (byte R, byte G, byte B)? Sample(string design, float x, float y, bool detailed)
{
    if (RoundedRect(x, y, 0.015f, 0.015f, 0.985f, 0.985f, 0.22f) > 0)
    {
        return null;
    }

    var tile = Mix(TileTop(), TileBottom(), (x * 0.35f) + (y * 0.65f));

    return design switch
    {
        "swap" => Swap(x, y, detailed, tile),
        "split" => Split(x, y, detailed, tile),
        "stack" => Stack(x, y, detailed, tile),
        "cycle" => Cycle(x, y, detailed, tile),
        _ => PhotoBadge(x, y, detailed, tile),
    };
}

/// <summary>Two photos side by side, one white and one amber, with an arrow crossing between.</summary>
static (byte R, byte G, byte B)? Swap(float x, float y, bool detailed, (byte R, byte G, byte B) tile)
{
    if (!detailed)
    {
        // Two blocks and an arrow gap is unreadable small; keep the two-card idea only.
        if (RoundedRect(x, y, 0.10f, 0.30f, 0.44f, 0.70f, 0.07f) <= 0) return Paper();
        if (RoundedRect(x, y, 0.56f, 0.30f, 0.90f, 0.70f, 0.07f) <= 0) return Accent();
        return tile;
    }

    // The arrow carries a tile-coloured halo so it stays legible where it crosses the cards.
    bool arrow = InArrow(x, y, 0.34f, 0.70f, 0.50f, 0.075f, 0.26f);
    bool halo = InArrow(x, y, 0.30f, 0.745f, 0.50f, 0.165f, 0.36f);

    if (arrow) return Paper();

    if (!halo)
    {
        if (Photo(x, y, 0.05f, 0.27f, 0.45f, 0.73f, Paper(), Ink(), Accent(), true) is { } left) return left;
        if (Photo(x, y, 0.55f, 0.27f, 0.95f, 0.73f, Accent(), Ink(), Paper(), true) is { } right) return right;
    }

    return tile;
}

/// <summary>One photo, split down the middle: the same picture on either side of the conversion.</summary>
static (byte R, byte G, byte B)? Split(float x, float y, bool detailed, (byte R, byte G, byte B) tile)
{
    const float X0 = 0.13f, Y0 = 0.22f, X1 = 0.87f, Y1 = 0.78f;

    if (RoundedRect(x, y, X0, Y0, X1, Y1, 0.09f) > 0)
    {
        return tile;
    }

    // The seam is a slight diagonal so it reads as a transition rather than a fold.
    float seam = 0.50f + (y - 0.50f) * 0.16f;

    if (MathF.Abs(x - seam) < 0.018f)
    {
        return tile;
    }

    bool right = x > seam;
    var card = right ? Accent() : Paper();
    var ridge = right ? Paper() : Ink();

    if (!detailed)
    {
        return card;
    }

    float baseY = Y0 + (Y1 - Y0) * 0.80f;
    float w = X1 - X0;

    if (InTriangle(x, y, X0 + w * 0.08f, baseY, X0 + w * 0.40f, Y0 + (Y1 - Y0) * 0.30f, X0 + w * 0.72f, baseY) ||
        InTriangle(x, y, X0 + w * 0.52f, baseY, X0 + w * 0.72f, Y0 + (Y1 - Y0) * 0.48f, X0 + w * 0.94f, baseY))
    {
        return ridge;
    }

    return InCircle(x, y, X0 + w * 0.26f, Y0 + (Y1 - Y0) * 0.24f, 0.055f) ? (right ? Paper() : Accent()) : card;
}

/// <summary>A stack of photos with the converted one coming off the top.</summary>
static (byte R, byte G, byte B)? Stack(float x, float y, bool detailed, (byte R, byte G, byte B) tile)
{
    // Back sheet, offset up and left, reads as "the originals".
    if (RoundedRect(x, y, 0.14f, 0.14f, 0.66f, 0.58f, 0.08f) <= 0 &&
        RoundedRect(x, y, 0.26f, 0.28f, 0.86f, 0.84f, 0.09f) > 0)
    {
        return Mix(tile, Paper(), 0.55f);
    }

    if (Photo(x, y, 0.26f, 0.28f, 0.86f, 0.84f, Paper(), Ink(), Accent(), detailed) is { } front)
    {
        return front;
    }

    if (!detailed)
    {
        return tile;
    }

    // Small arrow tucked into the free corner, pointing away from the stack.
    if (InArrow(x, y, 0.06f, 0.30f, 0.80f, 0.055f, 0.17f))
    {
        return Accent();
    }

    return tile;
}

/// <summary>A photo wrapped by two arrows, the universal "convert" loop.</summary>
static (byte R, byte G, byte B)? Cycle(float x, float y, bool detailed, (byte R, byte G, byte B) tile)
{
    const float Inner = 0.355f, Outer = 0.435f;

    if (InRing(x, y, 0.5f, 0.5f, Inner, Outer, 205, 335) ||
        InRing(x, y, 0.5f, 0.5f, Inner, Outer, 25, 155))
    {
        return Accent();
    }

    float mid = (Inner + Outer) / 2, half = (Outer - Inner) * 1.5f;

    // Arrowheads at the open ends of each arc.
    if (InTriangle(x, y, 0.5f + mid - half, 0.5f - (mid * 0.62f), 0.5f + mid + half, 0.5f - (mid * 0.62f), 0.5f + mid, 0.5f - (mid * 0.62f) + half * 1.7f) ||
        InTriangle(x, y, 0.5f - mid - half, 0.5f + (mid * 0.62f), 0.5f - mid + half, 0.5f + (mid * 0.62f), 0.5f - mid, 0.5f + (mid * 0.62f) - half * 1.7f))
    {
        return Accent();
    }

    if (Photo(x, y, 0.28f, 0.30f, 0.72f, 0.70f, Paper(), Ink(), Accent(), detailed) is { } photo)
    {
        return photo;
    }

    return tile;
}

/// <summary>The original mark: one photo with a convert badge in the corner.</summary>
static (byte R, byte G, byte B)? PhotoBadge(float x, float y, bool detailed, (byte R, byte G, byte B) tile)
{
    if (!detailed)
    {
        return InArrow(x, y, 0.20f, 0.82f, 0.50f, 0.13f, 0.44f) ? Paper() : tile;
    }

    bool badgeRing = InCircle(x, y, 0.705f, 0.715f, 0.255f);

    if (InCircle(x, y, 0.705f, 0.715f, 0.205f))
    {
        return InArrow(x, y, 0.60f, 0.825f, 0.715f, 0.056f, 0.15f) ? Paper() : Accent();
    }

    if (!badgeRing && Photo(x, y, 0.145f, 0.185f, 0.715f, 0.640f, Paper(), TileTop(), Accent(), true) is { } photo)
    {
        return photo;
    }

    return tile;
}

// ---------------------------------------------------------------- rasteriser

static MagickImage Render(string design, int size)
{
    const int Samples = 8;
    bool detailed = size >= 32;

    byte[] pixels = new byte[size * size * 4];
    int i = 0;

    for (int py = 0; py < size; py++)
    {
        for (int px = 0; px < size; px++)
        {
            float r = 0, g = 0, b = 0;
            int covered = 0;

            for (int sy = 0; sy < Samples; sy++)
            {
                for (int sx = 0; sx < Samples; sx++)
                {
                    float nx = (px + ((sx + 0.5f) / Samples)) / size;
                    float ny = (py + ((sy + 0.5f) / Samples)) / size;

                    if (Sample(design, nx, ny, detailed) is { } colour)
                    {
                        r += colour.R; g += colour.G; b += colour.B; covered++;
                    }
                }
            }

            int total = Samples * Samples;

            if (covered == 0)
            {
                i += 4;
                continue;
            }

            pixels[i++] = (byte)(r / covered);
            pixels[i++] = (byte)(g / covered);
            pixels[i++] = (byte)(b / covered);
            pixels[i++] = (byte)MathF.Round(covered * 255f / total);
        }
    }

    MagickReadSettings settings = new()
    {
        Format = MagickFormat.Rgba,
        Width = (uint)size,
        Height = (uint)size,
        Depth = 8,
    };

    return new MagickImage(pixels, settings);
}

// ---------------------------------------------------------------- .ico container

// A 32-bit bottom-up DIB plus an empty AND mask, which is what .ico entries hold.
static byte[] Dib(MagickImage image)
{
    int w = (int)image.Width, h = (int)image.Height;
    int maskStride = ((w + 31) / 32) * 4;

    byte[] rgba = image.ToByteArray(MagickFormat.Rgba);

    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);

    writer.Write(40);
    writer.Write(w);
    writer.Write(h * 2);          // XOR bitmap plus AND mask
    writer.Write((short)1);
    writer.Write((short)32);
    writer.Write(0);
    writer.Write((w * h * 4) + (maskStride * h));
    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);

    for (int y = h - 1; y >= 0; y--)
    {
        for (int x = 0; x < w; x++)
        {
            int p = ((y * w) + x) * 4;
            writer.Write(rgba[p + 2]); writer.Write(rgba[p + 1]); writer.Write(rgba[p]); writer.Write(rgba[p + 3]);
        }
    }

    writer.Write(new byte[maskStride * h]);
    return stream.ToArray();
}

static void WriteIco(string path, int[] sizes, List<MagickImage> images)
{
    // PNG is the convention at 256 and keeps the file a quarter the size; a raw DIB at that
    // resolution is 270KB on its own. Everything smaller stays a DIB for maximum compatibility.
    List<byte[]> payloads = [.. images.Select(i => i.Width >= 256 ? i.ToByteArray(MagickFormat.Png) : Dib(i))];

    using FileStream file = File.Create(path);
    using BinaryWriter writer = new(file);

    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)sizes.Length);

    int offset = 6 + (16 * sizes.Length);

    for (int i = 0; i < sizes.Length; i++)
    {
        // An entry stores each dimension in one byte, where 0 means 256.
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

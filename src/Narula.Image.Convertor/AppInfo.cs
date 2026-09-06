using System.Reflection;
using System.Runtime.InteropServices;

using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// What the product says about itself. One source for the About box, -h and anywhere else that
/// needs a version, so the two executables and the installer cannot disagree about what they are.
/// </summary>
internal static class AppInfo
{
    /// <summary>The engine assembly's own attributes, which Directory.Build.props fills in.</summary>
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    public static string Product =>
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Image to Image Convertor";

    public static string Company =>
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "9thAct LLC";

    public static string Copyright =>
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>
    /// major.minor.YY.MMDD. Taken from the informational version because that one keeps the
    /// padded month-day; the numeric assembly version drops a leading zero, so a build made on
    /// the 6th of September reads 906 there and 0906 here.
    /// </summary>
    public static string Version
    {
        get
        {
            string? informational = Self
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return Self.GetName().Version?.ToString() ?? "0.0.0.0";
            }

            // Source control stamps a "+commit" suffix onto it, which nobody wants to read.
            int plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }
    }

    /// <summary>The imaging library doing the actual work; it names itself.</summary>
    public static string Engine => MagickNET.Version;

    /// <summary>
    /// The native ImageMagick build underneath Magick.NET, without the commit hash and web
    /// address it appends, which say nothing to anyone reading an About box.
    /// </summary>
    public static string ImageMagick
    {
        get
        {
            string[] words = MagickNET.ImageMagickVersion.Split(' ');
            return string.Join(' ', words.TakeWhile(w => !w.Contains(':') && !w.StartsWith("http")));
        }
    }

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string Platform => RuntimeInformation.OSDescription.Trim();

    /// <summary>How many formats this build can read and write, counted rather than claimed.</summary>
    public static (int Readable, int Writable) FormatCounts
    {
        get
        {
            int readable = 0, writable = 0;

            foreach (IMagickFormatInfo format in MagickNET.SupportedFormats)
            {
                if (format.SupportsReading) readable++;
                if (format.SupportsWriting) writable++;
            }

            return (readable, writable);
        }
    }
}

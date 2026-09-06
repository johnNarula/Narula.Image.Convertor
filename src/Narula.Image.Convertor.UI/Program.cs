using Avalonia;

using Narula.Image.Convertor;

namespace Narula.Image.Convertor.UI;

internal static class Program
{
    /// <summary>
    /// Starting values passed on the command line, by img2img -ui or by the Explorer entry the
    /// installer adds. Read before the window exists so it can open already filled in.
    /// </summary>
    public static UiPrefill Prefill { get; private set; } = new();

    /// <summary>
    /// Complaints from reading settings.json. A window has nowhere to print them as they happen,
    /// so they are kept and shown in About, which is where someone looking for them would look.
    /// </summary>
    public static string SettingsWarnings { get; private set; } = string.Empty;

    [STAThread]
    public static int Main(string[] args)
    {
        // The window used to skip this, which quietly meant settings.json applied to the command
        // line and not to the window: the same tool with two sets of defaults.
        StringWriter warnings = new();
        Defaults.Initialise(warnings);
        SettingsWarnings = warnings.ToString().Trim();

        Prefill = UiPrefill.From(args);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by name from Avalonia's tooling; keep the signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

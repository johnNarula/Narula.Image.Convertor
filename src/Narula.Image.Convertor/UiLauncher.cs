using System.Diagnostics;

namespace Narula.Image.Convertor;

/// <summary>
/// Starting the window when the command line is empty. The two are separate executables so each
/// keeps the right Windows subsystem: a console binary that opened windows would leave a console
/// behind it, and a windows binary run from a terminal would print nowhere.
/// </summary>
internal static class UiLauncher
{
    private const string ExecutableName = "img2imgUI";

    /// <summary>The window binary, expected beside this one. Null when it was not shipped.</summary>
    public static string? Locate()
    {
        string? directory = Path.GetDirectoryName(Environment.ProcessPath);

        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? [ExecutableName + ".exe"]
            : [ExecutableName, ExecutableName + ".exe"];

        return candidates
            .Select(name => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Launches the window and returns immediately. False means there was nothing to launch, and
    /// the caller should fall back to printing help rather than exiting silently.
    /// </summary>
    public static bool TryLaunch(TextWriter errors)
    {
        if (Locate() is not { } path)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            errors.WriteLine($"nImgConvertor: could not start {Path.GetFileName(path)}: {exception.Message}");
            return false;
        }
    }
}

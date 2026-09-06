using System.Diagnostics;

namespace Narula.Image.Convertor;

/// <summary>
/// Starting the window from the command line. The two are separate executables so each keeps the
/// right Windows subsystem: a console binary that opened windows would leave a console behind
/// it, and a windows binary run from a terminal would print nowhere.
/// </summary>
internal static class UiLauncher
{
    private const string ExecutableName = "img2imgUI";

    /// <summary>The flag that says "open the window instead of converting here".</summary>
    public const string Flag = "-ui";

    /// <summary>
    /// True when -ui was asked for, with that flag removed from <paramref name="rest"/> so what
    /// is left can be handed to the window as starting values.
    /// </summary>
    public static bool Requested(string[] args, out string[] rest)
    {
        bool found = args.Any(a => a.Equals(Flag, StringComparison.OrdinalIgnoreCase));

        rest = found
            ? [.. args.Where(a => !a.Equals(Flag, StringComparison.OrdinalIgnoreCase))]
            : args;

        return found;
    }

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
    /// Launches the window and returns immediately, passing <paramref name="arguments"/> through
    /// for it to start from. False means there was nothing to launch, and the caller should fall
    /// back to printing help rather than exiting silently.
    /// </summary>
    public static bool TryLaunch(TextWriter errors, params string[] arguments)
    {
        if (Locate() is not { } path)
        {
            return false;
        }

        ProcessStartInfo start = new(path) { UseShellExecute = true };

        // Passed as a list rather than as one string, so quoting is the runtime's problem. That
        // matters here because most of these values are paths, and paths have spaces in them.
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            Process.Start(start);
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            errors.WriteLine($"img2img: could not start {Path.GetFileName(path)}: {exception.Message}");
            return false;
        }
    }
}

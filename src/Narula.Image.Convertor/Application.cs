using System.Diagnostics;
using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// The whole run, from arguments to exit code. Separate from Program.cs so the exit-code
/// behaviour can be tested without launching a process.
/// </summary>
internal static class Application
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        Defaults.Initialise(Console.Error);

        ConsoleProgressSink progress = new();
        RunOutcome outcome = await ConversionRun.ExecuteAsync(args, progress, cancellationToken);

        switch (outcome.Status)
        {
            case RunStatus.HelpRequested:
                Console.WriteLine(CliOptions.HelpText);
                return 0;

            case RunStatus.FormatsRequested:
                ImageFormats.WriteListing(Console.Out);
                return 0;

            case RunStatus.InvalidArguments:
                Console.Error.WriteLine($"nImgConvertor: {outcome.Message}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Run 'nImgConvertor -h' for usage.");
                return 2;

            case RunStatus.SourceMissing or RunStatus.SourceUnreadable:
                Console.Error.WriteLine($"nImgConvertor: {outcome.Message}");
                return 2;

            case RunStatus.DestinationUnavailable:
                Console.Error.WriteLine($"nImgConvertor: could not create destination folder {outcome.Options!.DestinationPath}");
                Console.Error.WriteLine($"  {outcome.Message}");
                WriteProtectedFolderHelp(outcome.Options.DestinationPath);
                return 2;

            case RunStatus.NothingToDo:
                Console.WriteLine(outcome.Message);
                return 0;
        }

        Report.Render(outcome.Results, outcome.TotalPlanned, outcome.Elapsed, outcome.Options!);

        // A refused write is a policy decision elsewhere, not a bad image. Explain it once,
        // after the report, however many files it hit.
        if (outcome.WriteRefused)
        {
            WriteProtectedFolderHelp(outcome.Options!.DestinationPath);
        }

        return outcome.AnyFailed ? 1 : 0;
    }

    /// <summary>
    /// Defender's Controlled Folder Access protects Documents, Pictures, Desktop and their
    /// OneDrive equivalents, refusing writes from applications not on its allow list. Windows
    /// reports the refusal in ways that read like a bug here rather than a policy decision
    /// somewhere else, so say what it actually is and how to clear it.
    ///
    /// The allow list is per executable: renaming or rebuilding to a new path needs adding again.
    /// </summary>
    private static void WriteProtectedFolderHelp(string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"  Windows refused to write into {destination}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  If that is under Documents, Pictures, Desktop or OneDrive, this is most likely");
        Console.Error.WriteLine("  Windows Defender Controlled Folder Access. To allow this tool:");
        Console.Error.WriteLine("    Windows Security > Virus & threat protection > Ransomware protection >");
        Console.Error.WriteLine("    Manage ransomware protection > Allow an app through Controlled folder access");
        Console.Error.WriteLine($"    Add: {Environment.ProcessPath}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  The allow list is per executable, so a renamed or rebuilt exe needs adding again.");
    }
}
